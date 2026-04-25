namespace YandexTrackerCLI.Core.Http;

using Auth;

/// <summary>
/// Delegating handler that attaches an <c>Authorization</c> header produced by an
/// <see cref="IAuthProvider"/> to every outgoing request.
/// </summary>
public sealed class AuthHandler : DelegatingHandler
{
    private readonly IAuthProvider _auth;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthHandler"/> class.
    /// </summary>
    /// <param name="auth">The authorization provider used to obtain credentials for each request.</param>
    public AuthHandler(IAuthProvider auth) => _auth = auth;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = await _auth.GetAuthorizationAsync(ct);
        return await base.SendAsync(request, ct);
    }
}
