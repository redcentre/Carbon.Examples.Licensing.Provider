using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	const string MimeUrl = "https://systemrcs.blob.core.windows.net/reference/mime-types.xml";
	const int MaxParallelUploads = 4;
	const string MimeBinary = "application/octet-stream";
	const string MimeText = "text/plain";
	readonly Decoder utf8dec = Encoding.UTF8.GetDecoder();
	readonly char[] NonTextChars = Enumerable.Range(0, 32).Except(new int[] { 9, 10, 13 }).Select(e => (char)e).ToArray();
	readonly static string[] LowerDirs = new string[]
	{
			@"\CaseData\", @"\Specs\"
	};

	sealed record UploadTData(int JobId, string JobName, string StorageConnect, UploadParameters Parameters, IProgress<string> Progress)
	{
		public int UploadId { get; } = Random.Shared.Next();
		public DateTime StartTime { get; } = DateTime.UtcNow;
		public DateTime? EndTime { get; set; }
		public int UploadCount { get; set; }
		public long UploadBytes { get; set; }
		public int SkipCount { get; set; }
		public long SkipBytes { get; set; }
		public bool IsRunning { get; set; }
		public Exception? Error { get; set; }
		public CancellationTokenSource CTS { get; } = new CancellationTokenSource();
	}

	readonly List<UploadTData> uploadList = new();
	XDocument mimedoc;

	public async Task<int> StartJobUpload(UploadParameters parameters, IProgress<string> progress)
	{
		if (mimedoc == null)
		{
			using var client = new HttpClient();
			string xml = await client.GetStringAsync(MimeUrl);
			mimedoc = XDocument.Parse(xml);
		}
		int jobid = int.Parse(parameters.JobId);
		if (uploadList.Any(u => u.JobId == jobid && u.IsRunning)) throw new ExampleLicensingException(LicensingErrorType.JobUploadRunning, $"Job Id {parameters.JobId} already has an upload running");
		using var context = MakeContext();
		var job = await context.Jobs.AsNoTracking().Include(j => j.Customer).FirstOrDefaultAsync(j => j.Id == jobid) ?? throw new ExampleLicensingException(LicensingErrorType.JobNotFound, $"Job Id {parameters.JobId} does not exist for upload");
		if (job.CustomerId == null) throw new ExampleLicensingException(LicensingErrorType.JobOrphaned, $"Job Id {parameters.JobId} does not have a parent customer");
		var data = new UploadTData(job.Id, job.Name, job.Customer.StorageKey, parameters, progress);
		uploadList.Add(data);
		var thread = new Thread(new ParameterizedThreadStart(UploadProc!));
		thread.Start(data);
		return data.UploadId;
	}

	const int TasteLength = 256;
	char[] cbuff;
	byte[] readbuff;
	sealed record BlobTup(string Name, long Bytes, DateTime Modified);

	async void UploadProc(object o)
	{
		readbuff ??= new byte[TasteLength];
		cbuff ??= new char[TasteLength];
		var data = (UploadTData)o;
		DirectoryInfo[] dirs = data.Parameters.Sources.OfType<DirectoryInfo>().ToArray();
		FileInfo[] files = data.Parameters.Sources.OfType<FileInfo>().ToArray();
		DirectoryInfo? sourceRoot = dirs.FirstOrDefault()?.Parent ?? files.FirstOrDefault()?.Directory;
		int sourceRootPfxLen = sourceRoot!.FullName.Length + 1;
		data.Progress.Report($"START|{MaxParallelUploads}|{data.JobId}|{data.JobName}|{sourceRoot!.FullName}");
		int uploadCount = 0;
		long uploadBytes = 0L;
		int skipCount = 0;
		long skipBytes = 0L;
		data.IsRunning = true;
		var cc = new BlobContainerClient(data.StorageConnect, data.JobName);
		await cc.CreateIfNotExistsAsync();
		List<BlobTup>? tuplist = null;
		if (data.Parameters.NewAndChangedOnly)
		{
			// Build a set of all blobs for new/change testing.
			DateTime now = DateTime.Now;
			tuplist = new List<BlobTup>();
			string? token = null;
			do
			{
				IAsyncEnumerable<Page<BlobItem>> pages = cc.GetBlobsAsync().AsPages(token);
				await foreach (Page<BlobItem> page in pages)
				{
					foreach (BlobItem blob in page.Values)
					{
						//Console.WriteLine($"{blob.Name} {blob.Properties.ContentLength}");
						tuplist.Add(new BlobTup(blob.Name, blob.Properties.ContentLength!.Value, blob.Properties.LastModified!.Value.UtcDateTime));
					}
					token = page.ContinuationToken;
				}
			}
			while (token?.Length > 0);
			double listsecs = DateTime.Now.Subtract(now).TotalSeconds;
			data.Progress.Report($"BLOBLIST|{tuplist.Count}|{listsecs:F2}");
		}
		// Build the source enumerables
		var diropts = new EnumerationOptions() { RecurseSubdirectories = true };
		var dirsources = files.Concat(dirs
			.Select(d => d.EnumerateFiles("*", diropts))
			.Aggregate((src, collect) => src.Concat(collect)));

		// Arbitrary maximum parallel upload limit is 4
		var po = new ParallelOptions() { CancellationToken = data.CTS.Token, MaxDegreeOfParallelism = Math.Min(MaxParallelUploads, Environment.ProcessorCount) };
		try
		{
			await Parallel.ForEachAsync(dirsources, po, async (f, t) =>
			{
				string fixname = NameAdjust(f.FullName);
				string blobname = fixname[sourceRootPfxLen..].Replace(Path.DirectorySeparatorChar, '/');
				if (tuplist != null)
				{
					// A new/change test is required for blob and file
					var tup = tuplist.FirstOrDefault(t => t.Name == blobname);
					if (tup != null)
					{
						if (f.LastWriteTimeUtc <= tup.Modified)
						{
							Interlocked.Increment(ref skipCount);
							Interlocked.Add(ref skipBytes, f.Length);
							data.Progress.Report($"SKIP|{blobname}|{f.Length}|{f.LastWriteTimeUtc:s}|{tup.Modified:s}");
							return;
						}
					}
				}
				using var reader = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				data.Progress.Report($"UPLOAD|{blobname}|{f.Length}");
				#region ----- Calculate the mime-type -----
				// Lookup the extension in the public XML document, then fallback to a slightly
				// clumsy probe of the bytes at the start of the file to see if contains a well-known
				// BOM or if it contains any controls characters not in text files.
				XElement? extelem = mimedoc.Root!.Elements("type").SelectMany(e => e.Elements("ext")).FirstOrDefault(e => string.Compare((string)e, f.Extension, StringComparison.OrdinalIgnoreCase) == 0);
				string? mimetype = (string?)extelem?.Parent!.Attribute("name");
				if (mimetype == null)
				{
					// Fallback inspection of bytes
					int len = reader.Read(readbuff, 0, readbuff.Length);
					bool isBigend = len >= 2 && readbuff[0] == 0xfe && readbuff[1] == 0xff;
					bool isLitlend = len >= 2 && readbuff[0] == 0xff && readbuff[1] == 0xfe;
					bool isUtf8 = len >= 3 && readbuff[0] == 0xef && readbuff[1] == 0xbb && readbuff[2] == 0xbf;
					if (isBigend || isLitlend || isUtf8)
					{
						// One of the most popular BOMs is found for a text file.
						mimetype = MimeText;
					}
					else
					{
						// Last resort is a fuzzy check of some bytes
						try
						{
							int ccount = utf8dec.GetChars(readbuff.AsSpan(0, len), cbuff.AsSpan(), true);
							bool badchars = cbuff.Take(ccount).Any(c => NonTextChars.Contains(c));
							mimetype = badchars ? MimeBinary : MimeText;
						}
						catch (DecoderFallbackException)
						{
							mimetype = MimeBinary;
						}
						//bool isbinary = readbuff.Take(len).Any(b => NonTextBytes.Contains(b));
						//mimetype = isbinary ? MimeBinary : MimeText;
					}
					reader.Position = 0L;
				}
				#endregion --------------------------------
				var upopts = new BlobUploadOptions()
				{
					Conditions = null,
					HttpHeaders = new BlobHttpHeaders() { ContentType = mimetype }
				};
				var bc = cc.GetBlobClient(blobname);
				await bc.UploadAsync(reader, upopts, data.CTS.Token);
				Interlocked.Increment(ref uploadCount);
				Interlocked.Add(ref uploadBytes, reader.Length);
			});
		}
		catch (Exception ex)
		{
			data.Error = ex;
			data.Progress.Report($"ERROR|{ex.Message}");
		}
		data.UploadCount = uploadCount;
		data.UploadBytes = uploadBytes;
		data.EndTime = DateTime.UtcNow;
		data.IsRunning = false;
		var secs = data.EndTime.Value.Subtract(data.StartTime).TotalSeconds;
		data.Progress.Report($"END|{uploadCount}|{uploadBytes}|{skipCount}|{skipBytes}|{secs:F1}");
	}

	static string NameAdjust(string fullname)
	{
		if (LowerDirs.Any(d => fullname.Contains(d, StringComparison.OrdinalIgnoreCase)))
		{
			string lowname = Path.GetFileName(fullname).ToLowerInvariant();
			string path = Path.GetDirectoryName(fullname)!;
			return Path.Combine(path, lowname);
		}
		return fullname;
	}

	public bool CancelUpload(int uploadId)
	{
		var data = uploadList.FirstOrDefault(x => x.UploadId == uploadId && x.IsRunning);
		if (data == null) return false;
		data.CTS.Cancel();
		return true;
	}
}
