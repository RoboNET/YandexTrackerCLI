namespace YandexTrackerCLI.Core.Auth;

using System.Net.Http.Headers;
using System.Security.Cryptography;

/// <summary>
/// Authenticates using a Yandex Cloud service account key: builds a signed JWT,
/// exchanges it for an IAM token via <see cref="IIamExchangeClient"/>, and caches
/// the IAM token in a <see cref="TokenCache"/> keyed by a caller-supplied identifier.
/// </summary>
/// <remarks>
/// The provided <see cref="RSA"/> instance is NOT disposed by this provider;
/// lifecycle ownership remains with the caller (typically the context factory).
/// </remarks>
public sealed class ServiceAccountProvider : IAuthProvider, IDisposable
{
    private readonly string _serviceAccountId;
    private readonly string _keyId;
    private readonly RSA _rsa;
    private readonly TokenCache _cache;
    private readonly IIamExchangeClient _exchange;
    private readonly string _cacheKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="ServiceAccountProvider"/>.
    /// </summary>
    /// <param name="serviceAccountId">The service account identifier (used as JWT <c>iss</c>).</param>
    /// <param name="keyId">The authorized key identifier (used as JWT <c>kid</c>).</param>
    /// <param name="privateKey">The RSA private key used to sign the JWT; lifetime is owned by the caller.</param>
    /// <param name="cache">Token cache used to persist the IAM token between invocations.</param>
    /// <param name="exchange">The IAM exchange client.</param>
    /// <param name="cacheKey">Stable cache key that uniquely identifies the credential.</param>
    public ServiceAccountProvider(
        string serviceAccountId,
        string keyId,
        RSA privateKey,
        TokenCache cache,
        IIamExchangeClient exchange,
        string cacheKey)
    {
        _serviceAccountId = serviceAccountId;
        _keyId = keyId;
        _rsa = privateKey;
        _cache = cache;
        _exchange = exchange;
        _cacheKey = cacheKey;
    }

    /// <inheritdoc />
    public async Task<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken ct)
    {
        var cached = await _cache.GetAsync(_cacheKey, ct: ct);
        if (cached is not null)
        {
            return new AuthenticationHeaderValue("Bearer", cached.Token);
        }

        await _gate.WaitAsync(ct);
        try
        {
            cached = await _cache.GetAsync(_cacheKey, ct: ct);
            if (cached is not null)
            {
                return new AuthenticationHeaderValue("Bearer", cached.Token);
            }

            var jwt = JwtBuilder.Build(
                rsa: _rsa,
                keyId: _keyId,
                issuer: _serviceAccountId,
                audience: IamExchangeClient.DefaultEndpoint.ToString(),
                issuedAt: DateTimeOffset.UtcNow,
                ttl: TimeSpan.FromHours(1));

            var result = await _exchange.ExchangeAsync(jwt, ct);
            await _cache.SetAsync(_cacheKey, result.IamToken, result.ExpiresAt, ct);
            return new AuthenticationHeaderValue("Bearer", result.IamToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases internal synchronization primitives. Does NOT dispose the RSA key.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }
}
