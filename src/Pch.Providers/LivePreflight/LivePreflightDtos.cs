using Pch.Providers.ModelRoles;

namespace Pch.Providers.LivePreflight;

public sealed record LivePreflightPacket(
    string PacketId,
    IReadOnlyList<LivePreflightRoleProbe> Roles,
    string Locale,
    string? ContextDigest = null);

public sealed record LivePreflightRoleProbe(
    LiveModelRole Role,
    string ProbeId,
    bool RequiresFallback = false);

public sealed record LivePreflightOptions(
    bool Enabled = false,
    bool ApiKeyAvailable = false,
    bool CreditGuardEnabled = true,
    bool StructuredOutputSupported = true,
    bool AllowPaidProviderFallback = false,
    TimeSpan? Timeout = null,
    LivePreflightProviderKind ProviderKind = LivePreflightProviderKind.OpenRouter,
    string Provider = "openrouter",
    string InHarnessModelId = "qwen/qwen3-14b",
    string StrongPlannerModelId = "qwen/qwen3-14b",
    int MaxTokens = 400)
{
    public static LivePreflightOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new LivePreflightOptions(
            Enabled: BoolValue(environment, "PCH_LIVE_MODEL_ENABLED") || BoolValue(environment, "PCH_LIVE_PREFLIGHT_ENABLED"),
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
            StructuredOutputSupported: !BoolValue(environment, "PCH_LIVE_MODEL_SCHEMA_UNSUPPORTED"),
            AllowPaidProviderFallback: BoolValue(environment, "PCH_LIVE_MODEL_ALLOW_PAID_FALLBACK"),
            Timeout: TimeoutValue(environment, "PCH_LIVE_MODEL_TIMEOUT_SECONDS"),
            ProviderKind: ProviderKindValue(environment),
            Provider: StringValue(environment, "PCH_LIVE_MODEL_PROVIDER") ?? "openrouter",
            InHarnessModelId: StringValue(environment, "PCH_LIVE_IN_HARNESS_MODEL") ?? "qwen/qwen3-14b",
            StrongPlannerModelId: StringValue(environment, "PCH_LIVE_STRONG_PLANNER_MODEL") ?? "qwen/qwen3-14b");
    }

    public string ModelFor(LiveModelRole role) =>
        role switch
        {
            LiveModelRole.InHarnessActionGenerator => InHarnessModelId,
            LiveModelRole.StrongPlanner => StrongPlannerModelId,
            _ => string.Empty
        };

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

    private static LivePreflightProviderKind ProviderKindValue(IReadOnlyDictionary<string, string?> environment) =>
        StringValue(environment, "PCH_LIVE_MODEL_PROVIDER") switch
        {
            "openai" => LivePreflightProviderKind.OpenAi,
            "grok" or "xai" or "grok-xai" => LivePreflightProviderKind.GrokXAi,
            _ => LivePreflightProviderKind.OpenRouter
        };
}

public sealed record LivePreflightResult(
    string PacketId,
    IReadOnlyList<LivePreflightRoleResult> Roles,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record LivePreflightRoleResult(
    LiveModelRole Role,
    string ProbeId,
    string ModelId,
    LivePreflightProviderKind ProviderKind,
    string OutputKind);

public sealed record LivePreflightEvalCase(
    string Name,
    LivePreflightPacket Packet);

public sealed record SanitizedLivePreflightEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedLivePreflightRoleRow> Roles,
    int RoleCount,
    int AcceptedRoleCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedLivePreflightRoleRow(
    LiveModelRole Role,
    string ProbeId,
    string ModelId,
    LivePreflightProviderKind ProviderKind,
    string OutputKind);

public enum LivePreflightProviderKind
{
    OpenRouter,
    OpenAi,
    GrokXAi
}
