namespace VoiceMite.Core;

public enum ServiceErrorCode
{
    InvalidConfiguration, MissingKey, Unauthorized, QuotaOrRateLimit, ModelNotFound, UnsupportedFormat,
    Network, Timeout, ServiceUnavailable, InvalidResponse, InputTooLong, EmptyAudio, EmptyTranscript, Cancelled, Unknown
}

/// <summary>Contains only a safe code. Never stores server response text, an API key, or private input.</summary>
public sealed class ServiceException(ServiceErrorCode code) : Exception(code.ToString())
{
    public ServiceErrorCode Code { get; } = code;
}
