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
		var rids = realmIds.Select(x => int.Parse(x)).ToArray();
		using var context = MakeContext();
		return await context.Customers
			.AsNoTracking()
			.Where(c => c.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
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
			row = await context.Customers.FirstOrDefaultAsync(c => c.Id.ToString() == customer.Id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.CustomerNotFound, $"Customer Id {customer.Id} not found for update");
		}
		row.Name = customer.Name;
		row.DisplayName = customer.DisplayName;
		row.StorageKey = customer.StorageKey;
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
			context.Jobs.Remove(job);
		}
		foreach (var user in cust.Users.ToArray())
		{
			cust.Users.Remove(user);
		}
		context.Customers.Remove(cust);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public Task<Shared.Entities.Customer?> ConnectCustomerChildJobs(string customerId, string[] jobIds) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public Task<Shared.Entities.Customer?> DisconnectCustomerChildJob(string customerId, string jobId) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

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
		var cust = await context.Customers
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.FirstOrDefaultAsync(c => c.Id == cid);
		if (cust == null) return null;
		int[] custjids = cust.Jobs.Select(j => j.Id).ToArray();
		int[] uids = userIds.Select(x => int.Parse(x)).ToArray();
		var userquery = context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Where(u => uids.Contains(u.Id));
		foreach (var user in userquery)
		{
			if (!user.Customers.Any(c => c.Id == cust.Id))
			{
				Log($"ConnectCustomerChildUsers | User {user.Id} {user.Name} ADD Cust {cust.Id} {cust.Name}");
				user.Customers.Add(cust);
			}
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
		var cust = await context.Customers
			.Include(c => c.Users)
			.FirstOrDefaultAsync(c => c.Id == id);
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

	static async Task<Shared.Entities.Customer?> RereadCustomer(ExampleContext context, int customerId)
	{
		var cust = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == customerId).ConfigureAwait(false);
		return ToCustomer(cust, true);
	}
}
