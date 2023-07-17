using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
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
public class ExampleLicensingProvider : LicensingProviderBase
{
	readonly string? _connect;
	const string GuestAccountName = "guest";

	public ExampleLicensingProvider(string adoConnectionString)
	{
		_connect = adoConnectionString ?? throw new ArgumentNullException(nameof(adoConnectionString));
	}

	public override string Name => "Example Licensing Provider";

	public override string Description
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

	public override string[][] ConfigValues
	{
		get
		{
			string safeconn = Regex.Replace(_connect!, @"(password)=([^;]+)", s => $"{s.Groups[1].Value}={Redact(s.Groups[2].Value)}", RegexOptions.IgnoreCase);
			return new string[][]
			{
			new string[] { "AdoConnectionString", safeconn }
			};
		}
	}

	public override async Task<LicenceFull> LoginId(string userId, string? password, bool skipCache = false)
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

	public override async Task<LicenceFull> LoginName(string userName, string? password, bool skipCache = false)
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

	public override async Task<LicenceFull> GetFreeLicence(string? clientIdentifier = null, bool skipCache = false)
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

	public override async Task<int> LogoutId(string userId)
	{
		// TODO Add an explanation of what LogoutId might really do.
		return await Task.FromResult<int>(-1);
	}

	public override async Task<int> ReturnId(string userId)
	{
		// TODO Add an explanation of what ReturnId might really do.
		return await Task.FromResult<int>(-1);
	}

	public override async Task<int> ChangePassword(string userId, string? oldPassword, string newPassword)
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

	public override async Task<int> UpdateAccount(string userId, string userName, string? comment, string? email)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id) ?? throw new CarbonException(600, $"User Id '{userId}' does not exist");
		user.Name = userName;
		user.Comment = comment;
		user.Email = email;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public override async Task<Shared.Entities.NavData> GetNavigationData()
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
			).ToArrayAsync();
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
			).ToArrayAsync();
		var users = await context.Users.AsNoTracking()
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.AsAsyncEnumerable()
			.Select(u => new Shared.Entities.NavUser()
			{
				Id = u.Id.ToString(),
				Name = u.Name,
				IsDisabled = u.IsDisabled,
				CustomerIds = u.Customers.Select(c => c.Id.ToString()).ToArray(),
				JobIds = u.Jobs.Select(j => j.Id.ToString()).ToArray()
			}
			).ToArrayAsync();
		return new Shared.Entities.NavData()
		{
			Customers = custs,
			Jobs = jobs,
			Users = users
		};
	}

	public override async Task<string> GetNavigationXml()
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
		return elem.ToString();
	}

	public override async Task<Shared.Entities.Customer?> ReadCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id.ToString() == id);
		return cust == null ? null : ToCustomer(cust);
	}

	public override async Task<Shared.Entities.Customer[]> ListCustomers()
	{
		using var context = MakeContext();
		return await context.Customers.Include(c => c.Jobs)
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c))
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public override async Task<Shared.Entities.Customer> UpdateCustomer(Shared.Entities.Customer customer)
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
			row = await context.Customers.FirstOrDefaultAsync(c => c.Id.ToString() == customer.Id) ?? throw new CarbonException(700, $"Customer Id {customer.Id} not found for update");
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
		return ToCustomer(row);
	}

	public override bool CanCreateCustomer => true;

	public override bool CanDeleteCustomer => true;

	public override async Task<int> DeleteCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers
			.Include(c => c.Jobs)
			.Include(c => c.Users)
			.FirstOrDefaultAsync(c => c.Id.ToString() == id);
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

	public override async Task<Shared.Entities.Job?> ReadJob(string id)
	{
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id.ToString() == id);
		return job == null ? null : ToJob(job);
	}

	public override async Task<Shared.Entities.Job[]> ListJobs()
	{
		using var context = MakeContext();
		return await context.Jobs
			.AsAsyncEnumerable()
			.Select(j => ToJob(j))
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public override async Task<Shared.Entities.Job> UpdateJob(Shared.Entities.Job job)
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
		return ToJob(row);
	}

	public override bool CanCreateJob => true;

	public override bool CanDeleteJob => true;

	public override async Task<int> DeleteJob(string id)
	{
		using var context = MakeContext();
		var job = await context.Jobs
			.Include(j => j.Users)
			.FirstOrDefaultAsync(j => j.Id.ToString(id) == id);
		if (job == null) return 0;
		foreach (var user in job.Users.ToArray())
		{
			job.Users.Remove(user);
		}
		context.Jobs.Remove(job);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

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
					VartreeNames = j.VartreeNames.Split(',')
				}).ToArray()
			}).ToArray()
		};
	}

	static Shared.Entities.Customer ToCustomer(Customer cust) => new()
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
		Jobs = cust.Jobs?.Select(j => ToJob(j)).ToArray()
	};

	static Shared.Entities.Job ToJob(Job job) => new()
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
		CustomerId = job.CustomerId.ToString(),
		VartreeNames = job.VartreeNames?.Split(',') ?? Array.Empty<string>()
	};

	ExampleContext MakeContext() => new(_connect);

	static string Redact(string value) => new string('*', value.Length - 1) + value.Last();
}
