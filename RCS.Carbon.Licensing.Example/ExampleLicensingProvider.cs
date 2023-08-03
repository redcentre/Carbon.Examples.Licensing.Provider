using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Example.EFCore;
using RCS.Carbon.Licensing.Shared;
using RCS.Carbon.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

/// <summary>
/// An example of a simple Carbon licensing provider. This provider uses a SQL Server database as the backing
/// storage for licensing information about Users, Jobs and Customers.
/// </summary>
public class ExampleLicensingProvider : ILicensingProvider
{
	readonly string? _connect;
	const string GuestAccountName = "guest";
	const string MimeUrl = "https://systemrcs.blob.core.windows.net/reference/mime-types.xml";
	const int MaxParallelUploads = 4;
	const string MimeBinary = "application/octet-stream";
	const string MimeText = "text/plain";
	readonly static string[] Excludes = new string[]	// TODO The upload ignores must come from the caller
	{
		@".vs\", @".git\", @"bin\", @"obj\", @"release\", @"debug\", @"packages\"
	};
	readonly static string[] LowerDirs = new string[]
	{
		@"\CaseData\", @"\Specs\"
	};
	readonly Decoder utf8dec = Encoding.UTF8.GetDecoder();
	readonly char[] NonTextChars = Enumerable.Range(0, 32).Except(new int[] { 9, 10, 13 }).Select(e => (char)e).ToArray();

	[Description("Example licensing provider using a SQL Server database")]
	public ExampleLicensingProvider(
		[Required]
		[Description("ADO database connection string")]
		string adoConnectionString
	)
	{
		_connect = adoConnectionString ?? throw new ArgumentNullException(nameof(adoConnectionString));
	}

	public string Name => "Example Licensing Provider";

	public string Description
	{
		get
		{
			var t = GetType();
			var asm = t.Assembly;
			var an = asm.GetName();
			var desc = asm.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description;
			return $"{t.Name} {an.Version} - {desc}";
		}
	}

	#region Authentication

	public async Task<LicenceFull> LoginId(string userId, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Id == id) ?? throw new CarbonException(200, $"User Id '{userId}' does not exist");
		if (user.Psw != null & user.Psw != password)
		{
			throw new CarbonException(201, $"User Id '{userId}' incorrect password");
		}
		return UserToFull(user);
	}

	public async Task<LicenceFull> LoginName(string userName, string? password, bool skipCache = false)
	{
		string upname = userName.ToUpper();
		using var context = MakeContext();
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Name.ToUpper() == upname) ?? throw new CarbonException(100, $"User Name '{userName}' does not exist");
		if (user.Psw != null & user.Psw != password)
		{
			throw new CarbonException(101, $"User Name '{userName}' incorrect password");
		}
		return UserToFull(user);
	}

	public async Task<LicenceFull> GetFreeLicence(string? clientIdentifier = null, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Name == GuestAccountName);
		return user == null
			? throw new CarbonException(20, $"Free or guest account with Name {GuestAccountName} does not exist")
			: UserToFull(user);
	}

	public async Task<int> LogoutId(string userId)
	{
		return await Task.FromResult(-1);
	}

	public async Task<int> ReturnId(string userId)
	{
		return await Task.FromResult(-1);
	}

	public async Task<int> ChangePassword(string userId, string? oldPassword, string newPassword)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id) ?? throw new CarbonException(400, $"User Id '{userId}' does not exist");
		if (user.Psw != null && user.Psw != oldPassword)
		{
			throw new CarbonException(500, $"User Id '{userId}' incorrect old password");
		}
		user.Psw = newPassword;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<int> UpdateAccount(string userId, string userName, string? comment, string? email)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id) ?? throw new CarbonException(600, $"User Id '{userId}' does not exist");
		user.Name = userName;
		user.Comment = comment;
		user.Email = email;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	#endregion

	#region Navigation

	public async Task<Shared.Entities.NavData> GetNavigationData()
	{
		using var context = MakeContext();
		var custs = await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.AsAsyncEnumerable()
			.Select(c => new Shared.Entities.NavCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Inactive = c.Inactive,
				JobIds = c.Jobs.Select(j => j.Id.ToString()).ToArray(),
				UserIds = c.Users.Select(u => u.Id.ToString()).ToArray()
			}
			).ToArrayAsync().ConfigureAwait(false);
		var jobs = await context.Jobs.AsNoTracking()
			.Include(j => j.Users)
			.AsAsyncEnumerable()
			.Select(j => new Shared.Entities.NavJob()
			{
				Id = j.Id.ToString(),
				Name = j.Name,
				DisplayName = j.DisplayName,
				Inactive = j.Inactive,
				CustomerId = j.CustomerId?.ToString(),
				UserIds = j.Users.Select(u => u.Id.ToString()).ToArray(),
				VartreeNames = j.VartreeNames?.Split(',') ?? Array.Empty<string>()
			}
			).ToArrayAsync().ConfigureAwait(false);
		var users = await context.Users.AsNoTracking()
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.AsAsyncEnumerable()
			.Select(u => new Shared.Entities.NavUser()
			{
				Id = u.Id.ToString(),
				Name = u.Name,
				Email = u.Email,
				IsDisabled = u.IsDisabled,
				CustomerIds = u.Customers.Select(c => c.Id.ToString()).ToArray(),
				JobIds = u.Jobs.Select(j => j.Id.ToString()).ToArray()
			}
			).ToArrayAsync().ConfigureAwait(false);
		return new Shared.Entities.NavData()
		{
			Customers = custs,
			Jobs = jobs,
			Users = users
		};
	}

	public async Task<XElement> GetNavigationXml()
	{
		using var context = MakeContext();
		var elem = new XElement("Navigation");
		XElement[] custs = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).AsAsyncEnumerable().Select(c => new XElement(nameof(Customer),
			new XAttribute(nameof(Customer.Id), c.Id),
			new XElement(nameof(Customer.Name), c.Name), c.DisplayName == null ? null : new XElement(nameof(Customer.DisplayName), c.DisplayName),
			new XElement(nameof(Customer.Inactive), c.Inactive),
			new XElement("JobIds", c.Jobs.Select(j => new XElement(nameof(Job.Id), j.Id))),
			new XElement("UserIds", c.Users.Select(j => new XElement(nameof(User.Id), j.Id)))
			)).ToArrayAsync().ConfigureAwait(false);
		XElement[] jobs = await context.Jobs.AsNoTracking().Include(j => j.Users).AsAsyncEnumerable().Select(j => new XElement(nameof(Job),
			new XAttribute(nameof(Job.Id), j.Id),
			new XElement(nameof(Job.Name), j.Name), j.DisplayName == null ? null : new XElement(nameof(Customer.DisplayName), j.DisplayName),
			new XElement(nameof(Customer.Inactive), j.Inactive),
			j.CustomerId == null ? null : new XElement(nameof(Job.CustomerId), j.CustomerId),
			new XElement("UserIds", j.Users.Select(u => new XElement(nameof(User.Id), u.Id)))
		)).ToArrayAsync().ConfigureAwait(false);
		XElement[] users = await context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).AsAsyncEnumerable().Select(u => new XElement(nameof(User),
			new XAttribute(nameof(User.Id), u.Id),
			new XAttribute(nameof(User.Name), u.Name),
			new XAttribute(nameof(User.IsDisabled), u.IsDisabled),
			new XElement("CustomerIds", u.Customers.Select(c => new XElement(nameof(Customer.Id), c.Id))),
			new XElement("JobIds", u.Jobs.Select(j => new XElement(nameof(Job.Id), j.Id)))
		)).ToArrayAsync().ConfigureAwait(false);
		elem.Add(
			new XElement("Customers", custs),
			new XElement("Jobs", jobs),
			new XElement("Users", users)
		);
		return elem;
	}

	#endregion

	#region Customer

	public async Task<Shared.Entities.Customer?> ReadCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id.ToString() == id).ConfigureAwait(false);
		return ToCustomer(cust, true);
	}

	public async Task<Shared.Entities.Customer[]> ListCustomers()
	{
		using var context = MakeContext();
		return await context.Customers.AsNoTracking().AsAsyncEnumerable().Select(c => ToCustomer(c, false)!).ToArrayAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer> UpdateCustomer(Shared.Entities.Customer customer)
	{
		using var context = MakeContext();
		Customer row;
		if (customer.Id == null)
		{
			// Inserting a new customer.
			row = new Customer
			{
				Id = Random.Shared.Next(30_000_000, 40_000_000),
				Created = DateTime.UtcNow
			};
			context.Customers.Add(row);
		}
		else
		{
			// This is an update of an existing customer.
			row = await context.Customers.FirstOrDefaultAsync(c => c.Id.ToString() == customer.Id).ConfigureAwait(false) ?? throw new CarbonException(700, $"Customer Id {customer.Id} not found for update");
		}
		row.Name = customer.Name;
		row.DisplayName = customer.DisplayName;
		row.StorageKey = customer.StorageKey ?? "DefaultEndpointsProtocol=https;AccountName=xxxxxxxxx;AccountKey=ZZZZZZZZZZZZZZZZ;BlobEndpoint=https://xxxxxxxxx.blob.core.windows.net/;";
		row.Comment = customer.Comment;
		row.CloudCustomerNames = customer.CloudCustomerNames?.Length > 0 ? string.Join(" ", customer.CloudCustomerNames) : null;
		row.Corporation = customer.Corporation;
		row.Sunset = customer.Sunset;
		row.Sequence = customer.Sequence;
		row.Credits = customer.Credits;
		row.DataLocation = (int?)customer.DataLocation;
		row.Inactive = customer.Inactive;
		row.Info = customer.Info;
		row.Logo = customer.Logo;
		row.Psw = customer.Psw;
		row.SignInLogo = customer.SignInLogo;
		row.SignInNote = customer.SignInNote;
		row.Spent = customer.Spent;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, row.Id).ConfigureAwait(false);
	}

	public async Task<string[]> ValidateCustomer(string customerId)
	{
		using var context = MakeContext();
		var cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id.ToString() == customerId).ConfigureAwait(false);
		var list = new List<string>();
		if (cust == null)
		{
			list.Add($"Customer Id {customerId} is not in the licensing database.");
		}
		else
		{
			try
			{
				var client = new BlobServiceClient(cust.StorageKey);
				var acc = await client.GetAccountInfoAsync();
				Trace.WriteLine($"Probe customer '{cust.Name}' storage account: {acc.Value.AccountKind}");
			}
			catch (Exception ex)
			{
				// The exception message can be really verbose with lots of lines.
				string first = ex.Message.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).First();
				list.Add(first);
			}
		}
		return list.ToArray();
	}

	public async Task<int> DeleteCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Jobs).Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == int.Parse(id)).ConfigureAwait(false);
		if (cust == null) return 0;
		foreach (var job in cust.Jobs.ToArray())
		{
			job.Customer = null;
			cust.Jobs.Remove(job);
		}
		foreach (var user in cust.Users.ToArray())
		{
			cust.Users.Remove(user);
		}
		context.Customers.Remove(cust);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer?> ConnectCustomerChildJobs(string customerId, string[] jobIds)
	{
		int id = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == id);
		if (cust == null) return null;
		var addjobs = context.Jobs.Where(j => jobIds.Contains(j.Id.ToString())).ToArray();
		foreach (var addjob in addjobs)
		{
			cust.Jobs.Add(addjob);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer?> DisconnectCustomerChildJob(string customerId, string jobId)
	{
		int id = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == id);
		if (cust == null) return null;
		var job = cust.Jobs.FirstOrDefault(j => j.Id.ToString() == jobId);
		if (job != null)
		{
			job.CustomerId = null;
			cust.Jobs.Remove(job);
			await context.SaveChangesAsync();
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer?> ConnectCustomerChildUsers(string customerId, string[] userIds)
	{
		int id = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == id);
		if (cust == null) return null;
		var addusers = context.Users.Where(u => userIds.Contains(u.Id.ToString())).ToArray();
		foreach (var adduser in addusers)
		{
			cust.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer?> DisconnectCustomerChildUser(string customerId, string userId)
	{
		int id = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == id);
		if (cust == null) return null;
		var user = cust.Users.FirstOrDefault(j => j.Id.ToString() == userId);
		if (user != null)
		{
			cust.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	static async Task<Shared.Entities.Customer?> RereadCustomer(ExampleContext context, int customerId)
	{
		var cust = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == customerId).ConfigureAwait(false);
		return ToCustomer(cust, true);
	}

	#endregion

	#region Job

	public async Task<Shared.Entities.Job?> ReadJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).Include(j => j.Users).FirstOrDefaultAsync(j => j.Id == id).ConfigureAwait(false);
		return ToJob(job, true);
	}

	public async Task<Shared.Entities.Job[]> ListJobs()
	{
		using var context = MakeContext();
		return await context.Jobs.AsNoTracking().AsAsyncEnumerable().Select(j => ToJob(j, true)!).ToArrayAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job> UpdateJob(Shared.Entities.Job job)
	{
		using var context = MakeContext();
		Job row;
		if (job.Id == null)
		{
			row = new Job
			{
				Id = Random.Shared.Next(20_000_000, 30_000_000),
				Created = DateTime.UtcNow
			};
			context.Jobs.Add(row);
			// Ensure a container for the job in the parent customer storage account.
			if (job.CustomerId != null)
			{
				int custid = int.Parse(job.CustomerId);
				var cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == custid).ConfigureAwait(false);
				if (cust != null)
				{
					try
					{
						var client = new BlobServiceClient(cust.StorageKey);
						var cc = client.GetBlobContainerClient(job.Name);
						Azure.Response<BlobContainerInfo> resp = await cc.CreateIfNotExistsAsync().ConfigureAwait(false);
						Trace.WriteLine($"Create container '{job.Name}' in customer '{cust.Name}' - {resp.Value.ETag}");
					}
					catch (Exception ex)
					{
						Trace.WriteLine($"Failed to create container '{job.Name}' in customer '{cust.Name}' - {ex.Message}");
					}
				}
			}
		}
		else
		{
			row = await context.Jobs.FirstOrDefaultAsync(j => j.Id.ToString() == job.Id) ?? throw new CarbonException(701, $"Customer Id {job.Id} not found for update");
		}
		row.Name = job.Name;
		row.DataLocation = (int?)job.DataLocation;
		row.DisplayName = job.DisplayName;
		row.CustomerId = job.CustomerId?.Length > 0 ? int.Parse(job.CustomerId) : (int?)null;
		row.Description = job.Description;
		row.Sequence = job.Sequence;
		row.Cases = job.Cases;
		row.LastUpdate = job.LastUpdate;
		row.Info = job.Info;
		row.Logo = job.Logo;
		row.Url = job.Url;
		row.IsMobile = job.IsMobile;
		row.DashboardsFirst = job.DashboardsFirst;
		row.Inactive = job.Inactive;
		row.VartreeNames = job.VartreeNames?.Length > 0 ? string.Join(",", job.VartreeNames) : null;
		row.LastUpdate = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		Shared.Entities.Job job2 = await RereadJob(context, row.Id).ConfigureAwait(false);
		return job2;
	}

	public async Task<string[]> ValidateJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == id);
		var list = new List<string>();
		if (job == null)
		{
			list.Add($"Job Id {jobId} is not in the licensing database.");
		}
		else
		{
			if (job.Customer == null)
			{
				list.Add($"Job Id {jobId} '{job.Name}' does not have a parent customer.");
			}
			else
			{
				try
				{
					var client = new BlobServiceClient(job.Customer.StorageKey);
					var cc = client.GetBlobContainerClient(job.Name);
					var props = await cc.GetPropertiesAsync();
					Trace.WriteLine($"Probe job '{job.Name}' customer '{job.Customer.Name}' container: {props.Value.ETag}");
				}
				catch (Exception ex)
				{
					// The exception message can be really verbose with lots of lines.
					string first = ex.Message.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).First();
					list.Add(first);
				}
			}
		}
		return list.ToArray();
	}

	public async Task<int> DeleteJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.Include(j => j.Users).FirstOrDefaultAsync(j => j.Id == id).ConfigureAwait(false);
		if (job == null) return 0;
		foreach (var user in job.Users.ToArray())
		{
			job.Users.Remove(user);
		}
		context.Jobs.Remove(job);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job?> ConnectJobChildUsers(string jobId, string[] userIds)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.Include(j => j.Users).FirstOrDefaultAsync(j => j.Id.ToString() == jobId).ConfigureAwait(false);
		if (job == null) return null;
		var addusers = context.Users.Where(j => userIds.Contains(j.Id.ToString())).ToArray();
		foreach (var adduser in addusers)
		{
			job.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job?> DisconnectJobChildUser(string jobId, string userId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.Include(c => c.Users).FirstOrDefaultAsync(j => j.Id.ToString() == jobId).ConfigureAwait(false);
		if (job == null) return null;
		var user = job.Users.FirstOrDefault(u => u.Id.ToString() == userId);
		if (user != null)
		{
			job.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, id).ConfigureAwait(false);
	}

	static async Task<Shared.Entities.Job?> RereadJob(ExampleContext context, int jobId)
	{
		var job = await context.Jobs.AsNoTracking().Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == jobId).ConfigureAwait(false);
		return ToJob(job, true);
	}

	#endregion

	#region User

	public async Task<Shared.Entities.User?> ReadUser(string userId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false);
		return user == null ? null : ToUser(user, true);
	}

	public async Task<Shared.Entities.User[]> ListUsers()
	{
		using var context = MakeContext();
		return await context.Users.AsNoTracking().AsAsyncEnumerable().Select(u => ToUser(u, false)!).ToArrayAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.User> UpdateUser(Shared.Entities.User user)
	{
		using var context = MakeContext();
		User row;
		if (user.Id == null)
		{
			Guid uid = Guid.NewGuid();
			row = new User
			{
				Id = Random.Shared.Next(10_000_000, 20_000_000),
				Uid = uid,
				Psw = null,     // The plaintext password might be needed for legacy usage, but avoid like the plague.
				PassHash = user.PassHash ?? HP(user.Psw, uid),
				Created = DateTime.UtcNow
			};
			context.Users.Add(row);
		}
		else
		{
			row = await context.Users.FirstAsync(u => u.Id.ToString() == user.Id) ?? throw new CarbonException(700, $"User Id {user.Id} not found for update");
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
		var user = await context.Users.Include(u => u.Customers).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		var addcusts = context.Customers.Where(c => customerIds.Contains(c.Id.ToString())).ToArray();
		foreach (var addcust in addcusts)
		{
			user.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	public async Task<Shared.Entities.User?> DisconnectUserChildCustomer(string userId, string customerId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(c => c.Customers).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		var cust = user.Customers.FirstOrDefault(c => c.Id.ToString() == customerId);
		if (cust != null)
		{
			user.Customers.Remove(cust);
			await context.SaveChangesAsync();
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	public async Task<Shared.Entities.User?> ConnectUserChildJobs(string userId, string[] jobIds)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users.Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id.ToString() == userId).ConfigureAwait(false);
		if (user == null) return null;
		var addjobs = context.Jobs.Where(j => jobIds.Contains(j.Id.ToString())).ToArray();
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
		var cust = user.Jobs.FirstOrDefault(j => j.Id.ToString() == jobId);
		if (cust != null)
		{
			user.Jobs.Remove(cust);
			await context.SaveChangesAsync();
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, id);
	}

	static async Task<Shared.Entities.User?> RereadUser(ExampleContext context, int userId)
	{
		var cust = await context.Users.AsNoTracking().Include(c => c.Customers).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == userId).ConfigureAwait(false);
		return ToUser(cust, true);
	}

	#endregion

	#region Job Upload

	sealed record UploadData(int JobId, string JobName, string StorageConnect, DirectoryInfo SourceDirectory, IProgress<string> Progress)
	{
		public int UploadId { get; } = Random.Shared.Next();
		public DateTime StartTime { get; } = DateTime.UtcNow;
		public DateTime? EndTime { get; set; }
		public int UploadCount { get; set; }
		public long UploadBytes { get; set; }
		public bool IsRunning { get; set; }
		public Exception? Error { get; set; }
		public CancellationTokenSource CTS { get; } = new CancellationTokenSource();
	}

	readonly List<UploadData> uploadList = new();
	XDocument mimedoc;

	public async Task<int> StartJobUpload(string jobId, DirectoryInfo sourceDirectory, IProgress<string> progress)
	{
		if (mimedoc == null)
		{
			using var client = new HttpClient();
			string xml = await client.GetStringAsync(MimeUrl);
			mimedoc = XDocument.Parse(xml);
		}
		int jobid = int.Parse(jobId);
		if (uploadList.Any(u => u.JobId == jobid && u.IsRunning)) throw new CarbonException(666, $"Job Id {jobId} already has an upload running");
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == jobid) ?? throw new CarbonException(666, $"Job Id {jobId} does not exist for upload");
		if (job.CustomerId == null) throw new CarbonException(666, $"Job Id {jobId} does not have a parent customer");
		var data = new UploadData(job.Id, job.Name, job.Customer.StorageKey, sourceDirectory, progress);
		uploadList.Add(data);
		var thread = new Thread(new ParameterizedThreadStart(UploadProc!));
		thread.Start(data);
		return data.UploadId;
	}

	const int TasteLength = 256;
	char[] cbuff;
	byte[] readbuff;

	async void UploadProc(object o)
	{
		readbuff ??= new byte[TasteLength];
		cbuff ??= new char[TasteLength];
		var data = (UploadData)o;
		data.Progress.Report($"START|{MaxParallelUploads}|{data.JobId}|{data.JobName}|{data.SourceDirectory.FullName}");
		int blobSeq = 0;
		long totalBytes = 0L;
		data.IsRunning = true;
		var cc = new BlobContainerClient(data.StorageConnect, data.JobName);
		await cc.CreateIfNotExistsAsync();
		var source = data.SourceDirectory.EnumerateFiles("*", new EnumerationOptions() { RecurseSubdirectories = true });
		int sourcePfxLen = data.SourceDirectory.FullName.Length + 1;
		// Arbitrary maximum parallel upload limit is 4
		var po = new ParallelOptions() { CancellationToken = data.CTS.Token, MaxDegreeOfParallelism = Math.Min(MaxParallelUploads, Environment.ProcessorCount) };
		try
		{
			await Parallel.ForEachAsync(source, po, async (f, t) =>
			{
				string? fixname = UpFileFilter(f.FullName);
				if (fixname == null) return;
				string blobname = fixname[sourcePfxLen..].Replace(Path.DirectorySeparatorChar, '/');
				using var reader = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				Interlocked.Increment(ref blobSeq);
				data.Progress.Report($"UPLOAD|{blobSeq}|{blobname}|{f.Length}");
				#region ----- Calculate the mime-type -----
				// Lookup the extension in the public XML document, then fallback to a slightly
				// clumsy probe of the bytes at the start of the file to see if contains a well-known
				// BOM or if it contains any controls characters not in text files.
				XElement? extelem = mimedoc.Root!.Elements("type").SelectMany(e => e.Elements("ext")).FirstOrDefault(e => string.Compare((string)e, f.Extension, StringComparison.OrdinalIgnoreCase) == 0);
				string? mimetype = (string?)extelem?.Parent!.Attribute("name");
				if (mimetype == null)
				{
					// Fallback inspection of bytes
					int len = reader.Read(readbuff, 0, readbuff.Length);
					bool isBigend = len >= 2 && readbuff[0] == 0xfe && readbuff[1] == 0xff;
					bool isLitlend = len >= 2 && readbuff[0] == 0xff && readbuff[1] == 0xfe;
					bool isUtf8 = len >= 3 && readbuff[0] == 0xef && readbuff[1] == 0xbb && readbuff[2] == 0xbf;
					if (isBigend || isLitlend || isUtf8)
					{
						// One of the mostpopular BOMs is found for a text file.
						mimetype = MimeText;
					}
					else
					{
						// Last resort is a fuzzy check of some bytes
						try
						{
							int ccount = utf8dec.GetChars(readbuff.AsSpan(0, len), cbuff.AsSpan(), true);
							bool badchars = cbuff.Take(ccount).Any(c => NonTextChars.Contains(c));
							mimetype = badchars ? MimeBinary : MimeText;
						}
						catch (DecoderFallbackException)
						{
							mimetype = MimeBinary;
						}
						//bool isbinary = readbuff.Take(len).Any(b => NonTextBytes.Contains(b));
						//mimetype = isbinary ? MimeBinary : MimeText;
					}
					reader.Position = 0L;
				}
				#endregion --------------------------------
				var upopts = new BlobUploadOptions()
				{
					Conditions = null,
					HttpHeaders = new BlobHttpHeaders() { ContentType = mimetype }
				};
				var bc = cc.GetBlobClient(blobname);
				await bc.UploadAsync(reader, upopts, data.CTS.Token);
				Interlocked.Add(ref totalBytes, reader.Length);
			});
		}
		catch (Exception ex)
		{
			data.Error = ex;
			data.Progress.Report($"ERROR|{ex.Message}");
		}
		data.UploadCount = blobSeq;
		data.UploadBytes = totalBytes;
		data.EndTime = DateTime.UtcNow;
		data.IsRunning = false;
		var secs = data.EndTime.Value.Subtract(data.StartTime).TotalSeconds;
		data.Progress.Report($"END|{blobSeq}|{totalBytes}|{secs:F1}");
	}

	static string? UpFileFilter(string fullname)
	{
		if (Excludes.Any(x => fullname.Contains(x, StringComparison.OrdinalIgnoreCase))) return null;
		if (LowerDirs.Any(d => fullname.Contains(d, StringComparison.OrdinalIgnoreCase)))
		{
			string lowname = Path.GetFileName(fullname).ToLowerInvariant();
			string path = Path.GetDirectoryName(fullname)!;
			return Path.Combine(path, lowname);
		}
		return fullname;
	}

	public bool CancelUpload(int uploadId)
	{
		var data = uploadList.FirstOrDefault(x => x.UploadId == uploadId && x.IsRunning);
		if (data == null) return false;
		data.CTS.Cancel();
		return true;
	}

	#endregion

	#region Azure Comparison

	public async Task<XElement> RunComparison()
	{
		var elem = new XElement("Comparison");
		var context = MakeContext();
		// Outer loop over storage accounts as database customers
		await foreach (var cust in context.Customers.AsNoTracking().Include(c => c.Jobs).AsAsyncEnumerable())
		{
			var conlist = new List<BlobContainerItem>();
			var celem = new XElement("Customer", new XAttribute("Name", cust.Name));
			elem.Add(celem);
			try
			{
				// List the cloud containers
				var client = new BlobServiceClient(cust.StorageKey);
				string? token = null;
				do
				{
					var pages = client.GetBlobContainersAsync(traits: BlobContainerTraits.Metadata).AsPages(token);
					await foreach (Azure.Page<BlobContainerItem> page in pages)
					{
						foreach (BlobContainerItem con in page.Values)
						{
							conlist.Add(con);
						}
					}
				}
				while (token?.Length > 0);
				// Compare the licensing job rows and the containers
				BlobContainerItem[] matches = conlist.Where(c => cust.Jobs.Any(j => j.Name == c.Name)).ToArray();
				BlobContainerItem[] cononly = conlist.Where(c => !cust.Jobs.Any(j => j.Name == c.Name)).ToArray();
				Job[] jobonly = cust.Jobs.Where(j => !conlist.Any(c => c.Name == j.Name)).ToArray();
				var elems1 = matches.Select(m => new XElement("Job", new XAttribute("Name", m.Name), new XAttribute("State", (int)JobState.OK)));
				var elems2 = cononly.Select(c => new XElement("Job", new XAttribute("Name", c.Name), new XAttribute("State", (int)JobState.OrphanContainer)));
				var elems3 = jobonly.Select(j => new XElement("Job", new XAttribute("Id", j.Id), new XAttribute("Name", j.Name), new XAttribute("State", (int)JobState.OrphanJobRecord)));
				var jobelems = elems1.Concat(elems2).Concat(elems3).OrderBy(e => (string)e.Attribute("Name")!);
				celem.Add(jobelems);
			}
			catch (Exception ex)
			{
				// Storage account access probably failed due to a bad connection string
				string msg = ex.Message.Split("\r\n".ToCharArray()).First();
				celem.Add(new XElement("Error", new XAttribute("Type", ex.GetType().Name), msg));
			}
		}
		return elem;
	}

	public async IAsyncEnumerable<BlobData> ListJobBlobs(string customerName, string jobName)
	{
		var context = MakeContext();
		var cust = await context.Customers.AsNoTracking().Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Name == customerName);
		if (cust != null)
		{
			var client = new BlobServiceClient(cust.StorageKey);
			var cc = client.GetBlobContainerClient(jobName);
			if (await cc.ExistsAsync())
			{
				string? token = null;
				do
				{
					IAsyncEnumerable<Page<BlobItem>> pages = cc.GetBlobsAsync().AsPages(token);
					await foreach (Page<BlobItem> page in pages)
					{
						foreach (BlobItem blob in page.Values)
						{
							var data = new BlobData(blob.Name, blob.Properties.ContentLength!.Value, blob.Properties.CreatedOn!.Value.UtcDateTime, blob.Properties.LastModified?.UtcDateTime, blob.Properties.ContentType);
							yield return data;
						}
						token = page.ContinuationToken;
					}
				}
				while (token?.Length > 0);
			}
		}

	}

	#endregion

	#region Row To Entities

	/// <summary>
	/// A deep loaded User from the example database is converted into a Carbon full licence.
	/// The example rows only contain mimimal data, so a lot of the return properties are null and unused.
	/// </summary>
	static LicenceFull UserToFull(User user)
	{
		Customer[] custs = user.Customers.Concat(user.Jobs.Select(j => j.Customer)).Distinct(new CustomerComparer()).ToArray();
		Job[] jobs = user.Jobs.Concat(user.Customers.SelectMany(c => c.Jobs)).Distinct(new JobComparer()).ToArray();
		return new LicenceFull()
		{
			Id = user.Id.ToString(),
			Name = user.Name,
			CloudCustomerNames = user.CloudCustomerNames?.Split(','),
			CloudJobNames = user.JobNames?.Split(","),
			DashboardNames = user.DashboardNames?.Split(","),
			VartreeNames = user.VartreeNames?.Split(','),
			Created = user.Created,
			DataLocation = ((Shared.Entities.DataLocationType?)user.DataLocation)?.ToString(),
			EntityId = user.EntityId,
			Filter = user.Filter,
			LoginCount = user.LoginCount,
			LoginMax = user.LoginMax,
			LoginMacs = user.LoginMacs,
			MinVersion = user.MinVersion,
			Version = user.Version,
			Sequence = user.Sequence,
			Sunset = user.Sunset,
			Email = user.Email,
			Comment = user.Comment,
			LastLogin = DateTime.UtcNow,
			DaysRemaining = null,
			EntityLogo = null,
			EntityName = null,
			EntityType = null,
			Recovered = null,
			GuestJobs = null,
			Roles = user.Roles?.Split(','),
			Customers = custs.Select(c => new LicenceCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Comment = c.Comment,
				StorageKey = c.StorageKey,
				Jobs = jobs.Where(j => j.CustomerId == c.Id).Select(j => new LicenceJob()
				{
					Id = j.Id.ToString(),
					Name = j.Name,
					DisplayName = j.DisplayName,
					Description = j.Description,
					VartreeNames = j.VartreeNames?.Split(',')
				}).ToArray()
			}).ToArray()
		};
	}

	static Shared.Entities.Customer? ToCustomer(Customer? cust, bool includeChildren)
	{
		if (cust == null) return null;
		return new()
		{
			Id = cust.Id.ToString(),
			Name = cust.Name,
			DisplayName = cust.DisplayName,
			StorageKey = cust.StorageKey,
			Comment = cust.Comment,
			CloudCustomerNames = cust.CloudCustomerNames?.Split(',') ?? Array.Empty<string>(),
			Corporation = cust.Corporation,
			Sunset = cust.Sunset,
			Sequence = cust.Sequence,
			Created = cust.Created,
			Credits = cust.Credits,
			DataLocation = (Shared.Entities.DataLocationType?)cust.DataLocation,
			Inactive = cust.Inactive,
			Info = cust.Info,
			Logo = cust.Logo,
			Psw = cust.Psw,
			SignInLogo = cust.SignInLogo,
			SignInNote = cust.SignInNote,
			Spent = cust.Spent,
			Jobs = includeChildren ? cust.Jobs?.Select(j => ToJob(j, false)).ToArray() : null,
			Users = includeChildren ? cust.Users?.Select(u => ToUser(u, false)).ToArray() : null
		};
	}

	static Shared.Entities.Job? ToJob(Job? job, bool includeChildren)
	{
		if (job == null) return null;
		return new()
		{
			Id = job.Id.ToString(),
			Name = job.Name,
			DataLocation = (Shared.Entities.DataLocationType?)job.DataLocation,
			DisplayName = job.DisplayName,
			Description = job.Description,
			Cases = job.Cases,
			Logo = job.Logo,
			Info = job.Info,
			Inactive = job.Inactive,
			Created = job.Created,
			IsMobile = job.IsMobile,
			DashboardsFirst = job.DashboardsFirst,
			LastUpdate = job.LastUpdate,
			Sequence = job.Sequence,
			Url = job.Url,
			CustomerId = job.CustomerId?.ToString(),
			VartreeNames = job.VartreeNames?.Split(',') ?? Array.Empty<string>(),
			Users = includeChildren ? job.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Customer = includeChildren ? job.Customer == null ? null : ToCustomer(job.Customer, false) : null
		};
	}

	static Shared.Entities.User? ToUser(User? user, bool includeChildren)
	{
		if (user == null) return null;
		return new()
		{
			Id = user.Id.ToString(),
			Name = user.Name,
			ProviderId = user.ProviderId,
			Psw = user.Psw,
			PassHash = user.PassHash,
			Email = user.Email,
			EntityId = user.EntityId,
			CloudCustomerNames = user.CloudCustomerNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
			JobNames = user.JobNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
			VartreeNames = user.VartreeNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
			DashboardNames = user.DashboardNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
			DataLocation = (Shared.Entities.DataLocationType?)user.DataLocation,
			Sequence = user.Sequence,
			Uid = user.Uid,
			Comment = user.Comment,
			Roles = user.Roles?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
			Filter = user.Filter,
			LoginMacs = user.LoginMacs,
			LoginCount = user.LoginCount,
			LoginMax = user.LoginMax,
			LastLogin = user.LastLogin,
			Sunset = user.Sunset,
			Version = user.Version,
			MinVersion = user.MinVersion,
			IsDisabled = user.IsDisabled,
			Customers = includeChildren ? user.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null,
			Jobs = includeChildren ? user.Jobs?.Select(j => ToJob(j, false)).ToArray() : null
		};
	}

	#endregion

	static byte[]? HP(string p, Guid u)
	{
		if (p == null) return null;
		using var deriver = new Rfc2898DeriveBytes(p, u.ToByteArray(), 15000, HashAlgorithmName.SHA1);
		return deriver.GetBytes(16);
	}

	ExampleContext MakeContext() => new(_connect);

	static string Redact(string value) => new string('*', value.Length - 1) + value.Last();
}
