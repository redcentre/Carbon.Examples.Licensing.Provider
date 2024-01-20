using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	sealed record DownloadTData(int JobId, string JobName, string StorageConnect, DownloadParameters Parameters, IProgress<string> Progress)
	{
		public int DownloadId { get; } = Random.Shared.Next();
		public DateTime StartTime { get; } = DateTime.UtcNow;
		public DateTime? EndTime { get; set; }
		public int DownloadCount { get; set; }
		public long DownloadBytes { get; set; }
		public int SkipCount { get; set; }
		public long SkipBytes { get; set; }
		public bool IsRunning { get; set; }
		public Exception? Error { get; set; }
		public CancellationTokenSource CTS { get; } = new CancellationTokenSource();
	}

	const int MaxParallelDownloads = 4;
	readonly List<DownloadTData> downloadList = new();

	public async Task<int> StartJobDownload(DownloadParameters parameters, IProgress<string> progress)
	{
		int jobid = int.Parse(parameters.JobId);
		if (downloadList.Any(u => u.JobId == jobid && u.IsRunning)) throw new ExampleLicensingException(LicensingErrorType.JobDownloadRunning, $"Job Id {parameters.JobId} already has a download running");
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == jobid) ?? throw new ExampleLicensingException(LicensingErrorType.JobNotFound, $"Job Id {parameters.JobId} does not exist for download");
		if (job.CustomerId == null) throw new ExampleLicensingException(LicensingErrorType.JobOrphaned, $"Job Id {parameters.JobId} does not have a parent customer");
		var data = new DownloadTData(job.Id, job.Name, job.Customer.StorageKey, parameters, progress);
		downloadList.Add(data);
		var thread = new Thread(new ParameterizedThreadStart(DownloadProc!));
		thread.Start(data);
		return data.DownloadId;
	}

	async void DownloadProc(object o)
	{
		var data = (DownloadTData)o;

		data.Progress.Report($"START|{MaxParallelDownloads}|{data.JobId}|{data.JobName}|{data.Parameters.Destination.FullName}");
		int downloadCount = 0;
		long downloadBytes = 0L;
		int skipCount = 0;
		long skipBytes = 0L;
		data.IsRunning = true;
		var cc = new BlobContainerClient(data.StorageConnect, data.JobName);
		var po = new ParallelOptions() { CancellationToken = data.CTS.Token, MaxDegreeOfParallelism = Math.Min(MaxParallelDownloads, Environment.ProcessorCount) };
		try
		{
			var blobSource = WalkBlobs(cc);
			await Parallel.ForEachAsync(blobSource, po, async (bi, ct) =>
			{
				ct.ThrowIfCancellationRequested();
				var bc = cc.GetBlobClient(bi.Name);
				string filename = Path.Combine(data.Parameters.Destination.FullName, bi.Name.Replace('/', Path.DirectorySeparatorChar));
				if (data.Parameters.NewAndChangedOnly)
				{
					// Skip download if the target file exists and its modified
					// timestamp is greater than the blob's timestamp.
					var fi = new FileInfo(filename);
					if (fi.Exists)
					{
						DateTime? blobutc = bi.Properties.LastModified?.UtcDateTime;
						if (blobutc < fi.LastWriteTimeUtc)
						{
							Interlocked.Increment(ref skipCount);
							Interlocked.Add(ref skipBytes, fi.Length);
							data.Progress.Report($"SKIP|{bi.Name}|{bi.Properties.ContentLength}|{bi.Properties.LastModified?.UtcDateTime:s}|{fi.LastWriteTimeUtc:s}|{Environment.CurrentManagedThreadId}");
							return;
						}
					}
				}
				string? dir = Path.GetDirectoryName(filename);
				if (dir?.Length > 0)
				{
					if (!Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}
				}
				data.Progress.Report($"DOWNLOAD|{bi.Name}|{bi.Properties.ContentLength}|{Environment.CurrentManagedThreadId}");
				await bc.DownloadToAsync(filename, ct);
				Interlocked.Increment(ref downloadCount);
				Interlocked.Add(ref downloadBytes, bi.Properties.ContentLength ?? 0);
			});
		}
		catch (Exception ex)
		{
			data.Error = ex;
			data.Progress.Report($"ERROR|{ex.Message}");
		}

		data.DownloadCount = downloadCount;
		data.DownloadBytes = downloadBytes;
		data.EndTime = DateTime.UtcNow;
		data.IsRunning = false;
		var secs = data.EndTime.Value.Subtract(data.StartTime).TotalSeconds;
		data.Progress.Report($"END|{downloadCount}|{downloadBytes}|{skipCount}|{skipBytes}|{secs:F1}");
	}

	static async IAsyncEnumerable<BlobItem> WalkBlobs(BlobContainerClient cc)
	{
		string? token = null;
		do
		{
			IAsyncEnumerable<Page<BlobItem>> pages = cc.GetBlobsAsync().AsPages(token);
			await foreach (Page<BlobItem> page in pages)
			{
				foreach (BlobItem blob in page.Values)
				{
					yield return blob;
				}
				token = page.ContinuationToken;
			}
		}
		while (token?.Length > 0);
	}

	public bool CancelDownload(int downloadId)
	{
		var data = downloadList.FirstOrDefault(x => x.DownloadId == downloadId && x.IsRunning);
		if (data == null) return false;
		data.CTS.Cancel();
		return true;
	}
}
