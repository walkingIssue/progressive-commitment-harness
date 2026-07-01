using System.Text.Json.Serialization;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveTurns;

public sealed record LiveTurnPacket(
    string RunId,
    string TurnId,
    string PacketId,
    string SessionId,
    LiveModelRole Role,
    string Locale,
    IReadOnlyList<LiveTurnOutputKind> AllowedOutputKinds,
    IReadOnlyList<LiveTurnTrustedCandidate> TrustedCandidates,
    bool RequiresFallback = false,
    string? ProjectionDigest = null,
    [property: JsonIgnore]
    string? TransientUserPrompt = null);

public sealed record LiveTurnTrustedCandidate(
    string CandidateId,
    string SlotId,
    LiveTurnCandidateCategory Category);

public sealed record LiveTurnOptions(
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
    int MaxTokens = 1_200)
{
    public static LiveTurnOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new LiveTurnOptions(
            Enabled: BoolValue(environment, "PCH_LIVE_MODEL_ENABLED") ||
                BoolValue(environment, "PCH_LIVE_TURN_ENABLED"),
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

public sealed record LiveTurnResult(
    string RunId,
    string TurnId,
    string PacketId,
    string SessionId,
    LiveModelRole Role,
    LiveTurnOutputKind OutputKind,
    LiveMissionProposalResult? MissionProposal,
    LiveTurnPendingQuestion? PendingQuestion,
    LiveTurnChoiceSet? ChoiceSet,
    LiveTurnSummaryNotice? SummaryNotice,
    bool HasUnsafeValue,
    TimeSpan Duration,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record LiveTurnPendingQuestion(
    string QuestionId,
    string FieldPath,
    LiveMissionPendingReason ReasonCode,
    [property: JsonIgnore]
    string? PromptText);

public sealed record LiveTurnChoiceSet(
    string ChoiceSetId,
    IReadOnlyList<LiveTurnChoiceOption> Options,
    LiveTurnUiMood UiMood,
    [property: JsonIgnore]
    string? FramingText);

public sealed record LiveTurnChoiceOption(
    string CandidateId,
    string SlotId,
    LiveTurnCandidateCategory Category,
    [property: JsonIgnore]
    string? Label,
    [property: JsonIgnore]
    string? Rationale);

public sealed record LiveTurnSummaryNotice(
    LiveTurnNoticeKind NoticeKind,
    [property: JsonIgnore]
    string? SummaryText);

public sealed record LiveTurnEvalCase(
    string Name,
    LiveTurnPacket Packet);

public sealed record SanitizedLiveTurnLogRow(
    string Name,
    string RunId,
    string TurnId,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    ProviderFailureClass? FailureClass,
    string? FailureClassCode,
    LiveTurnOutputKind? OutputKind,
    LiveModelRole? Role,
    IReadOnlyList<string> CandidateIds,
    IReadOnlyList<LiveTurnCandidateCategory> CandidateCategories,
    int CandidateCount,
    int? DurationMilliseconds,
    string? DurationBucket,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public enum LiveTurnOutputKind
{
    MissionProposal,
    PendingConfirmationQuestion,
    ChoiceSet,
    SummaryFallbackNotice
}

public enum LiveTurnCandidateCategory
{
    Dining,
    Activity,
    Transit,
    Downtime,
    Lodging
}

public enum LiveTurnUiMood
{
    Unspecified,
    CalmMorning,
    LivelyFood,
    ReflectiveCulture,
    SoftNature,
    RestorativeDowntime,
    Logistics
}

public enum LiveTurnNoticeKind
{
    Summary,
    Fallback,
    ProviderBlocked
}

public enum ProviderFailureClass
{
    ProviderHttp4xx,
    ProviderHttp5xx,
    ProviderRateLimited,
    ProviderTimeout,
    ProviderEmptyContent,
    ProviderMalformedJson,
    ProviderSchemaInvalid,
    ProviderUpstreamModelUnavailable,
    ProviderNetworkError,
    ProviderUnknownError,
    ProviderCreditExhausted,
    ProviderDisabled,
    ProviderKeyMissing,
    ProviderFallbackDisabled
}
