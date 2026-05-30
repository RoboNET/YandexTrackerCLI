namespace YandexTrackerCLI.Core.Api.Errors;

public sealed class TrackerException : Exception
{
    public ErrorCode Code { get; }
    public int? HttpStatus { get; }
    public string? TraceId { get; }

    /// <summary>
    /// Optional actionable hint naming the exact CLI command the user/agent should run to
    /// recover (e.g. <c>yt auth relogin --profile foo</c>). Surfaced by the error renderer
    /// as the <c>relogin_command</c> field so non-interactive consumers can act on it.
    /// </summary>
    public string? ReloginCommand { get; }

    public TrackerException(
        ErrorCode code,
        string message,
        int? httpStatus = null,
        string? traceId = null,
        Exception? inner = null,
        string? reloginCommand = null)
        : base(message, inner)
    {
        Code = code;
        HttpStatus = httpStatus;
        TraceId = traceId;
        ReloginCommand = reloginCommand;
    }

    public TrackerError ToError() => new(Code.ToWireName(), Message, HttpStatus, TraceId, ReloginCommand);
}
