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
        _                      => "unexpected",
    };
}
