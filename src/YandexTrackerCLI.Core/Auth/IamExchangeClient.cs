namespace YandexTrackerCLI.Core.Auth;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Api.Errors;
using Json;

/// <summary>
/// Represents the result of a successful IAM token exchange.
/// </summary>
/// <param name="IamToken">The issued IAM token.</param>
/// <param name="ExpiresAt">The UTC instant at which the IAM token expires.</param>
public sealed record IamExchangeResult(string IamToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Exchanges a signed JWT for a Yandex Cloud IAM token.
/// </summary>
public interface IIamExchangeClient
{
    /// <summary>
    /// Exchanges a signed JWT for an IAM token.
    /// </summary>
    /// <param name="jwt">The signed JWT assertion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The IAM token and its expiration timestamp.</returns>
    Task<IamExchangeResult> ExchangeAsync(string jwt, CancellationToken ct);
}

/// <summary>
/// Default <see cref="IIamExchangeClient"/> implementation that posts the JWT assertion
/// to the Yandex Cloud IAM token endpoint.
/// </summary>
public sealed class IamExchangeClient : IIamExchangeClient
{
    /// <summary>
    /// The default Yandex Cloud IAM token endpoint.
    /// </summary>
    public static readonly Uri DefaultEndpoint = new("https://iam.api.cloud.yandex.net/iam/v1/tokens");

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    /// <summary>
    /// Initializes a new <see cref="IamExchangeClient"/>.
    /// </summary>
    /// <param name="http">The HTTP client used for outbound requests.</param>
    /// <param name="endpoint">Optional override for the IAM token endpoint; defaults to <see cref="DefaultEndpoint"/>.</param>
    public IamExchangeClient(HttpClient http, Uri? endpoint = null)
    {
        _http = http;
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    /// <inheritdoc />
    public async Task<IamExchangeResult> ExchangeAsync(string jwt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Content = JsonContent.Create(new IamExchangeRequest(jwt), TrackerJsonContext.Default.IamExchangeRequest);
        using var resp = await _http.SendAsync(req, ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new TrackerException(ErrorCode.AuthFailed,
                $"IAM token exchange failed with HTTP {(int)resp.StatusCode}.",
                httpStatus: (int)resp.StatusCode);
        }

        var body = await resp.Content.ReadFromJsonAsync(TrackerJsonContext.Default.IamExchangeResponse, ct)
                   ?? throw new TrackerException(ErrorCode.AuthFailed, "Empty IAM exchange response.");

        return new IamExchangeResult(body.IamToken, body.ExpiresAt);
    }
}

internal sealed record IamExchangeRequest(
    [property: JsonPropertyName("jwt")] string Jwt);

internal sealed record IamExchangeResponse(
    [property: JsonPropertyName("iamToken")]  string IamToken,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt);
