using System;
using System.Threading.Tasks;
using RCS.Carbon.Licensing.Shared;

#nullable enable

namespace RCS.Carbon.Licensing.Example;

partial class ExampleLicensingProvider
{
	public Task<int> StartJobDownload(DownloadParameters parameters, IProgress<string> progress)
	{
		throw new NotImplementedException("Example Licensing Provider StartJobDownload");
	}

	public bool CancelDownload(int downloadId)
	{
		return false;
	}
}
