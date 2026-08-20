namespace YandexTrackerCLI.Core.Api.Errors;

public enum ErrorCode
{
    Unexpected = 0,
    InvalidArgs,
    ReadOnlyMode,
    AuthFailed,
    Forbidden,
    NotFound,
    RateLimited,
    ServerError,
    NetworkError,
    ConfigError,

    /// <summary>
    /// The request was blocked by a CLI-side profile policy (for example, a queue outside
    /// <c>allowed_queues</c>). Distinct from <see cref="Forbidden"/>, which reports a server-side denial.
    /// </summary>
    PolicyViolation,

    /// <summary>
    /// The command did not run to completion: it was interrupted (Ctrl-C / SIGINT / SIGTERM),
    /// its HTTP timeout elapsed, or the host cancelled the invocation. The result is incomplete
    /// by definition — never treat the output of a <see cref="Cancelled"/> run as a full answer.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The server rejected a mutation because the resource version supplied for optimistic
    /// locking no longer matches the stored one — someone else changed the resource after it
    /// was read. Distinct from <see cref="Unexpected"/> so scripts can retry the
    /// read-modify-write cycle instead of treating the failure as a bug.
    /// </summary>
    VersionConflict,
}

public static class ErrorCodeExtensions
{
    public static int ToExitCode(this ErrorCode code) => code switch
    {
        ErrorCode.InvalidArgs  => 2,
        ErrorCode.ReadOnlyMode => 3,
        ErrorCode.AuthFailed   => 4,
        ErrorCode.Forbidden    => 4,
        ErrorCode.NotFound     => 5,
        ErrorCode.RateLimited  => 6,
        ErrorCode.ServerError  => 7,
        ErrorCode.NetworkError => 8,
        ErrorCode.ConfigError  => 9,
        ErrorCode.PolicyViolation => 10,
        ErrorCode.Cancelled    => 11,
        ErrorCode.VersionConflict => 12,
        _                      => 1,
    };

    public static string ToWireName(this ErrorCode code) => code switch
    {
        ErrorCode.InvalidArgs  => "invalid_args",
        ErrorCode.ReadOnlyMode => "read_only_mode",
        ErrorCode.AuthFailed   => "auth_failed",
        ErrorCode.Forbidden    => "forbidden",
        ErrorCode.NotFound     => "not_found",
        ErrorCode.RateLimited  => "rate_limited",
        ErrorCode.ServerError  => "server_error",
        ErrorCode.NetworkError => "network_error",
        ErrorCode.ConfigError  => "config_error",
        ErrorCode.PolicyViolation => "policy_violation",
        ErrorCode.Cancelled    => "cancelled",
        ErrorCode.VersionConflict => "version_conflict",
        _                      => "unexpected",
    };
}
