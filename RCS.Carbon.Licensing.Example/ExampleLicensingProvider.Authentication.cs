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

	public async Task<LicenceFull> AuthenticateId(string userId, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (user.PassHash != null)
		{
			// If user's password hash is null, then it's the rare and possibly invalid
			// situation where a user does not have a password and can authenticate without one.
			// Normally the hash will be present and it must be compared to the hash of the incoming password.
			byte[] inhash = HP(password ?? "", user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash)) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect password");
		}
		user.LoginCount = user.LoginCount ?? 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<LicenceFull> AuthenticateName(string userName, string? password, bool skipCache = false)
	{
		string upname = userName.ToUpper();
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Name.ToUpper() == upname) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Name '{userName}' does not exist");
		if (user.PassHash != null)
		{
			byte[] inhash = HP(password ?? "", user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash)) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Name '{userName}' incorrect password");
		}
		user.LoginCount = user.LoginCount ?? 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<LicenceFull> GetFreeLicence(string? clientIdentifier = null, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Name == GuestAccountName) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"Free or guest account with Name {GuestAccountName} does not exist");
		user.LoginCount = user.LoginCount == null ? 1 : user.LoginCount + 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<int> LogoutId(string userId)
	{
		// Reserved for future use
		return await Task.FromResult(-1);
	}

	public async Task<int> ReturnId(string userId)
	{
		// Reserved for future use
		return await Task.FromResult(-1);
	}

	public async Task<int> ChangePassword(string userId, string? oldPassword, string newPassword)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (oldPassword != null)
		{
			// If an old password is specified then its hash must match the user's record hash.
			// Not specifying and old password causes the password to be replaced without verification.
			// The plaintext password is no longer persisted anywhere for modern safety reasons.
			byte[] inhash = HP(oldPassword, user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash)) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect old password");
		}
		user.PassHash = HP(newPassword, user.Uid);
		user.Psw = null;
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
		var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		user.Name = userName;
		user.Comment = comment;
		user.Email = email;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}
}
