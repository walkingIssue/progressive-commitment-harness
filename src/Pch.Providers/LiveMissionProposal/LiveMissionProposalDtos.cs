using System.Text.Json.Serialization;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveMissionProposal;

public sealed record LiveMissionProposalPacket(
    string PacketId,
    string SessionId,
    LiveModelRole Role,
    string Locale,
    IReadOnlyList<string> AllowedOutputKinds,
    bool RequiresFallback = false,
    string? ContextDigest = null);

public sealed record LiveMissionProposalOptions(
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
    public static LiveMissionProposalOptions FromEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return new LiveMissionProposalOptions(
            Enabled: BoolValue(environment, "PCH_LIVE_MODEL_ENABLED") ||
                BoolValue(environment, "PCH_LIVE_MISSION_PROPOSAL_ENABLED"),
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

public sealed record LiveMissionProposalResult(
    string PacketId,
    string SessionId,
    LiveModelRole Role,
    string OutputKind,
    LiveMissionKind MissionKind,
    IReadOnlyList<LiveMissionFieldProposal> Fields,
    IReadOnlyList<LiveMissionCommitmentProposal> Commitments,
    IReadOnlyList<LiveMissionPendingConfirmation> PendingConfirmations,
    bool HasUnsafeValue,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record LiveMissionFieldProposal(
    string FieldPath,
    [property: JsonIgnore]
    string? Value,
    LiveMissionAuthoritySource AuthoritySource,
    [property: JsonIgnore]
    IReadOnlyList<string> EvidenceIds);

public sealed record LiveMissionCommitmentProposal(
    string CommitmentId,
    LiveMissionCommitmentKind CommitmentKind,
    [property: JsonIgnore]
    string? Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    [property: JsonIgnore]
    string? Location,
    bool IsIrreversible,
    bool RequiresSpend,
    LiveMissionCommitmentPriority Priority,
    LiveMissionAuthoritySource AuthoritySource,
    [property: JsonIgnore]
    IReadOnlyList<string> EvidenceIds);

public sealed record LiveMissionPendingConfirmation(
    string ConfirmationId,
    string FieldPath,
    LiveMissionPendingReason ReasonCode,
    LiveMissionAuthoritySource AuthoritySource,
    [property: JsonIgnore]
    IReadOnlyList<string> EvidenceIds);

public sealed record LiveMissionProposalEvalCase(
    string Name,
    LiveMissionProposalPacket Packet);

public sealed record SanitizedLiveMissionProposalEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    string? SessionId,
    LiveModelRole? Role,
    string? OutputKind,
    LiveMissionKind? MissionKind,
    IReadOnlyList<string> FieldPaths,
    IReadOnlyList<LiveMissionCommitmentKind> CommitmentKinds,
    IReadOnlyList<LiveMissionPendingReason> PendingReasonCodes,
    int FieldCount,
    int CommitmentCount,
    int PendingConfirmationCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public enum LiveMissionKind
{
    Vacation,
    Business,
    Funeral,
    HelpingFamily
}

public enum LiveMissionAuthoritySource
{
    UserStated,
    ModelInferencePendingConfirmation,
    TrustedProvider
}

public enum LiveMissionCommitmentKind
{
    Travel,
    Lodging,
    Dining,
    Activity,
    FamilySupport,
    Work
}

public enum LiveMissionCommitmentPriority
{
    Normal,
    High,
    Critical
}

public enum LiveMissionPendingReason
{
    NeedsUserConfirmation,
    NeedsDateConfirmation,
    NeedsBudgetConfirmation,
    NeedsLocationConfirmation
}
