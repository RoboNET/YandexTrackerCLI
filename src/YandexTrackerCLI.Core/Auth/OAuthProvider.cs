namespace YandexTrackerCLI.Core.Auth;

using System.Net.Http.Headers;

/// <summary>
/// Supplies an <c>Authorization: OAuth &lt;token&gt;</c> header using a static Yandex OAuth token.
/// </summary>
public sealed class OAuthProvider : IAuthProvider
{
    private readonly string _token;

    /// <summary>
    /// Initializes a new instance of the <see cref="OAuthProvider"/> class.
    /// </summary>
    /// <param name="token">The static OAuth token issued by Yandex OAuth.</param>
    public OAuthProvider(string token) => _token = token;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken ct) =>
        Task.FromResult(new AuthenticationHeaderValue("OAuth", _token));
}
