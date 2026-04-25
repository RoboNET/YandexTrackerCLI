namespace YandexTrackerCLI.Core.Auth;

using System.Net.Http.Headers;

/// <summary>
/// Supplies an <c>Authorization: Bearer &lt;token&gt;</c> header using a static Yandex Cloud IAM token.
/// </summary>
public sealed class IamStaticProvider : IAuthProvider
{
    private readonly string _token;

    /// <summary>
    /// Initializes a new instance of the <see cref="IamStaticProvider"/> class.
    /// </summary>
    /// <param name="token">The static IAM token obtained externally (for example, via <c>yc iam create-token</c>).</param>
    public IamStaticProvider(string token) => _token = token;

    /// <inheritdoc />
    public Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken ct) =>
        Task.FromResult(new AuthenticationHeaderValue("Bearer", _token));
}
