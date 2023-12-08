using System;
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
		var user = await context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
		return ToUser(user, true);
	}

	public async Task<Shared.Entities.User[]> ListUsers()
	{
		using var context = MakeContext();
		var users = await context.Users.AsNoTracking().ToArrayAsync().ConfigureAwait(false);
		return users.Select(u => ToUser(u, false)!).ToArray();
	}

	public async Task<Shared.Entities.User[]> ListUsers(params string[] realmIds)
	{
		var rids = realmIds.Select(x => int.Parse(x)).ToArray();
		using var context = MakeContext();
		return await context.Users
			.AsNoTracking()
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

	public async Task<Shared.Entities.User?> ConnectUserChildCustomers(string userId, string[] customerIds)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Customers).FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
		if (user == null) return null;
		int[] cids = customerIds.Select(c => int.Parse(c)).ToArray();
		var addcusts = context.Customers.Where(c => cids.Contains(c.Id)).ToArray();
		foreach (var addcust in addcusts)
		{
			user.Customers.Add(addcust);
		}
		user.CloudCustomerNames = user.Customers.Count == 0 ? null : string.Join(" ", user.Customers.Select(c => c.Name));
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	public async Task<Shared.Entities.User?> DisconnectUserChildCustomer(string userId, string customerId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Customers).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		int cid = int.Parse(customerId);
		var cust = user.Customers.FirstOrDefault(c => c.Id == cid);
		if (cust != null)
		{
			user.Customers.Remove(cust);
		}
		user.CloudCustomerNames = user.Customers.Count == 0 ? null : string.Join(" ", user.Customers.Select(c => c.Name));
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	public async Task<Shared.Entities.User?> ConnectUserChildJobs(string userId, string[] jobIds)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		int[] jids = jobIds.Select(j => int.Parse(j)).ToArray();
		var addjobs = context.Jobs.Where(j => jids.Contains(j.Id)).ToArray();
		foreach (var addjob in addjobs)
		{
			user.Jobs.Add(addjob);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.User?> DisconnectUserChildJob(string userId, string jobId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(c => c.Jobs).FirstOrDefaultAsync(u => u.Id.ToString() == userId);
		if (user == null) return null;
		int jid = int.Parse(jobId);
		var job = user.Jobs.FirstOrDefault(j => j.Id == jid);
		if (job != null)
		{
			user.Jobs.Remove(job);
			await context.SaveChangesAsync();
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
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
