namespace YandexTrackerCLI.Core.Config;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<AuthType>))]
public enum AuthType
{
    [JsonStringEnumMemberName("oauth")]           OAuth,
    [JsonStringEnumMemberName("iam-static")]      IamStatic,
    [JsonStringEnumMemberName("service-account")] ServiceAccount,
    [JsonStringEnumMemberName("federated")]       Federated,
}
