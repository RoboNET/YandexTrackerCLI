namespace YandexTrackerCLI.Core.Tests.Api.Errors;

using TUnit.Core;
using YandexTrackerCLI.Core.Api.Errors;

public sealed class ErrorCodeTests
{
    [Test]
    [Arguments(ErrorCode.InvalidArgs, 2)]
    [Arguments(ErrorCode.ReadOnlyMode, 3)]
    [Arguments(ErrorCode.AuthFailed, 4)]
    [Arguments(ErrorCode.Forbidden, 4)]
    [Arguments(ErrorCode.NotFound, 5)]
    [Arguments(ErrorCode.RateLimited, 6)]
    [Arguments(ErrorCode.ServerError, 7)]
    [Arguments(ErrorCode.NetworkError, 8)]
    [Arguments(ErrorCode.ConfigError, 9)]
    [Arguments(ErrorCode.Unexpected, 1)]
    public async Task ToExitCode_ReturnsDeterministicMapping(ErrorCode code, int expected)
    {
        await Assert.That(code.ToExitCode()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(ErrorCode.InvalidArgs,  "invalid_args")]
    [Arguments(ErrorCode.ReadOnlyMode, "read_only_mode")]
    [Arguments(ErrorCode.AuthFailed,   "auth_failed")]
    [Arguments(ErrorCode.Forbidden,    "forbidden")]
    [Arguments(ErrorCode.NotFound,     "not_found")]
    [Arguments(ErrorCode.RateLimited,  "rate_limited")]
    [Arguments(ErrorCode.ServerError,  "server_error")]
    [Arguments(ErrorCode.NetworkError, "network_error")]
    [Arguments(ErrorCode.ConfigError,  "config_error")]
    [Arguments(ErrorCode.Unexpected,   "unexpected")]
    public async Task ToWireName_StableStringMapping(ErrorCode code, string expected)
    {
        await Assert.That(code.ToWireName()).IsEqualTo(expected);
    }

    [Test]
    public async Task TrackerException_ToError_CarriesCodeNameAndMetadata()
    {
        var ex = new TrackerException(ErrorCode.NotFound, "Issue DEV-1 not found", httpStatus: 404, traceId: "abc-123");
        var err = ex.ToError();

        await Assert.That(err.Code).IsEqualTo("not_found");
        await Assert.That(err.Message).IsEqualTo("Issue DEV-1 not found");
        await Assert.That(err.HttpStatus).IsEqualTo(404);
        await Assert.That(err.TraceId).IsEqualTo("abc-123");
    }
}
