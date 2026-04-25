namespace YandexTrackerCLI.Core.Http;

/// <summary>
/// Delegating handler that injects a <c>DPoP</c> (RFC 9449) header into each outgoing
/// request when an <see cref="IAuthProvider"/> (or other upstream caller) has published
/// a proof factory for the current asynchronous flow via <see cref="ProofFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The factory is published via an <see cref="AsyncLocal{T}"/> so that the HTTP pipeline
/// in <c>YandexTrackerCLI.Core</c> can stay decoupled from the federated/DPoP key
/// machinery that lives in the CLI layer. When no factory is set, the handler is a pure
/// pass-through and adds no header.
/// </para>
/// <para>
/// The factory signature is <c>(method, url) =&gt; DPoP proof JWT</c>; it is invoked
/// per request and must return a ready-to-emit compact JWT value for the
/// <c>DPoP:</c> header. The proof is bound to the request method and URL by the caller.
/// </para>
/// </remarks>
public sealed class DPoPHandler : DelegatingHandler
{
    /// <summary>
    /// Ambient proof factory used to produce the <c>DPoP</c> JWT for each outgoing request.
    /// Set by the auth provider before a token-protected call and cleared afterwards.
    /// When <c>null</c> the handler is a no-op.
    /// </summary>
    public static readonly AsyncLocal<Func<string, string, string>?> ProofFactory = new();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var factory = ProofFactory.Value;
        if (factory is not null && request.RequestUri is not null)
        {
            var proof = factory(request.Method.Method, request.RequestUri.ToString());
            request.Headers.TryAddWithoutValidation("DPoP", proof);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
