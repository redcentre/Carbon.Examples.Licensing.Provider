using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using RCS.Carbon.Licensing.Example.EFCore;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

/// <summary>
/// An example of a simple Carbon licensing provider. This provider uses a SQL Server database as the backing
/// storage for licensing information about Users, Jobs and Customers.
/// </summary>
public partial class ExampleLicensingProvider : ILicensingProvider
{
	readonly string _prodkey;
	readonly string _connect;

	[Description("Example licensing provider using a SQL Server database")]
	public ExampleLicensingProvider(
		[Required]
		[Description("Product key")]
		string productKey,
		[Required]
		[Description("ADO database connection string")]
		string adoConnectionString
	)
	{
		_prodkey = productKey ?? throw new ArgumentNullException(nameof(productKey));
		_connect = adoConnectionString ?? throw new ArgumentNullException(nameof(adoConnectionString));
	}

	public string Name
	{
		get
		{
			var t = GetType();
			var asm = t.Assembly;
			var an = asm.GetName();
			return asm.GetCustomAttribute<AssemblyTitleAttribute>()!.Title;
		}
	}

	public string Description
	{
		get
		{
			var t = GetType();
			var asm = t.Assembly;
			var an = asm.GetName();
			return asm.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description;
		}
	}

	public string ConfigSummary
	{
		get
		{
			var m = Regex.Match(_connect, "(?:Server|Data Source)=([^;]+)", RegexOptions.IgnoreCase);
			string server = m.Success ? m.Groups[1].Value : "(SERVER?)";
			m = Regex.Match(_connect, "(?:database|initial catalog)=([^;]+)", RegexOptions.IgnoreCase);
			string database = m.Success ? m.Groups[1].Value : "(DATABASE?)";
			return $"{server};{database};{_prodkey}";
		}
	}

	void Log(string message) => Trace.WriteLine($"\u25a0 {DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}");

	static string Join(params object[] values) => values == null ? "NULL" : "[" + string.Join(",", values) + "]";

	ExampleContext MakeContext() => new(_connect);

	/// <summary>
	/// A deep loaded User from the example database is converted into a Carbon full licence.
	/// The example rows only contain mimimal data, so a lot of the return properties are null and unused.
	/// </summary>
	async Task<LicenceFull> UserToFull(User user)
	{
		Customer[] custs = user.Customers.Concat(user.Jobs.Select(j => j.Customer)).Distinct(new CustomerComparer()).ToArray();
		Job[] jobs = user.Jobs.Concat(user.Customers.SelectMany(c => c.Jobs)).Distinct(new JobComparer()).ToArray();
		string s = $"{user.Id}+{user.Name}+{DateTime.UtcNow:s}";
		var licfull = new LicenceFull()
		{
			Id = user.Id.ToString(),
			Name = user.Name,
			CloudCustomerNames = user.CloudCustomerNames?.Split(",; ".ToArray()),
			CloudJobNames = user.JobNames?.Split(",; ".ToArray()),
			DashboardNames = user.DashboardNames?.Split(",; ".ToArray()),
			VartreeNames = user.VartreeNames?.Split(",; ".ToArray()),
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
			ProductKey = _prodkey,      // This value can be set by the parent service, it's not part of the licensing database.
			Roles = user.Roles?.Split(",; ".ToArray()),
			Realms = user.Realms.Select(r => new LicenceRealm() { Id = r.Id.ToString(), Name = r.Name, Inactive = r.Inactive, Policy = r.Policy }).ToArray(),
			Customers = custs.Select(c => new LicenceCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Comment = c.Comment,
				StorageKey = c.StorageKey,
				Info = c.Info,
				Logo = c.Logo,
				SignInLogo = c.SignInLogo,
				SignInNote = c.SignInNote,
				Sequence = c.Sequence,
				Url = null,
				AgencyId = null,
				ParentAgency = null,
				Jobs = jobs.Where(j => j.CustomerId == c.Id).Select(j => new LicenceJob()
				{
					Id = j.Id.ToString(),
					Name = j.Name,
					DisplayName = j.DisplayName,
					Description = j.Description,
					Info = j.Info,
					Logo = j.Logo,
					Sequence = j.Sequence,
					Url = j.Url,
					VartreeNames = j.VartreeNames?.Split(",; ".ToArray())
					//RealCloudVartreeNames -> Filled by loop below
					//IsAccessible -> Filled by loop below
				}).ToArray()
			}).ToArray()
		};

		// The real vartree names are added to the licensing response. This is the only place
		// where licensing does processing outside its own data. The names of real vartree (*.vtr)
		// blobs can only be found by scanning the root blobs in each job's container, which is
		// done in parallel to minimise delays.

		var tasks = licfull.Customers
			.Where(c => c.StorageKey != null)
			.SelectMany(c => c.Jobs.Select(j => new { c, j }))
			.Select(x => ScanJobForVartreesAsync(x.c.StorageKey, x.j));
		await Task.WhenAll(tasks);

		return licfull;
	}

	static async Task ScanJobForVartreesAsync(string storageConnect, LicenceJob job)
	{
		var cc = new BlobContainerClient(storageConnect, job.Name);
		// A job's container does not contain many root blobs where the vartrees are stored.
		// A single call is expected to return all root blobs without the need for continue token looping.
		IAsyncEnumerable<Page<BlobHierarchyItem>> pages = cc.GetBlobsByHierarchyAsync(delimiter: "/", prefix: null).AsPages(null);
		var list = new List<string>();
		try
		{
			await foreach (Page<BlobHierarchyItem> page in pages)
			{
				foreach (BlobHierarchyItem bhi in page.Values.Where(b => b.IsBlob))
				{
					string blobext = Path.GetExtension(bhi.Blob.Name);
					if (string.Compare(blobext, ".vtr", StringComparison.OrdinalIgnoreCase) == 0)
					{
						list.Add(Path.GetFileNameWithoutExtension(bhi.Blob.Name));
					}
				}
			}
			job.IsAccessible = true;
		}
		catch (RequestFailedException ex)
		{
			Trace.WriteLine($"@@@@ ERROR Status {ex.Status} ErrorCode {ex.ErrorCode} - {ex.Message}");
			job.IsAccessible = false;
		}
		job.RealCloudVartreeNames = list.ToArray();
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
			Users = includeChildren ? cust.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Realms = includeChildren ? cust.Realms?.Select(r => ToRealm(r, false)).ToArray() : null
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
			Customer = includeChildren ? ToCustomer(job.Customer, false) : null
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
			MaxJobs = user.MaxJobs,
			Version = user.Version,
			MinVersion = user.MinVersion,
			IsDisabled = user.IsDisabled,
			Created = user.Created,
			Realms = includeChildren ? user.Realms?.Select(r => ToRealm(r, false)).ToArray() : null,
			Customers = includeChildren ? user.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null,
			Jobs = includeChildren ? user.Jobs?.Select(j => ToJob(j, false)).ToArray() : null
		};
	}

	static Shared.Entities.Realm? ToRealm(Realm? realm, bool includeChildren)
	{
		if (realm == null) return null;
		return new Shared.Entities.Realm()
		{
			Id = realm.Id.ToString(),
			Name = realm.Name,
			Inactive = realm.Inactive,
			Policy = realm.Policy,
			Created = realm.Created,
			Users = includeChildren ? realm.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Customers = includeChildren ? realm.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null
		};
	}
}
