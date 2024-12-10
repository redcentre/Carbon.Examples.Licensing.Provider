using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Example.EFCore;
using RCS.Carbon.Licensing.Shared;


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

	public async Task<Shared.Entities.Job[]> ReadJobsByName(string jobName)
	{
		using var context = MakeContext();
		return await context.Jobs.AsNoTracking()
			.Include(j => j.Customer)
			.Include(j => j.Users)
			.Where(j => j.Name == jobName)
			.AsAsyncEnumerable()
			.Select(j => ToJob(j, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
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

	/// <summary>
	/// The example licensing provider which is based upon a SQL Server database does not currently
	/// support flagging which jobs (surveys) are available for TSAPI export. An empty set of string
	/// lines is currently returned.
	/// </summary>
	/// <remarks>
	/// This method can be revisited in the future when TSAPI exports are required, and a suitable
	/// job tagging convention can be invented.
	/// </remarks>
	public async Task<string[]> ListTSAPIVisibleJobs()
	{
		var empty = Array.Empty<string>();
		return await Task.FromResult(empty);
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
		row.CustomerId = job.CustomerId?.Length > 0 ? int.Parse(job.CustomerId) : null;
		row.Description = job.Description;
		row.Sequence = job.Sequence;
		row.DataLocation = (int?)job.DataLocation;
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
		var job = await context.Jobs.Include(c => c.Users).FirstOrDefaultAsync(j => j.Id.ToString() == jobId).ConfigureAwait(false);
		if (job == null) return null;
		int uid = int.Parse(userId);
		var user = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		var cust = await context.Customers.Include(c => c.Jobs).FirstAsync(c => c.Id == job.CustomerId).ConfigureAwait(false);
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
		var job = await context.Jobs.FirstOrDefaultAsync(j => j.Id == jid).ConfigureAwait(false);
		if (job == null) return null;
		var cust = await context.Customers.Include(c => c.Jobs).FirstAsync(c => c.Id == job.CustomerId).ConfigureAwait(false);
		Debug.Assert(cust != null);
		int[] allCustJids = cust.Jobs.Select(j => j.Id).ToArray();
		var userquery = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).Where(u => uids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
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

	public Task<Shared.Entities.Job?> ReplaceJobChildUsers(string jobId, string[] userIds)
	{
		throw new NotImplementedException("Replacing all Job child users is not implemented because it not currently a meaningful operation.");
	}

	/// <summary>
	/// Gets a list of the 'real' vartree (*.vtr) blobs in the root of a job's container.
	/// </summary>
	/// <remarks>
	/// Note that this method is somewhat unusual because it combines information about a job and
	/// its parent customer to read the blobs in an actual container. The returned names are useful
	/// in some high-level tools to manage licensing.
	/// </remarks>
	public async Task<string[]?> GetRealCloudVartreeNames(string jobId)
	{
		using var context = MakeContext();
		int jid = int.Parse(jobId);
		var job = await context.Jobs.Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == jid).ConfigureAwait(false);
		if (job?.Customer?.StorageKey == null) return null;
		var cc = new BlobContainerClient(job.Customer.StorageKey, job.Name);
		if (!await cc.ExistsAsync()) return null;
		IAsyncEnumerable<Page<BlobHierarchyItem>> pages = cc.GetBlobsByHierarchyAsync(delimiter: "/", prefix: null).AsPages(null);
		var list = new List<string>();
		try
		{
			await foreach (Page<BlobHierarchyItem> page in pages)
			{
				foreach (BlobHierarchyItem bhi in page.Values.Where(b => b.IsBlob))
				{
					string blobext = Path.GetExtension(bhi.Blob.Name);
					if (string.Compare(blobext, ".vtr", StringComparison.OrdinalIgnoreCase) == 0)
					{
						list.Add(Path.GetFileNameWithoutExtension(bhi.Blob.Name));
					}
				}
			}
		}
		catch (RequestFailedException ex)
		{
			Trace.WriteLine($"@@@@ ERROR Status {ex.Status} ErrorCode {ex.ErrorCode} - {ex.Message}");
		}
		return list.ToArray();

	}

	public async Task<XElement> CompareJobsAndContainers()
	{
		var elem = new XElement("Comparison");
		var context = MakeContext();
		// Outer loop over storage accounts as database customers
		await foreach (var cust in context.Customers.AsNoTracking().Include(c => c.Jobs).AsAsyncEnumerable())
		{
			var conlist = new List<BlobContainerItem>();
			var celem = new XElement("Customer", new XAttribute("Id", cust.Id), new XAttribute("Name", cust.Name));
			elem.Add(celem);
			try
			{
				// List the cloud containers
				var client = new BlobServiceClient(cust.StorageKey);
				string? token = null;
				do
				{
					var pages = client.GetBlobContainersAsync().AsPages(token);
					await foreach (Azure.Page<BlobContainerItem> page in pages)
					{
						foreach (BlobContainerItem con in page.Values)
						{
							conlist.Add(con);
						}
					}
				}
				while (token?.Length > 0);
				// Compare the licensing job rows and the containers
				BlobContainerItem[] matches = conlist.Where(c => cust.Jobs.Any(j => j.Name == c.Name)).ToArray();
				BlobContainerItem[] cononly = conlist.Where(c => !cust.Jobs.Any(j => j.Name == c.Name)).ToArray();
				Job[] jobonly = cust.Jobs.Where(j => !conlist.Any(c => c.Name == j.Name)).ToArray();
				var elems1 = matches.Select(m => new XElement("Job", new XAttribute("Name", m.Name), new XAttribute("State", (int)JobState.OK)));
				var elems2 = cononly.Select(c => new XElement("Job", new XAttribute("Name", c.Name), new XAttribute("State", (int)JobState.OrphanContainer)));
				var elems3 = jobonly.Select(j => new XElement("Job", new XAttribute("Id", j.Id), new XAttribute("Name", j.Name), new XAttribute("State", (int)JobState.OrphanJobRecord)));
				var jobelems = elems1.Concat(elems2).Concat(elems3).OrderBy(e => (string)e.Attribute("Name")!);
				celem.Add(jobelems);
			}
			catch (Exception ex)
			{
				// Storage account access probably failed due to a bad connection string
				string msg = ex.Message.Split("\r\n".ToCharArray()).First();
				celem.Add(new XElement("Error", new XAttribute("Type", ex.GetType().Name), msg));
			}
		}
		return elem;
	}

	public async IAsyncEnumerable<BlobData> ListJobBlobs(string customerName, string jobName)
	{
		var context = MakeContext();
		var cust = await context.Customers.AsNoTracking().Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Name == customerName);
		if (cust != null)
		{
			var client = new BlobServiceClient(cust.StorageKey);
			var cc = client.GetBlobContainerClient(jobName);
			if (await cc.ExistsAsync())
			{
				string? token = null;
				do
				{
					IAsyncEnumerable<Page<BlobItem>> pages = cc.GetBlobsAsync().AsPages(token);
					await foreach (Page<BlobItem> page in pages)
					{
						foreach (BlobItem blob in page.Values)
						{
							var data = new BlobData(blob.Name, blob.Properties.ContentLength!.Value, blob.Properties.CreatedOn!.Value.UtcDateTime, blob.Properties.LastModified?.UtcDateTime, blob.Properties.ContentType);
							yield return data;
						}
						token = page.ContinuationToken;
					}
				}
				while (token?.Length > 0);
			}
		}
	}

	static async Task<Shared.Entities.Job?> RereadJob(ExampleContext context, int jobId)
	{
		var job = await context.Jobs.AsNoTracking().Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == jobId).ConfigureAwait(false);
		return ToJob(job, true);
	}
}
