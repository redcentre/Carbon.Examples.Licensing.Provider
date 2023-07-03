using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

	public ExampleLicensingProvider(string adoConnectionString)
	{
		_connect = adoConnectionString ?? throw new ArgumentNullException(nameof(adoConnectionString));
	}

	protected override string GetDescriptionCore()
	{
		var t = GetType();
		var asm = t.Assembly;
		var an = asm.GetName();
		var desc = asm.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description;
		return $"{t.Name} {an.Version} - {desc}";
	}

	protected override IDictionary<string, string> GetConfigValuesCore()
	{
		string safeconn = Regex.Replace(_connect!, @"(password)=([^;]+)", s => $"{s.Groups[1].Value}={Redact(s.Groups[2].Value)}", RegexOptions.IgnoreCase);
		return new Dictionary<string, string>()
		{
			{ "AdoConnectionString", safeconn }
		};
	}

	protected override async Task<LicenceFull> GetFreeLicenceCore(string? clientIdentifier = null, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefault(u => u.Name == "guest");
		Debug.Assert(user != null);
		return await Task.FromResult(UserToFull(user));
	}

	protected override async Task<LicenceFull> GetLicenceNameCore(string userName, string? password, bool skipCache = false)
	{
		string upname = userName.ToUpper();
		using var context = MakeContext();
		var user = context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefault(u => u.Name.ToUpper() == upname);
		if (user == null)
		{
			throw new CarbonException(100, $"User Name '{userName}' does not exist");
		}
		if (user.Password != null & user.Password != password)
		{
			throw new CarbonException(101, $"User Name '{userName}' incorrect password");
		}
		return await Task.FromResult(UserToFull(user));
	}

	protected override async Task<LicenceFull> LoginIdCore(string userId, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefault(u => u.Id == id);
		if (user == null)
		{
			throw new CarbonException(200, $"User Id '{userId}' does not exist");
		}
		if (user.Password != null & user.Password != password)
		{
			throw new CarbonException(201, $"User Id '{userId}' incorrect password");
		}
		return await Task.FromResult(UserToFull(user));
	}

	protected override async Task<int> LogoutIdCore(string userId)
	{
		// TODO Add an explanation of what LogoutId might really do.
		return await Task.FromResult<int>(-1);
	}

	protected override async Task<int> ReturnIdCore(string userId)
	{
		// TODO Add an explanation of what ReturnId might really do.
		return await Task.FromResult<int>(-1);
	}

	protected override async Task<int> ChangePasswordCore(string userId, string? oldPassword, string newPassword)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id);
		if (user == null)
		{
			throw new CarbonException(400, $"User Id '{userId}' does not exist");
		}
		if (user.Password != null && user.Password != oldPassword)
		{
			throw new CarbonException(500, $"User Id '{userId}' incorrect old password");
		}
		user.Password = newPassword;
		int count = context.SaveChanges();
		return await Task.FromResult(count);
	}

	protected override async Task<int> UpdateAccountCore(string userId, string userName, string? comment, string? email)
	{
		using var context = MakeContext();
		long id = long.Parse(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id);
		if (user == null)
		{
			throw new CarbonException(600, $"User Id '{userId}' does not exist");
		}
		user.Name = userName;
		user.Note = comment;
		user.Email = email;
		int count = context.SaveChanges();
		return await Task.FromResult(count);
	}

	protected override Task<string> SaveStateCore()
	{
		throw new NotImplementedException();
	}

	protected override Task RestoreStateCore(string state)
	{
		throw new NotImplementedException();
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
			Email = user.Email,
			Comment = user.Note,
			LastLogin = DateTime.UtcNow,
			Roles = new string[] { "Manager", "Analyst" },
			Customers = custs.Select(c => new LicenceCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Comment = c.Note,
				StorageKey = c.StorageKey,
				Jobs = jobs.Where(j => j.CustomerId == c.Id).Select(j => new LicenceJob()
				{
					Id = j.Id.ToString(),
					Name = j.Name,
					DisplayName = j.DisplayName,
					Description = j.Note,
					VartreeNames = j.VartreeNames.Split(',')
				}).ToArray()
			}).ToArray()
		};
	}

	ExampleContext MakeContext() => new(_connect);

	static string Redact(string value) => new string('*', value.Length - 1) + value.Last();
}
