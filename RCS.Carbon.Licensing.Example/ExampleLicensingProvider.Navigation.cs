using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	public async Task<Shared.Entities.NavData> GetNavigationData()
	{
		using var context = MakeContext();
		var custs = await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.AsAsyncEnumerable()
			.Select(c => new Shared.Entities.NavCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Inactive = c.Inactive,
				JobIds = c.Jobs.Select(j => j.Id.ToString()).ToArray(),
				UserIds = c.Users.Select(u => u.Id.ToString()).ToArray(),
				RealmIds = c.Realms.Select(r => r.Id.ToString()).ToArray()
			}
			).ToArrayAsync().ConfigureAwait(false);
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
				VartreeNames = j.VartreeNames?.Split(",; ".ToCharArray()) ?? Array.Empty<string>()
			}
			).ToArrayAsync().ConfigureAwait(false);
		var users = await context.Users.AsNoTracking()
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Include(u => u.Realms)
			.AsAsyncEnumerable()
			.Select(u => new Shared.Entities.NavUser()
			{
				Id = u.Id.ToString(),
				Name = u.Name,
				Email = u.Email,
				IsDisabled = u.IsDisabled,
				CustomerIds = u.Customers.Select(c => c.Id.ToString()).ToArray(),
				JobIds = u.Jobs.Select(j => j.Id.ToString()).ToArray(),
				RealmIds = u.Realms.Select(r => r.Id.ToString()).ToArray()
			}
			).ToArrayAsync().ConfigureAwait(false);
		var realms = await context.Realms.AsNoTracking()
			.Include(r => r.Users)
			.Include(r => r.Customers)
			.AsAsyncEnumerable()
			.Select(r => new Shared.Entities.NavRealm()
			{
				Id = r.Id.ToString(),
				Name = r.Name,
				IsInactive = r.Inactive,
				UserIds = r.Users.Select(u => u.Id.ToString()).ToArray(),
				CustomerIds = r.Customers.Select(c => c.Id.ToString()).ToArray()
			}).ToArrayAsync().ConfigureAwait(false);
		return new Shared.Entities.NavData()
		{
			Customers = custs,
			Jobs = jobs,
			Users = users,
			Realms = realms
		};
	}

	public async Task<ReportItem[]> GetDatabaseReport()
	{
		using var context = MakeContext();
		var list = new List<ReportItem>();
		var custs = context.Customers.AsNoTracking().Include(c => c.Jobs).Where(c => c.Jobs.Count == 0);
		foreach (var cust in custs)
		{
			list.Add(new ReportItem(0, cust.Id.ToString(), null, null, $"Customer '{cust.Name}' has no jobs"));
		}
		var users = context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).Where(u => u.Customers.Count == 0 && u.Jobs.Count == 0);
		foreach (var user in users)
		{
			list.Add(new ReportItem(0, null, null, user.Id.ToString(), $"User '{user.Name}' has no customers or jobs"));
		}
		var custs2 = context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).ThenInclude(j => j.Users);
		foreach (var cust2 in custs2)
		{
			int[] juserids = cust2.Jobs.SelectMany(j => j.Users.Select(u => u.Id)).ToArray();
			int[] cuserids = cust2.Users.Select(u => u.Id).ToArray();
			int[] uiddups = juserids.Intersect(cuserids).ToArray();
			foreach (var uiddup in uiddups)
			{
				var user = cust2.Users.Single(u => u.Id == uiddup);
				list.Add(new ReportItem(1, cust2.Id.ToString(), null, uiddup.ToString(), $"Customer '{cust2.Name}' User '{user.Name}' has multiple connections"));
			}
		}
		return await Task.FromResult(list.ToArray());
	}
}
