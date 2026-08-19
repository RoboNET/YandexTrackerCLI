namespace YandexTrackerCLI.Core.Json;

using System.Text.Json.Serialization;
using Api;
using Auth;
using Config;
using Storage;

[JsonSerializable(typeof(JwtHeader))]
[JsonSerializable(typeof(JwtPayload))]
[JsonSerializable(typeof(ConfigFile))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(AuthConfig))]
[JsonSerializable(typeof(TokenCacheEntry))]
[JsonSerializable(typeof(Dictionary<string, TokenCacheEntry>))]
[JsonSerializable(typeof(ResourceVersionEntry))]
[JsonSerializable(typeof(Dictionary<string, ResourceVersionEntry>))]
[JsonSerializable(typeof(IamExchangeRequest))]
[JsonSerializable(typeof(IamExchangeResponse))]
[JsonSerializable(typeof(IssueSearchRequest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class TrackerJsonContext : JsonSerializerContext;
