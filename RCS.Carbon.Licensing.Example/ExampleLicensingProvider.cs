using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
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
	readonly string? _connect;

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

	ExampleContext MakeContext() => new(_connect);

	/// <summary>
	/// A deep loaded User from the example database is converted into a Carbon full licence.
	/// The example rows only contain mimimal data, so a lot of the return properties are null and unused.
	/// </summary>
	LicenceFull UserToFull(User user)
	{
		Customer[] custs = user.Customers.Concat(user.Jobs.Select(j => j.Customer)).Distinct(new CustomerComparer()).ToArray();
		Job[] jobs = user.Jobs.Concat(user.Customers.SelectMany(c => c.Jobs)).Distinct(new JobComparer()).ToArray();
		string s = $"{user.Id}+{user.Name}+{DateTime.UtcNow:s}";
		return new LicenceFull()
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
					VartreeNames = j.VartreeNames?.Split(",; ".ToArray()),
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
			MaxJobs = user.MaxJobs,
			Version = user.Version,
			MinVersion = user.MinVersion,
			IsDisabled = user.IsDisabled,
			Customers = includeChildren ? user.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null,
			Jobs = includeChildren ? user.Jobs?.Select(j => ToJob(j, false)).ToArray() : null
		};
	}
}
