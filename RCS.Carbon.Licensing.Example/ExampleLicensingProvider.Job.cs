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
	public async Task<Shared.Entities.Job?> ReadJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking()
			.Include(j => j.Customer)
			.Include(j => j.Users)
			.FirstOrDefaultAsync(j => j.Id == id)
			.ConfigureAwait(false);
		return ToJob(job, true);
	}

	public async Task<Shared.Entities.Job[]> ListJobs()
	{
		using var context = MakeContext();
		return await context.Jobs.AsNoTracking()
			.AsAsyncEnumerable()
			.Select(j => ToJob(j, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job> UpdateJob(Shared.Entities.Job job)
	{
		using var context = MakeContext();
		Job row;
		if (job.Id == null)
		{
			row = new Job
			{
				Id = Random.Shared.Next(20_000_000, 30_000_000),
				Created = DateTime.UtcNow
			};
			context.Jobs.Add(row);
			//// Ensure a container for the job in the parent customer storage account.
			//if (job.CustomerId != null)
			//{
			//	int custid = int.Parse(job.CustomerId);
			//	var cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == custid).ConfigureAwait(false);
			//	if (cust != null)
			//	{
			//		try
			//		{
			//			var client = new BlobServiceClient(cust.StorageKey);
			//			var cc = client.GetBlobContainerClient(job.Name);
			//			Azure.Response<BlobContainerInfo> resp = await cc.CreateIfNotExistsAsync().ConfigureAwait(false);
			//			Trace.WriteLine($"Create container '{job.Name}' in customer '{cust.Name}' - {resp?.Value.ETag}");
			//		}
			//		catch (Exception ex)
			//		{
			//			Trace.WriteLine($"Failed to create container '{job.Name}' in customer '{cust.Name}' - {ex.Message}");
			//		}
			//	}
			//}
		}
		else
		{
			row = await context.Jobs.FirstOrDefaultAsync(j => j.Id.ToString() == job.Id) ?? throw new ExampleLicensingException(LicensingErrorType.CustomerNotFound, $"Customer Id {job.Id} not found for update");
		}
		row.Name = job.Name;
		row.DataLocation = (int?)job.DataLocation;
		row.DisplayName = job.DisplayName;
		row.CustomerId = job.CustomerId?.Length > 0 ? int.Parse(job.CustomerId) : (int?)null;
		row.Description = job.Description;
		row.Sequence = job.Sequence;
		row.Cases = job.Cases;
		row.LastUpdate = job.LastUpdate;
		row.Info = job.Info;
		row.Logo = job.Logo;
		row.Url = job.Url;
		row.IsMobile = job.IsMobile;
		row.DashboardsFirst = job.DashboardsFirst;
		row.Inactive = job.Inactive;
		row.VartreeNames = job.VartreeNames?.Length > 0 ? string.Join(" ", job.VartreeNames) : null;
		row.LastUpdate = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		Shared.Entities.Job job2 = await RereadJob(context, row.Id).ConfigureAwait(false);
		return job2;
	}

	public async Task<string[]> ValidateJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == id);
		var list = new List<string>();
		if (job == null)
		{
			list.Add($"Job Id {jobId} is not in the licensing database.");
		}
		else
		{
			if (job.Customer == null)
			{
				list.Add($"Job Id {jobId} '{job.Name}' does not have a parent customer.");
			}
			else
			{
				try
				{
					var client = new BlobServiceClient(job.Customer.StorageKey);
					var cc = client.GetBlobContainerClient(job.Name);
					var props = await cc.GetPropertiesAsync();
					Trace.WriteLine($"Probe job '{job.Name}' customer '{job.Customer.Name}' container: {props.Value.ETag}");
				}
				catch (Exception ex)
				{
					// The exception message can be really verbose with lots of lines.
					string first = ex.Message.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).First();
					list.Add(first);
				}
			}
		}
		return list.ToArray();
	}

	public async Task<int> DeleteJob(string jobId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs
			.Include(j => j.Users)
			.FirstOrDefaultAsync(j => j.Id == id)
			.ConfigureAwait(false);
		if (job == null) return 0;
		foreach (var user in job.Users.ToArray())
		{
			job.Users.Remove(user);
		}
		context.Jobs.Remove(job);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Connects a Job to Users.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- If connecting a Job⮞User results in the User being connected to all Jobs of the parent Customer,
	/// then the new connect is redundant and all previous connections to the Customer's Jobs are removed and replaced with
	/// a single Customer⮞User connection.
	/// </remarks>
	public async Task<Shared.Entities.Job?> ConnectJobChildUsers(string jobId, string[] userIds)
	{
		Log($"ConnectJobChildUsers({jobId},{Join(userIds)})");
		int jid = int.Parse(jobId);
		int[] uids = userIds.Select(x => int.Parse(x)).ToArray();
		using var context = MakeContext();
		var job = await context.Jobs
			.FirstOrDefaultAsync(j => j.Id == jid);
		if (job == null) return null;
		var cust = context.Customers
			.Include(c => c.Jobs)
			.First(c => c.Id == job.CustomerId);
		Debug.Assert(cust != null);
		int[] allCustJids = cust.Jobs.Select(j => j.Id).ToArray();
		var userquery = context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Where(u => uids.Contains(u.Id));
		foreach (var user in userquery)
		{
			int[] newUserJobIds = user.Jobs.Select(j => j.Id).Concat(new int[] { jid }).Distinct().ToArray();
			// Do the new job ids contain all of the customer's child job ids? (TRICKY)
			if (newUserJobIds.Intersect(allCustJids).Count() == allCustJids.Length)
			{
				var remjobs = user.Jobs.Where(uj => allCustJids.Contains(uj.Id)).ToArray();
				foreach (var remjob in remjobs)
				{
					Log($"ConnectJobChildUsers | User {user.Id} {user.Name} DEL Cust {remjob.Id} {remjob.Name}");
					user.Jobs.Remove(remjob);
				}
				if (!user.Customers.Any(c => c.Id == cust.Id))
				{
					Log($"ConnectJobChildUsers | User {user.Id} {user.Name} ADD Cust {cust.Id} {cust.Name}");
					user.Customers.Add(cust);
				}
			}
			else
			{
				if (!user.Jobs.Any(j => j.Id == jid))
				{
					Log($"ConnectJobChildUsers | User {user.Id} {user.Name} ADD Job {job.Id} {job.Name}");
					user.Jobs.Add(job);
				}
				var delcust = user.Customers.FirstOrDefault(c => c.Id == cust.Id);
				if (delcust != null)
				{
					Log($"ConnectJobChildUsers | User {user.Id} {user.Name} DEL Cust {delcust.Id} {delcust.Name}");
					user.Customers.Remove(delcust);
				}
			}
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, jid).ConfigureAwait(false);
	}

	/// <summary>
	/// Disconnect a Job from a User.
	/// </summary>
	/// <remarks>
	/// CANONICAL -- See the comments in DisconnectUserChildJob.
	/// </remarks>
	public async Task<Shared.Entities.Job?> DisconnectJobChildUser(string jobId, string userId)
	{
		Log($"DisconnectJobChildUser({jobId},{userId})");
		int jid = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs
			.Include(c => c.Users)
			.FirstOrDefaultAsync(j => j.Id.ToString() == jobId);
		if (job == null) return null;
		int uid = int.Parse(userId);
		var user = context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.FirstOrDefault(u => u.Id == uid);
		if (user == null) return null;
		var cust = context.Customers
			.Include(c => c.Jobs)
			.First(c => c.Id == job.CustomerId);
		var usercust = user.Customers.FirstOrDefault(c => c.Id == cust.Id);
		if (usercust != null)
		{
			// The User is connected to the Job's parent Customer.
			Debug.Assert(!user.Jobs.Any());
			var addjobs = cust.Jobs.Where(j => j.Id != jid);
			foreach (var addjob in addjobs)
			{
				Log($"DisconnectJobChildUser | User {user.Id} {user.Name} ADD Job {addjob.Id} {addjob.Name}");
				user.Jobs.Add(addjob);
			}
			Log($"DisconnectJobChildUser | User {user.Id} {user.Name} DEL Cust {usercust.Id} {usercust.Name}");
			user.Customers.Remove(usercust);
		}
		else
		{
			// The User is not connected to the Job's parent Customer.
			// It can only have a set of Job connections (maybe empty).
			var deljob = user.Jobs.FirstOrDefault(j => j.Id == jid);
			if (deljob != null)
			{
				Log($"DisconnectJobChildUser | User {user.Id} {user.Name} ADD Job {deljob.Id} {deljob.Name}");
				user.Jobs.Remove(deljob);
			}
		}

		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, jid).ConfigureAwait(false);
	}

	static async Task<Shared.Entities.Job?> RereadJob(ExampleContext context, int jobId)
	{
		var job = await context.Jobs.AsNoTracking().Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == jobId).ConfigureAwait(false);
		return ToJob(job, true);
	}
}
