namespace YandexTrackerCLI.Core.Api.Errors;

public sealed record TrackerError(
    string Code,
    string Message,
    int? HttpStatus = null,
    string? TraceId = null);
