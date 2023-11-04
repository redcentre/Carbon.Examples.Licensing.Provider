using System;
using System.Runtime.Serialization;

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

	private ExampleLicensingException(SerializationInfo info, StreamingContext context)
		: base(info, context)
	{
		ErrorType = (LicensingErrorType)info.GetValue(nameof(ErrorType), typeof(LicensingErrorType));
	}

	public override void GetObjectData(SerializationInfo info, StreamingContext context)
	{
		info.AddValue(nameof(ErrorType), ErrorType);
		base.GetObjectData(info, context);
	}

	public LicensingErrorType ErrorType { get; private set; }
}
