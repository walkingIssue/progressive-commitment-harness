namespace Pch.Providers.RepairPosture;

public sealed record RepairPosturePacket(
    string PacketId,
    IReadOnlyList<RepairPostureNode> Nodes,
    string Locale,
    string? ContextDigest = null);

public sealed record RepairPostureNode(
    string NodeId,
    RepairPostureNodeKind NodeKind,
    RepairPostureNodeStatus Status,
    int DownstreamDependencyCount,
    bool UserConfirmationRequired,
    bool HasAvailabilityOrHold,
    IReadOnlyList<string>? EvidenceIds = null);

public sealed record RepairPostureOptions(
    RepairPostureLiveOptions? Live = null,
    int MaxSuggestions = 4);

public sealed record RepairPostureLiveOptions(
    bool Enabled = false,
    bool ApiKeyAvailable = false,
    bool CreditGuardEnabled = true,
    bool AllowPaidProviderFallback = false,
    TimeSpan? Timeout = null,
    string Provider = "openrouter",
    string Model = "qwen/qwen3-14b",
    RepairPostureProviderKind ProviderKind = RepairPostureProviderKind.OpenRouter);

public sealed record RepairPostureResult(
    string PacketId,
    IReadOnlyList<RepairSuggestion> Suggestions,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record RepairSuggestion(
    string NodeId,
    RepairMode Mode,
    RepairReasonCode ReasonCode,
    int AffectedNodeCount);

public sealed record RepairPostureEvalCase(
    string Name,
    RepairPosturePacket Packet);

public sealed record SanitizedRepairPostureEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedRepairSuggestionRow> Suggestions,
    int NodeCount,
    int SuggestionCount,
    int TotalAffectedNodeCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedRepairSuggestionRow(
    string NodeId,
    RepairPostureNodeKind NodeKind,
    RepairMode Mode,
    RepairReasonCode ReasonCode,
    int AffectedNodeCount);

public sealed record RepairPostureLiveProviderDescriptor(
    RepairPostureProviderKind ProviderKind,
    string ProviderName,
    bool EnabledByDefault,
    bool RequiresApiKey,
    bool SupportsCreditGuard,
    string GuardPolicy);

public enum RepairPostureNodeKind
{
    MissionFact,
    Day,
    Slot,
    SelectedCandidate,
    DeferredCandidate,
    AvailabilityPreview,
    MockHold
}

public enum RepairPostureNodeStatus
{
    Preserved,
    Changed,
    Affected,
    Blocked,
    NeedsUser
}

public enum RepairMode
{
    Keep,
    ReplanDay,
    ReselectCandidate,
    AskUser,
    BlockedReview
}

public enum RepairReasonCode
{
    NoRepairNeeded,
    DownstreamDayImpact,
    CandidateInvalidated,
    NeedsUserConfirmation,
    AvailabilityOrHoldRisk,
    BlockedDependency,
    UnsupportedOrMalformed
}

public enum RepairPostureProviderKind
{
    OpenRouter,
    OpenAi,
    GrokXAi
}
