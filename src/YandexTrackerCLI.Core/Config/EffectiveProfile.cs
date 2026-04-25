namespace YandexTrackerCLI.Core.Config;

public sealed record EffectiveProfile(
    string Name,
    OrgType OrgType,
    string OrgId,
    bool ReadOnly,
    AuthConfig Auth,
    string? DefaultFormat = null);
