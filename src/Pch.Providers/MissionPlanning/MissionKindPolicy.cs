namespace Pch.Providers.MissionPlanning;

internal static class MissionKindPolicy
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    {
        "vacation",
        "business",
        "funeral_downtime",
        "helping_family",
        "family_support",
        "general"
    };

    public static bool IsAllowed(string? missionKind) =>
        !string.IsNullOrWhiteSpace(missionKind) && AllowedKinds.Contains(missionKind);
}
