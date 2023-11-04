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
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).Include(j => j.Users).FirstOrDefaultAsync(j => j.Id == id).ConfigureAwait(false);
		return ToJob(job, true);
	}

	public async Task<Shared.Entities.Job[]> ListJobs()
	{
		using var context = MakeContext();
		return await context.Jobs.AsNoTracking().AsAsyncEnumerable().Select(j => ToJob(j, true)!).ToArrayAsync().ConfigureAwait(false);
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
		var job = await context.Jobs.Include(j => j.Users).FirstOrDefaultAsync(j => j.Id == id).ConfigureAwait(false);
		if (job == null) return 0;
		foreach (var user in job.Users.ToArray())
		{
			job.Users.Remove(user);
		}
		context.Jobs.Remove(job);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job?> ConnectJobChildUsers(string jobId, string[] userIds)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.Include(j => j.Users).FirstOrDefaultAsync(j => j.Id.ToString() == jobId).ConfigureAwait(false);
		if (job == null) return null;
		var addusers = context.Users.Where(j => userIds.Contains(j.Id.ToString())).ToArray();
		foreach (var adduser in addusers)
		{
			job.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, id).ConfigureAwait(false);
	}

	public async Task<Shared.Entities.Job?> DisconnectJobChildUser(string jobId, string userId)
	{
		int id = int.Parse(jobId);
		using var context = MakeContext();
		var job = await context.Jobs.Include(c => c.Users).FirstOrDefaultAsync(j => j.Id.ToString() == jobId).ConfigureAwait(false);
		if (job == null) return null;
		var user = job.Users.FirstOrDefault(u => u.Id.ToString() == userId);
		if (user != null)
		{
			job.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadJob(context, id).ConfigureAwait(false);
	}

	static async Task<Shared.Entities.Job?> RereadJob(ExampleContext context, int jobId)
	{
		var job = await context.Jobs.AsNoTracking().Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == jobId).ConfigureAwait(false);
		return ToJob(job, true);
	}
}
