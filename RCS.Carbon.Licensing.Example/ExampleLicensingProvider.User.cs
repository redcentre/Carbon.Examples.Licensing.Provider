using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Example.EFCore;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	public async Task<Shared.Entities.User?> ReadUser(string userId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users
			.AsNoTracking()
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id)
			.ConfigureAwait(false);
		return ToUser(user, true);
	}

	public async Task<Shared.Entities.User[]> ListUsers()
	{
		using var context = MakeContext();
		var users = await context.Users
			.AsNoTracking()
			.ToArrayAsync()
			.ConfigureAwait(false);
		return users.Select(u => ToUser(u, false)!).ToArray();
	}

	public async Task<Shared.Entities.User[]> ListUsers(params string[] realmIds)
	{
		var rids = realmIds.Select(x => int.Parse(x)).ToArray();
		using var context = MakeContext();
		return await context.Users
			.AsNoTracking()
			.Include(u => u.Realms)
			.Where(u => u.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(u => ToUser(u, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Shared.Entities.User> UpdateUser(Shared.Entities.User user)
	{
		using var context = MakeContext();
		User row;
		if (user.Id == null)
		{
			// A null Id on the incoming User has special meaning and indicates
			// we are creating a new User. The Id cannot be manually specified.
			Guid uid = Guid.NewGuid();
			int[] oldids = await context.Users.Select(u => u.Id).ToArrayAsync().ConfigureAwait(false);
			int randid;
			do
			{
				randid = Random.Shared.Next(10_000_000, 20_000_000);
			}
			while (oldids.Any(x => x == randid));
			row = new User
			{
				Id = randid,
				Uid = uid,
				Psw = null,     // The plaintext password might be needed for legacy usage, but avoid like the plague.
				PassHash = user.PassHash ?? HP(user.Psw, uid),
				Created = DateTime.UtcNow
			};
			context.Users.Add(row);
		}
		else
		{
			row = await context.Users.FirstAsync(u => u.Id.ToString() == user.Id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id {user.Id} not found for update");
			if (row.Uid == Guid.Empty)
			{
				row.Uid = Guid.NewGuid();
			}
		}
		row.Name = user.Name;
		row.ProviderId = user.ProviderId;
		row.Psw = user.Psw;
		row.PassHash = HP(user.Psw, row.Uid);
		row.Email = user.Email;
		row.EntityId = user.EntityId;
		row.CloudCustomerNames = user.CloudCustomerNames?.Length > 0 ? string.Join(" ", user.CloudCustomerNames) : null;
		row.JobNames = user.JobNames?.Length > 0 ? string.Join(" ", user.JobNames) : null;
		row.VartreeNames = user.VartreeNames?.Length > 0 ? string.Join(" ", user.VartreeNames) : null;
		row.DashboardNames = user.DashboardNames?.Length > 0 ? string.Join(" ", user.DashboardNames) : null;
		row.DataLocation = (int?)user.DataLocation;
		row.Sequence = user.Sequence;
		row.Comment = user.Comment;
		row.Roles = user.Roles?.Length > 0 ? string.Join(" ", user.Roles) : null;
		row.Filter = user.Filter;
		row.LoginMacs = user.LoginMacs;
		row.LoginCount = user.LoginCount;
		row.LoginMax = user.LoginMax;
		row.LastLogin = user.LastLogin;
		row.Sunset = user.Sunset;
		row.MaxJobs = user.MaxJobs;
		row.Version = user.Version;
		row.MinVersion = user.MinVersion;
		row.IsDisabled = user.IsDisabled;
		await context.SaveChangesAsync().ConfigureAwait(false);
		row = await context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).FirstAsync(u => u.Id == row.Id);
		return await RereadUser(context, row.Id).ConfigureAwait(false);
	}

	public async Task<int> DeleteUser(string userId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
		if (user == null) return 0;
		foreach (var job in user.Jobs.ToArray())
		{
			user.Jobs.Remove(job);
		}
		foreach (var cust in user.Customers.ToArray())
		{
			user.Customers.Remove(cust);
		}
		context.Users.Remove(user);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Connects a User to Customers.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- Connecting a User⮞Customer means that any User⮞Job connects to Job children of the
	/// Customer are now redundant and must be removed.
	/// </remarks>
	public async Task<Shared.Entities.User?> ConnectUserChildCustomers(string userId, string[] customerIds)
	{
		Log($"ConnectUserChildCustomers({userId},{Join(customerIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.FirstOrDefaultAsync(u => u.Id == uid)
			.ConfigureAwait(false);
		if (user == null) return null;
		int[] cids = customerIds.Select(x => int.Parse(x)).ToArray();
		var custquery = context.Customers
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Where(c => cids.Contains(c.Id));
		foreach (var cust in custquery)
		{
			if (!cust.Users.Any(cu => cu.Id == uid))
			{
				Log($"ConnectJobChildUsers | Cust {cust.Id} {cust.Name} ADD User {user.Id} {user.Name}");
				cust.Users.Add(user);
			}
			int[] custjids = cust.Jobs.Select(j => j.Id).ToArray();
			var deljobs = user.Jobs.Where(j => custjids.Contains(j.Id)).ToArray();
			foreach (var deljob in deljobs)
			{
				Log($"ConnectJobChildUsers | User {user.Id} {user.Name} DEL Job {deljob.Id} {deljob.Name}");
				user.Jobs.Remove(deljob);
			}
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	/// <summary>
	/// Disconnects a User from a Customer.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- None required. There should be no connections to Customer child Jobs, and even if
	/// there are then it's harmless to let them remain an they can be dealt with separately.
	/// </remarks>
	public async Task<Shared.Entities.User?> DisconnectUserChildCustomer(string userId, string customerId)
	{
		Log($"DisconnectUserChildCustomer({userId},{customerId})");
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Customers).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		int cid = int.Parse(customerId);
		var cust = user.Customers.FirstOrDefault(c => c.Id == cid);
		if (cust != null)
		{
			Log($"DisconnectUserChildCustomer | User {user.Id} {user.Name} DEL Cust {cust.Id} {cust.Name}");
			user.Customers.Remove(cust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	/// <summary>
	/// Connects a User to Jobs.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- If connecting a User⮞Job results in some joins, then any existing connection
	/// from the User to the Job's parent customer is redundant and can be removed.
	/// </remarks>
	public async Task<Shared.Entities.User?> ConnectUserChildJobs(string userId, string[] jobIds)
	{
		Log($"ConnectUserChildJobs({userId},{Join(jobIds)})");
		int uid = int.Parse(userId);
		int[] jids = jobIds.Select(j => int.Parse(j)).ToArray();
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.FirstOrDefaultAsync(u => u.Id.ToString() == userId)
			.ConfigureAwait(false);
		if (user == null) return null;
		var jobquery = context.Jobs
			.Include(j => j.Users)
			.Include(j => j.Customer)
			.Where(j => jids.Contains(j.Id))
			.ToArray();
		foreach (var job in jobquery)
		{
			int[] newUserJids = user.Jobs.Select(uj => uj.Id).Concat(new int[] { job.Id }).Distinct().ToArray();
			var cust = context.Customers
				.Include(c => c.Jobs)
				.First(c => c.Id == job.CustomerId);
			int[] custjids = cust.Jobs.Select(cj => cj.Id).ToArray();
			// If the new user job ids contains all the customer's child job ids? (TRICKY)
			if (custjids.Intersect(newUserJids).Count() == custjids.Length)
			{
				if (!user.Customers.Any(uc => uc.Id == cust.Id))
				{
					Log($"ConnectUserChildJobs | User {user.Id} {user.Name} ADD Cust {cust.Id} {cust.Name}");
					user.Customers.Add(cust);
				}
				var remjobs = user.Jobs.Where(uj => custjids.Contains(uj.Id)).ToArray();
				foreach (var remjob in remjobs)
				{
					Log($"ConnectUserChildJobs | User {user.Id} {user.Name} DEL Job {remjob.Id} {remjob.Name}");
					user.Jobs.Remove(remjob);
				}
			}
			else
			{
				if (!user.Jobs.Any(uj => uj.Id == job.Id))
				{
					Log($"ConnectUserChildJobs | User {user.Id} {user.Name} ADD Job {job.Id} {job.Name}");
					user.Jobs.Add(job);
				}
				var delcust = user.Customers.FirstOrDefault(c => c.Id == cust.Id);
				if (delcust != null)
				{
					Log($"ConnectUserChildJobs | User {user.Id} {user.Name} DEL Cust {delcust.Id} {delcust.Name}");
					user.Customers.Remove(delcust);
				}
			}
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	/// <summary>
	/// Disconnect a User from a Job.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- This is a rather special case. If the User is not connected to a Customer then the Job
	/// is simply removed from the set of User⮞Job connections. If the User is connected to a Customer then
	/// there should not be any connections the Customer's child jobs, and in this case a set of Job connections
	/// is created which excludes the disconnecting job.
	/// </remarks>
	public async Task<Shared.Entities.User?> DisconnectUserChildJob(string userId, string jobId)
	{
		Log($"DisconnectUserChildJob({userId},{jobId})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users
			.Include(c => c.Jobs)
			.FirstOrDefaultAsync(u => u.Id.ToString() == userId);
		if (user == null) return null;
		int jid = int.Parse(jobId);
		var job = context.Jobs.First(j => j.Id == jid);
		if (job == null) return null;
		var cust = context.Customers
			.Include(c => c.Jobs)
			.First(c => c.Id == job.CustomerId);
		if (user.Customers.Any(uc => uc.Id == cust.Id))
		{
			// The User is connected to the Job's parent Customer.
			Debug.Assert(!user.Jobs.Any());
			var addjobs = cust.Jobs.Where(j => j.Id != jid);
			foreach (var addjob in addjobs)
			{
				Log($"DisconnectUserChildJob | User {user.Id} {user.Name} ADD Job {addjob.Id} {addjob.Name}");
				user.Jobs.Add(addjob);
			}
			var delcust = user.Customers.First(c => c.Id == cust.Id);
			Log($"DisconnectUserChildJob | User {user.Id} {user.Name} DEL Cust {delcust.Id} {delcust.Name}");
			user.Customers.Remove(delcust);
		}
		else
		{
			// The User is not connected to the Job's parent Customer.
			// It can only have a set of Job connections (maybe empty).
			var deljob = user.Jobs.FirstOrDefault(j => j.Id == jid);
			if (deljob != null)
			{
				Log($"DisconnectUserChildJob | User {user.Id} {user.Name} ADD Job {deljob.Id} {deljob.Name}");
				user.Jobs.Remove(deljob);
			}
		}

		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	static async Task<Shared.Entities.User?> RereadUser(ExampleContext context, int userId)
	{
		var cust = await context.Users.AsNoTracking().Include(c => c.Customers).Include(c => c.Jobs).FirstOrDefaultAsync(u => u.Id == userId).ConfigureAwait(false);
		return ToUser(cust, true);
	}

	static byte[]? HP(string p, Guid u)
	{
		if (p == null) return null;
		using var deriver = new Rfc2898DeriveBytes(p, u.ToByteArray(), 15000, HashAlgorithmName.SHA1);
		return deriver.GetBytes(16);
	}
}
