namespace Pch.Providers.Fidelity;

public sealed record FidelityEvalPacket(
    string PacketId,
    IReadOnlyList<FidelityTrustedCandidate> Candidates,
    string Locale,
    string? PromptDigest = null,
    string? ContextDigest = null);

public sealed record FidelityTrustedCandidate(
    string CandidateId,
    FidelityCandidateCategory Category);

public sealed record FidelityEvalOptions(
    string? SmallModel = null,
    string? StrongModel = null,
    TimeSpan? Timeout = null);

public sealed record FidelityEvalSourceResult(
    string PacketId,
    FidelityEvalSourceKind SourceKind,
    FidelityEvalSourceResultKind Kind,
    IReadOnlyList<FidelityCandidateVerdict> Candidates,
    IReadOnlyList<string> ClaimCodes,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record FidelityCandidateVerdict(
    string CandidateId,
    FidelityCandidateDecision Decision,
    double? Confidence = null);

public sealed record FidelityEvalCase(
    string Name,
    FidelityEvalPacket Packet);

public sealed record SanitizedFidelityEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedFidelitySourceRow> Sources,
    IReadOnlyList<SanitizedFidelityCandidateComparisonRow> Candidates,
    int CandidateCount,
    int AgreementCount,
    int DisagreementCount);

public sealed record SanitizedFidelitySourceRow(
    FidelityEvalSourceKind SourceKind,
    int CandidateCount,
    int IncludeCount,
    int ExcludeCount,
    int DeferCount,
    int UnsupportedClaimCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedFidelityCandidateComparisonRow(
    string CandidateId,
    FidelityCandidateCategory Category,
    FidelityCandidateDecision SmallModelDecision,
    FidelityCandidateDecision StrongModelDecision,
    FidelityCandidateDecision HarnessOnlyDecision,
    bool AllSourcesAgree);

public enum FidelityCandidateCategory
{
    Dining,
    Activity,
    Transit,
    Downtime,
    Other
}

public enum FidelityEvalSourceKind
{
    SmallModel,
    StrongModel,
    HarnessOnly
}

public enum FidelityEvalSourceResultKind
{
    Completed,
    SchemaInvalid,
    FallbackRequired
}

public enum FidelityCandidateDecision
{
    Include,
    Exclude,
    Defer
}
