namespace Pch.Providers.RepairPosture;

public interface IRepairPostureSource
{
    Task<RepairPostureResult> SuggestAsync(
        RepairPosturePacket packet,
        RepairPostureOptions? options = null,
        CancellationToken cancellationToken = default);
}

public static class RepairPostureLiveProviders
{
    public static IReadOnlyList<RepairPostureLiveProviderDescriptor> Defaults { get; } =
    [
        new(
            RepairPostureProviderKind.OpenRouter,
            "openrouter",
            EnabledByDefault: false,
            RequiresApiKey: true,
            SupportsCreditGuard: true,
            "explicit enablement, key, credit, timeout, empty-content, malformed-schema, and no-fallback guarded"),
        new(
            RepairPostureProviderKind.OpenAi,
            "openai",
            EnabledByDefault: false,
            RequiresApiKey: true,
            SupportsCreditGuard: false,
            "explicit enablement, key, timeout, empty-content, malformed-schema, and no-fallback guarded"),
        new(
            RepairPostureProviderKind.GrokXAi,
            "grok-xai",
            EnabledByDefault: false,
            RequiresApiKey: true,
            SupportsCreditGuard: false,
            "explicit enablement, key, timeout, empty-content, malformed-schema, and no-fallback guarded")
    ];
}
