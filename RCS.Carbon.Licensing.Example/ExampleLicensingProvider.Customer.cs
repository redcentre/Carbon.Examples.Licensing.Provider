using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Example.EFCore;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	public async Task<Shared.Entities.Customer?> ReadCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.FirstOrDefaultAsync(c => c.Id.ToString() == id)
			.ConfigureAwait(false);
		return ToCustomer(cust, true);
	}

	public async Task<Shared.Entities.Customer[]> ReadCustomersByName(string customerName)
	{
		using var context = MakeContext();
		return await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.Where(c => c.Name == customerName)
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer[]> ListCustomers()
	{
		using var context = MakeContext();
		return await context.Customers.AsNoTracking()
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer[]> ListCustomers(params string[] realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? Array.Empty<int>();
		using var context = MakeContext();
		return await context.Customers
			.AsNoTracking()
			.Where(c => rids.Length == 0 || c.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Shared.Entities.CustomerPick[]> ListCustomerPicksForRealms(params string[] realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? Array.Empty<int>();
		using var context = MakeContext();
		return await context.Customers
			.AsNoTracking()
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.Where(c => rids.Length == 0 || c.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(c => new Shared.Entities.CustomerPick(c.Id.ToString(), c.Name)
			{
				IsInactive = c.Inactive,
				DisplayName = c.DisplayName,
				Jobs = c.Jobs.Select(j => new Shared.Entities.JobPick(j.Id.ToString(), j.Name)
				{
					DisplayName = j.DisplayName,
					IsInactive = j.Inactive
				}).ToArray()
			})
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Updates or inserts a customer. See the remarks for more detailed information.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This method should have been called UpsertCustomer because it can insert or update a customer.
	/// If the Id if the <paramref name="customer"/> parameter is <c>null</c> then a new customer will be created with a new unique Id and
	/// default values for some other properties. If a customer with the Id exists then it will be updating with the incoming values.
	/// An error is thrown if an attempt is made to update a customer that does not exist.
	/// </para>
	/// <para>
	/// If the Subscription, Tenant, App and Secret Ids were specified when the provider was constructed, then creating a new customer
	/// will also create an Azure Storage Account to be associated with the new customer. The connection string for the new account will
	/// be stored in the <see cref="Shared.Entities.Customer.StorageKey"/> property of the new customer and returned.
	/// </para>
	/// </remarks>
	/// <exception cref="ExampleLicensingException">Thrown if an attempt is made to update an existing customer Id that does not exist.</exception>
	public async Task<Shared.Entities.Customer> UpdateCustomer(Shared.Entities.Customer customer)
	{
		using var context = MakeContext();
		Customer row;
		if (customer.Id == null)
		{
			// Inserting a new customer. Generate a new unique Id.
			int? newid = null;
			while (newid == null)
			{
				int tryid = Random.Shared.Next(30_000_000, 40_000_000);
				if (!await context.Customers.AnyAsync(c => c.Id == tryid).ConfigureAwait(false))
				{
					newid = tryid;
				}
			}
			row = new Customer
			{
				Id = newid.Value,
				Created = DateTime.UtcNow
			};
			context.Customers.Add(row);
		}
		else
		{
			// This is an update of an existing customer Id (which must exist).
			row = await context.Customers.FirstOrDefaultAsync(c => c.Id.ToString() == customer.Id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.CustomerNotFound, $"Customer Id {customer.Id} not found for update");
		}
		row.Name = customer.Name;
		row.DisplayName = customer.DisplayName;
		row.Psw = customer.Psw;
		row.StorageKey = customer.StorageKey;
		row.CloudCustomerNames = customer.CloudCustomerNames?.Length > 0 ? string.Join(" ", customer.CloudCustomerNames) : null;
		row.DataLocation = (int?)customer.DataLocation;
		row.Sequence = customer.Sequence;
		row.Corporation = customer.Corporation;
		row.Comment = customer.Comment;
		row.Info = customer.Info;
		row.Logo = customer.Logo;
		row.SignInLogo = customer.SignInLogo;
		row.SignInNote = customer.SignInNote;
		row.Credits = customer.Credits;
		row.Spent = customer.Spent;
		row.Sunset = customer.Sunset;
		row.MaxJobs = customer.MaxJobs;
		row.Inactive = customer.Inactive;
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
		var cust = await context.Customers.Include(c => c.Jobs).Include(c => c.Users).Include(c => c.Realms).FirstOrDefaultAsync(c => c.Id == int.Parse(id)).ConfigureAwait(false);
		if (cust == null) return 0;
		foreach (var job in cust.Jobs.ToArray())
		{
			job.Customer = null;
			cust.Jobs.Remove(job);
			context.Jobs.Remove(job);
		}
		foreach (var user in cust.Users.ToArray())
		{
			cust.Users.Remove(user);
		}
		foreach (var realm in cust.Realms.ToArray())
		{
			cust.Realms.Remove(realm);
		}
		context.Customers.Remove(cust);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public Task<Shared.Entities.Customer?> ConnectCustomerChildJobs(string customerId, string[] jobIds) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public Task<Shared.Entities.Customer?> DisconnectCustomerChildJob(string customerId, string jobId) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public Task<Shared.Entities.Customer> ReplaceCustomerChildJobs(string customerId, string[] jobIds) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	/// <summary>
	/// Disconnect a Customer from a User.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- None required. There should be no connections to Customer child Jobs, and even if
	/// there are then it's harmless to let them remain an they can be dealt with separately.
	/// </remarks>
	public async Task<Shared.Entities.Customer?> DisconnectCustomerChildUser(string customerId, string userId)
	{
		Log($"DisconnectCustomerChildUser({customerId},{userId})");
		int id = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == id);
		if (cust == null) return null;
		var user = cust.Users.FirstOrDefault(j => j.Id.ToString() == userId);
		if (user != null)
		{
			Log($"DisconnectCustomerChildUser | Cust {cust.Id} {cust.Name} DEL User {user.Id} {user.Name}");
			cust.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	/// <summary>
	/// Connects a Customer to Users.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- If a User⮞Customer connect is created then any connections
	/// to child Jobs of that User are a conflict and must be removed.
	/// </remarks>
	public async Task<Shared.Entities.Customer?> ConnectCustomerChildUsers(string customerId, string[] userIds)
	{
		Log($"ConnectCustomerChildUsers({customerId},{Join(userIds)})");
		int cid = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == cid);
		if (cust == null) return null;
		int[] custjids = cust.Jobs.Select(j => j.Id).ToArray();
		int[] uids = userIds.Select(x => int.Parse(x)).ToArray();
		var userquery = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).Where(u => uids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var user in userquery)
		{
			// Add the User ⮞ Customer if not already present.
			if (!user.Customers.Any(c => c.Id == cust.Id))
			{
				Log($"ConnectCustomerChildUsers | User {user.Id} {user.Name} ADD Cust {cust.Id} {cust.Name}");
				user.Customers.Add(cust);
			}
			// No User ⮞ Job are allowed if the job is a child of the customer.
			var deljobs = user.Jobs.Where(j => custjids.Contains(j.Id)).ToArray();
			foreach (var deljob in deljobs)
			{
				Log($"ConnectCustomerChildUsers | User {user.Id} {user.Name} DEL Job {deljob.Id} {deljob.Name}");
				user.Jobs.Remove(deljob);
			}
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, cid).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Customer?> ReplaceCustomerChildUsers(string customerId, string[] userIds)
	{
		Log($"ReplaceCustomerChildUsers({customerId},{Join(userIds)})");
		int cid = int.Parse(customerId);
		using var context = MakeContext();
		var cust = await context.Customers.Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == cid);
		if (cust == null) return null;
		int[] custjids = cust.Jobs.Select(j => j.Id).ToArray();
		int[] uids = userIds.Select(x => int.Parse(x)).ToArray();
		var userquery = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).Where(u => uids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var user in userquery)
		{
			// Set a single User ⮞ Customer.
			user.Customers.Clear();
			user.Customers.Add(cust);
			// No User ⮞ Job are allowed if the job is a child of the customer.
			var deljobs = user.Jobs.Where(j => custjids.Contains(j.Id)).ToArray();
			foreach (var deljob in deljobs)
			{
				Log($"ReplaceCustomerChildUsers | User {user.Id} {user.Name} DEL Job {deljob.Id} {deljob.Name}");
				user.Jobs.Remove(deljob);
			}
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, cid).ConfigureAwait(false);
	}

	static async Task<Shared.Entities.Customer?> RereadCustomer(ExampleContext context, int customerId)
	{
		var cust = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == customerId).ConfigureAwait(false);
		return ToCustomer(cust, true);
	}
}
