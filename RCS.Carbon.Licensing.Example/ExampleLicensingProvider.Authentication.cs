using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	const string GuestAccountName = "guest";

	static long GetId(string userId) => long.TryParse(userId, out long id) ? id : throw new ExampleLicensingException(LicensingErrorType.IdentityBadFormat, $"User Id '{userId}' is not in the correct format");

	public async Task<LicenceFull> LoginId(string userId, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (user.Psw != null & user.Psw != password)
		{
			throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect password");
		}
		return await UserToFull(user);
	}

	public async Task<LicenceFull> LoginName(string userName, string? password, bool skipCache = false)
	{
		string upname = userName.ToUpper();
		using var context = MakeContext();
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Name.ToUpper() == upname) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Name '{userName}' does not exist");
		if (user.Psw != null & user.Psw != password)
		{
			throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Name '{userName}' incorrect password");
		}
		return await UserToFull(user);
	}

	public async Task<LicenceFull> GetFreeLicence(string? clientIdentifier = null, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = await context.Users.AsNoTracking()
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Name == GuestAccountName);
		return user == null
			? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"Free or guest account with Name {GuestAccountName} does not exist")
			: await UserToFull(user);
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
		long id = GetId(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (user.Psw != null && user.Psw != oldPassword)
		{
			throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect old password");
		}
		user.Psw = newPassword;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<bool> ResetPassword(string email, DateTime utcTime, int signature)
	{
		// TODO Implement ResetPassword once the plaintext password is removed.
		await Task.CompletedTask;
		return true;
	}

	public async Task<int> UpdateAccount(string userId, string userName, string? comment, string? email)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = context.Users.FirstOrDefault(u => u.Id == id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		user.Name = userName;
		user.Comment = comment;
		user.Email = email;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}
}
