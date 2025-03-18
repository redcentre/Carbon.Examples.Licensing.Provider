using System;

namespace RCS.Carbon.Licensing.Example;

public enum LicensingErrorType
{
	None,
	IdentityBadFormat,
	IdentityNotFound,
	PasswordIncorrect,
	CustomerNotFound,
	JobNotFound,
	JobUploadRunning,
	JobDownloadRunning,
	JobOrphaned
}

public sealed class ExampleLicensingException : ApplicationException
{
	public ExampleLicensingException(LicensingErrorType errorType, string message)
		: base(message)
	{
		ErrorType = errorType;
	}

	public ExampleLicensingException(LicensingErrorType errorType, string message, Exception innerException)
		: base(message, innerException)
	{
		ErrorType = errorType;
	}

	public LicensingErrorType ErrorType { get; private set; }
}
