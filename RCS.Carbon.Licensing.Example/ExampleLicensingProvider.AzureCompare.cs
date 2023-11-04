using System;
using System.Collections.Generic;
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
	public async Task<XElement> RunComparison()
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
}
