namespace YandexTrackerCLI.Core.Auth;

using System.Net.Http.Headers;

/// <summary>
/// Provides an HTTP <c>Authorization</c> header value for outgoing Yandex Tracker API requests.
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Produces an <see cref="AuthenticationHeaderValue"/> to be attached to an outgoing request.
    /// </summary>
    /// <param name="ct">Cancellation token used to abort token retrieval.</param>
    /// <returns>The authorization header value (scheme and parameter).</returns>
    Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken ct);
}
