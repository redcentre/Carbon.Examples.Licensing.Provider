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
}
