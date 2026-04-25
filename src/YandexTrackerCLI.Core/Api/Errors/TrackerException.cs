namespace YandexTrackerCLI.Core.Api.Errors;

public sealed class TrackerException : Exception
{
    public ErrorCode Code { get; }
    public int? HttpStatus { get; }
    public string? TraceId { get; }

    public TrackerException(
        ErrorCode code,
        string message,
        int? httpStatus = null,
        string? traceId = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        HttpStatus = httpStatus;
        TraceId = traceId;
    }

    public TrackerError ToError() => new(Code.ToWireName(), Message, HttpStatus, TraceId);
}
