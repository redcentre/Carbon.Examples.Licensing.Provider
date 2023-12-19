using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Example.EFCore;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	public bool SupportsRealms => true;

	public async Task<Shared.Entities.Realm?> ReadRealm(string realmId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		return realm == null ? null : ToRealm(realm, true);
	}

	public async Task<Shared.Entities.Realm[]> ListRealms()
	{
		using var context = MakeContext();
		var realms = await context.Realms.AsNoTracking().ToArrayAsync().ConfigureAwait(false);
		return realms.Select(r => ToRealm(r, false)!).ToArray();
	}

	public async Task<Shared.Entities.Realm> UpdateRealm(Shared.Entities.Realm realm)
	{
		using var context = MakeContext();
		Realm row;
		if (realm.Id == null)
		{
			int[] oldids = await context.Realms.Select(r => r.Id).ToArrayAsync().ConfigureAwait(false);
			int randid;
			do
			{
				randid = Random.Shared.Next(70_000_000, 80_000_000);
			}
			while (oldids.Any(x => x == randid));
			row = new Realm
			{
				Id = randid,
				Created = DateTime.UtcNow
			};
			context.Realms.Add(row);
		}
		else
		{
			row = await context.Realms.FirstAsync(r => r.Id.ToString() == realm.Id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"Realm Id {realm.Id} not found for update");
		}
		row.Name = realm.Name;
		row.Policy = realm.Policy;
		row.Inactive = realm.Inactive;
		await context.SaveChangesAsync().ConfigureAwait(false);
		row = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Users).FirstAsync(r => r.Id == row.Id);
		return await RereadRealm(context, row.Id).ConfigureAwait(false);
	}

	public async Task<int> DeleteRealm(string realmId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms
			.Include(r => r.Users)
			.Include(r => r.Customers)
			.FirstOrDefaultAsync(r => r.Id == id)
			.ConfigureAwait(false);
		if (realm == null) return 0;
		foreach (var user in realm.Users.ToArray())
		{
			realm.Users.Remove(user);
		}
		foreach (var cust in realm.Customers.ToArray())
		{
			realm.Customers.Remove(cust);
		}
		context.Realms.Remove(realm);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<string[]> ValidateRealm(string realmId)
	{
		return await Task.FromResult(Array.Empty<string>());    // Not used
	}

	public async Task<Shared.Entities.Realm?> ConnectRealmChildUsers(string realmId, string[] userIds)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] uids = userIds.Select(u => int.Parse(u)).ToArray();
		var addusers = context.Users.Where(u => uids.Contains(u.Id)).ToArray();
		foreach (var adduser in addusers)
		{
			realm.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Realm?> DisconnectRealmChildUser(string realmId, string userId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id);
		if (realm == null) return null;
		int uid = int.Parse(userId);
		var cust = realm.Users.FirstOrDefault(u => u.Id == uid);
		if (cust != null)
		{
			realm.Users.Remove(cust);
			await context.SaveChangesAsync();
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	public async Task<Shared.Entities.Realm?> ConnectRealmChildCustomers(string realmId, string[] customerIds)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] cids = customerIds.Select(c => int.Parse(c)).ToArray();
		var addcusts = context.Customers.Where(c => cids.Contains(c.Id)).ToArray();
		foreach (var addcust in addcusts)
		{
			realm.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	public async Task<Shared.Entities.Realm?> DisconnectRealmChildCustomer(string realmId, string customerId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id.ToString() == realmId).ConfigureAwait(false);
		if (realm == null) return null;
		int cid = int.Parse(customerId);
		var cust = realm.Customers.FirstOrDefault(c => c.Id == cid);
		if (cust != null)
		{
			realm.Customers.Remove(cust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	static async Task<Shared.Entities.Realm?> RereadRealm(ExampleContext context, int realmId)
	{
		var realm = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == realmId).ConfigureAwait(false);
		return ToRealm(realm, true);
	}
}
