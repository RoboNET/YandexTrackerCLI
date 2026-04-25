namespace YandexTrackerCLI.Core.Config;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<OrgType>))]
public enum OrgType
{
    [JsonStringEnumMemberName("yandex360")] Yandex360,
    [JsonStringEnumMemberName("cloud")]     Cloud,
}
