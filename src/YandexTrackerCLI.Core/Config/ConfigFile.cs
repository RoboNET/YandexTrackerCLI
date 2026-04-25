namespace YandexTrackerCLI.Core.Config;

using System.Text.Json.Serialization;

public sealed record ConfigFile(
    [property: JsonPropertyName("default_profile")] string DefaultProfile,
    [property: JsonPropertyName("profiles")]        Dictionary<string, Profile> Profiles);
