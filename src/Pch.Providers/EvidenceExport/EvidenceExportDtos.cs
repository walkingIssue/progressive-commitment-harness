namespace Pch.Providers.EvidenceExport;

public sealed record EvidenceExportPacket(
    string PacketId,
    TripPlanEvidenceSummary Summary,
    IReadOnlyList<TripPlanEvidenceItem> Evidence,
    IReadOnlyList<TripPlanHoldOutcome> HoldOutcomes,
    string Locale,
    string? ContextDigest = null);

public sealed record TripPlanEvidenceSummary(
    string PlanId,
    int DayCount,
    int SelectedCandidateCount,
    int DeferredCandidateCount,
    int PreparedHoldCount,
    int EvidenceCount);

public sealed record TripPlanEvidenceItem(
    string EvidenceId,
    EvidenceKind Kind,
    string SourceId);

public sealed record TripPlanHoldOutcome(
    string SlotId,
    string CandidateId,
    HoldOutcomeKind Outcome,
    string EvidenceId);

public sealed record EvidenceExportOptions(
    string? Model = null);

public sealed record EvidenceExportResult(
    string PacketId,
    EvidenceExportResultKind Kind,
    TripPlanEvidenceExport Export,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record TripPlanEvidenceExport(
    string PlanId,
    int DayCount,
    int SelectedCandidateCount,
    int DeferredCandidateCount,
    int PreparedHoldCount,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> SlotIds,
    IReadOnlyList<string> CandidateIds);

public enum EvidenceKind
{
    MissionField,
    Constraint,
    Commitment,
    Candidate,
    Hold
}

public enum HoldOutcomeKind
{
    Previewed,
    HoldPrepared,
    Deferred,
    Failed
}

public enum EvidenceExportResultKind
{
    ExportReady,
    Unsupported,
    Malformed
}

public sealed record EvidenceExportEvalCase(
    string Name,
    EvidenceExportPacket Packet);

public sealed record SanitizedEvidenceExportEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    string? PlanId,
    int SelectedCandidateCount,
    int DeferredCandidateCount,
    int PreparedHoldCount,
    int EvidenceCount,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<string> SlotIds,
    IReadOnlyList<string> CandidateIds,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);
