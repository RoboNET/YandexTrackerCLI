namespace YandexTrackerCLI.Core.Http;

using Api.Errors;
using Config;

/// <summary>
/// Delegating handler that attaches the organization-selector header required by the
/// Yandex Tracker API. The header name depends on the organization type:
/// <c>X-Org-ID</c> for Yandex 360, <c>X-Cloud-Org-ID</c> for Yandex Cloud.
/// </summary>
public sealed class OrgHeaderHandler : DelegatingHandler
{
    private readonly OrgType _type;
    private readonly string _orgId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrgHeaderHandler"/> class.
    /// </summary>
    /// <param name="type">Organization platform type used to select the header name.</param>
    /// <param name="orgId">Organization identifier value to send. Must not be empty and must not contain
    /// CR, LF, or any other control characters (prevents HTTP header injection).</param>
    /// <exception cref="TrackerException">
    /// Thrown with <see cref="ErrorCode.ConfigError"/> when <paramref name="orgId"/> is empty
    /// or contains control characters such as CR or LF.
    /// </exception>
    public OrgHeaderHandler(OrgType type, string orgId)
    {
        if (string.IsNullOrEmpty(orgId))
        {
            throw new TrackerException(ErrorCode.ConfigError, "orgId must not be empty.");
        }

        foreach (var c in orgId)
        {
            if (c is '\r' or '\n' || char.IsControl(c))
            {
                throw new TrackerException(
                    ErrorCode.ConfigError,
                    "orgId contains invalid characters (control/CRLF).");
            }
        }

        _type = type;
        _orgId = orgId;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var header = _type switch
        {
            OrgType.Yandex360 => "X-Org-ID",
            OrgType.Cloud     => "X-Cloud-Org-ID",
            _ => throw new InvalidOperationException($"Unknown OrgType: {_type}"),
        };
        request.Headers.TryAddWithoutValidation(header, _orgId);
        return base.SendAsync(request, ct);
    }
}
