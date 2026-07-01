using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pch.Providers.ModelRoles;

public sealed record LiveModelRoleRegistryEntry(
    LiveModelRole Role,
    string ModelId,
    string Provider,
    bool IsConfigured);

public sealed record LiveModelRoleRegistry(
    IReadOnlyList<LiveModelRoleRegistryEntry> Entries)
{
    public LiveModelRoleRegistryEntry? Resolve(LiveModelRole role) =>
        Entries.FirstOrDefault(entry => entry.Role == role);

    public static LiveModelRoleRegistry FromOptions(LiveModelRunnerOptions options) =>
        new(
            [
                new LiveModelRoleRegistryEntry(
                    LiveModelRole.InHarnessActionGenerator,
                    options.InHarnessModelId,
                    options.Provider,
                    !string.IsNullOrWhiteSpace(options.InHarnessModelId)),
                new LiveModelRoleRegistryEntry(
                    LiveModelRole.StrongPlanner,
                    options.StrongPlannerModelId,
                    options.Provider,
                    !string.IsNullOrWhiteSpace(options.StrongPlannerModelId))
            ]);
}

public sealed record LiveModelRunnerOptions(
    bool LiveModeEnabled = false,
    bool ApiKeyAvailable = false,
    bool CreditGuardEnabled = true,
    LiveModelFallbackPolicy FallbackPolicy = LiveModelFallbackPolicy.Disabled,
    TimeSpan? Timeout = null,
    string Provider = "openrouter",
    string InHarnessModelId = "qwen/qwen3-14b",
    string StrongPlannerModelId = "qwen/qwen3-14b",
    double Temperature = 0,
    int MaxTokens = 1_200)
{
    public static LiveModelRunnerOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new LiveModelRunnerOptions(
            LiveModeEnabled: BoolValue(environment, "PCH_LIVE_MODEL_ENABLED"),
            ApiKeyAvailable: BoolValue(environment, "PCH_LIVE_MODEL_KEY_AVAILABLE") ||
                HasValue(environment, "OPENROUTER_API_KEY") ||
                HasValue(environment, "OPENROUTER_API_KEY_FILE") ||
                HasValue(environment, "OPENAI_API_KEY") ||
                HasValue(environment, "OPENAI_API_KEY_FILE") ||
                HasValue(environment, "XAI_API_KEY") ||
                HasValue(environment, "XAI_API_KEY_FILE") ||
                HasValue(environment, "GROK_API_KEY") ||
                HasValue(environment, "GROK_API_KEY_FILE"),
            CreditGuardEnabled: !BoolValue(environment, "PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"),
            FallbackPolicy: StringValue(environment, "PCH_LIVE_MODEL_FALLBACK_POLICY") == "allow_same_provider"
                ? LiveModelFallbackPolicy.AllowSameProvider
                : LiveModelFallbackPolicy.Disabled,
            Timeout: TimeoutValue(environment, "PCH_LIVE_MODEL_TIMEOUT_SECONDS"),
            Provider: StringValue(environment, "PCH_LIVE_MODEL_PROVIDER") ?? "openrouter",
            InHarnessModelId: StringValue(environment, "PCH_LIVE_IN_HARNESS_MODEL") ?? "qwen/qwen3-14b",
            StrongPlannerModelId: StringValue(environment, "PCH_LIVE_STRONG_PLANNER_MODEL") ?? "qwen/qwen3-14b");
    }

    private static bool BoolValue(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool HasValue(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string? StringValue(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static TimeSpan? TimeoutValue(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) && int.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}

public sealed record LiveModelRunPacket(
    string PacketId,
    LiveModelRole Role,
    string Prompt,
    IReadOnlyList<string> AllowedOutputKinds,
    string Locale,
    bool RequiresFallback = false,
    string? ContextDigest = null);

public sealed record LiveModelRunOptions(
    LiveModelRunnerOptions RunnerOptions,
    LiveModelRoleRegistry? Registry = null);

public sealed record LiveModelRunResult(
    bool IsAccepted,
    string OutcomeCode,
    string? ErrorCode,
    string Name,
    string PacketId,
    LiveModelRole? Role,
    string? ModelId,
    string? OutputKind,
    LiveModelUiMood? UiMood,
    [property: JsonIgnore]
    JsonElement? Arguments,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record LiveModelRunEvalCase(
    string Name,
    LiveModelRunPacket Packet);

public sealed record SanitizedLiveModelRunEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    LiveModelRole? Role,
    string? ModelId,
    string? OutputKind,
    LiveModelUiMood? UiMood,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public enum LiveModelRole
{
    InHarnessActionGenerator,
    StrongPlanner
}

public enum LiveModelFallbackPolicy
{
    Disabled,
    AllowSameProvider
}

public enum LiveModelUiMood
{
    Unspecified,
    CalmMorning,
    LivelyFood,
    ReflectiveCulture,
    SoftNature,
    RestorativeDowntime,
    Logistics
}
