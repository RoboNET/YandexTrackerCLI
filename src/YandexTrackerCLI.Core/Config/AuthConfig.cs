namespace YandexTrackerCLI.Core.Config;

using System.Text;
using System.Text.Json.Serialization;

public sealed record AuthConfig(
    [property: JsonPropertyName("type")]                     AuthType Type,
    [property: JsonPropertyName("token")]                    string? Token = null,
    [property: JsonPropertyName("service_account_id")]       string? ServiceAccountId = null,
    [property: JsonPropertyName("key_id")]                   string? KeyId = null,
    [property: JsonPropertyName("private_key_path")]         string? PrivateKeyPath = null,
    [property: JsonPropertyName("private_key_pem")]          string? PrivateKeyPem = null,
    [property: JsonPropertyName("refresh_token")]            string? RefreshToken = null,
    [property: JsonPropertyName("federation_id")]            string? FederationId = null,
    [property: JsonPropertyName("dpop_key_path")]            string? DpopKeyPath = null,
    [property: JsonPropertyName("access_token_expires_at")]  string? AccessTokenExpiresAt = null)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Type = ").Append(Type);

        if (Token is not null)
            builder.Append(", Token = ***");

        if (ServiceAccountId is not null)
            builder.Append(", ServiceAccountId = ").Append(ServiceAccountId);

        if (KeyId is not null)
            builder.Append(", KeyId = ").Append(KeyId);

        if (PrivateKeyPath is not null)
            builder.Append(", PrivateKeyPath = ").Append(PrivateKeyPath);

        if (PrivateKeyPem is not null)
            builder.Append(", PrivateKeyPem = ***");

        if (RefreshToken is not null)
            builder.Append(", RefreshToken = ***");

        if (FederationId is not null)
            builder.Append(", FederationId = ").Append(FederationId);

        if (DpopKeyPath is not null)
            builder.Append(", DpopKeyPath = ").Append(DpopKeyPath);

        // AccessTokenExpiresAt is operational metadata, not a secret — print verbatim.
        if (AccessTokenExpiresAt is not null)
            builder.Append(", AccessTokenExpiresAt = ").Append(AccessTokenExpiresAt);

        return true;
    }
}
