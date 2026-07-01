using System.Text.Json.Serialization;
using Pch.Providers.LivePreflight;
using Pch.Providers.OpenAi;
using Pch.Providers.OpenRouter;

namespace Pch.Providers.PlannerPrimitives;

public sealed record PlannerToolManifestMirror(
    string ManifestId,
    string ManifestVersion,
    string GraphRevision,
    string SessionId,
    string Stage,
    IReadOnlyList<PlannerPrimitiveDefinition> AllowedPrimitives,
    IReadOnlyList<string> AllowedFieldPaths,
    IReadOnlyList<string> AllowedMoodTokens,
    int MaxPrimitiveCount);

public sealed record PlannerPrimitiveDefinition(
    string PrimitiveId,
    string PrimitiveKind,
    string RendererKey);

public sealed record PlannerModelRequest(
    string RunId,
    string TurnId,
    PlannerToolManifestMirror Manifest,
    string Locale,
    [property: JsonIgnore]
    string? RuntimePrompt = null,
    string? PromptDigest = null);

public sealed record PlannerModelOptions(
    bool Enabled = false,
    bool ApiKeyAvailable = false,
    bool CreditGuardEnabled = true,
    bool AllowPaidProviderFallback = false,
    bool RepairEnabled = true,
    TimeSpan? Timeout = null,
    LivePreflightProviderKind ProviderKind = LivePreflightProviderKind.OpenRouter,
    string Provider = "openrouter",
    string Model = "qwen/qwen3-14b",
    int MaxTokens = 1_200)
{
    public static PlannerModelOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var provider = StringValue(environment, "PCH_LIVE_MODEL_PROVIDER") ?? "openrouter";
        return new PlannerModelOptions(
            Enabled: BoolValue(environment, "PCH_LIVE_MODEL_ENABLED") ||
                BoolValue(environment, "PCH_PLANNER_PRIMITIVE_ENABLED"),
            ApiKeyAvailable: BoolValue(environment, "PCH_LIVE_MODEL_KEY_AVAILABLE") ||
                HasValue(environment, "OPENROUTER_API_KEY") ||
                HasValue(environment, "OPENAI_API_KEY") ||
                HasValue(environment, "XAI_API_KEY") ||
                HasValue(environment, "GROK_API_KEY"),
            CreditGuardEnabled: !BoolValue(environment, "PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"),
            AllowPaidProviderFallback: BoolValue(environment, "PCH_LIVE_MODEL_ALLOW_PAID_FALLBACK"),
            RepairEnabled: !BoolValue(environment, "PCH_PLANNER_PRIMITIVE_DISABLE_REPAIR"),
            Timeout: TimeoutValue(environment, "PCH_LIVE_MODEL_TIMEOUT_SECONDS"),
            ProviderKind: ProviderKindValue(environment),
            Provider: provider,
            Model: StringValue(environment, "PCH_PLANNER_PRIMITIVE_MODEL") ??
                StringValue(environment, "PCH_LIVE_STRONG_PLANNER_MODEL") ??
                DefaultModelForProvider(provider));
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

    private static LivePreflightProviderKind ProviderKindValue(IReadOnlyDictionary<string, string?> environment) =>
        StringValue(environment, "PCH_LIVE_MODEL_PROVIDER") switch
        {
            "openai" => LivePreflightProviderKind.OpenAi,
            "grok" or "xai" or "grok-xai" => LivePreflightProviderKind.GrokXAi,
            _ => LivePreflightProviderKind.OpenRouter
        };

    private static string DefaultModelForProvider(string provider) =>
        provider switch
        {
            "openai" => OpenAiOptions.DefaultModel,
            _ => OpenRouterOptions.DefaultModel
        };
}

public sealed record PlannerModelResult(
    string ManifestId,
    string ManifestVersion,
    string GraphRevision,
    string SessionId,
    PlannerModelOutputKind OutputKind,
    IReadOnlyList<PlannerPrimitiveInvocation> Primitives,
    bool WasRepaired,
    bool HasUnsafeValue,
    TimeSpan Duration,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record PlannerPrimitiveInvocation(
    string PrimitiveId,
    string PrimitiveKind,
    string InstanceId,
    string RendererKey,
    string? FieldPath,
    string? MoodToken,
    IReadOnlyList<string> CandidateIds,
    [property: JsonIgnore]
    string? Label,
    [property: JsonIgnore]
    string? PromptText);

public sealed record PlannerModelEvalCase(
    string Name,
    PlannerModelRequest Request);

public sealed record SanitizedPlannerModelLogRow(
    string Name,
    string RunId,
    string TurnId,
    string ManifestId,
    string ManifestVersion,
    bool Passed,
    string OutcomeCode,
    string? FailureClassCode,
    PlannerModelOutputKind? OutputKind,
    IReadOnlyList<string> PrimitiveIds,
    int PrimitiveCount,
    bool WasRepaired,
    int? DurationMilliseconds,
    string? DurationBucket,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public enum PlannerModelOutputKind
{
    CompositeForm,
    ToolSearchRequest,
    ToolGapRequest
}
