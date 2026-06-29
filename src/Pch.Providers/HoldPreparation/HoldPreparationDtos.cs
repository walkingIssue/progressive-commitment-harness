using Pch.Providers.CandidateExpansion;

namespace Pch.Providers.HoldPreparation;

public sealed record HoldPreparationPacket(
    string PacketId,
    HoldPreparationOperation Operation,
    IReadOnlyList<SelectedItineraryCandidate> SelectedCandidates,
    string Locale,
    string? ApprovalToken = null,
    string? ContextDigest = null);

public sealed record SelectedItineraryCandidate(
    string SlotId,
    string CandidateId,
    CandidateCategory Category);

public sealed record HoldPreparationOptions(
    string? Model = null,
    string RequiredApprovalToken = "mock-approval-token");

public sealed record HoldPreparationResult(
    string PacketId,
    HoldPreparationResultKind Kind,
    IReadOnlyList<HoldPreparationCandidateResult> Candidates,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record HoldPreparationCandidateResult(
    string SlotId,
    string CandidateId,
    CandidateCategory Category,
    HoldPreparationCandidateStatus Status,
    string? HoldReferenceId = null,
    DateTimeOffset? ExpiresAt = null);

public enum HoldPreparationOperation
{
    Preview,
    Hold
}

public enum HoldPreparationResultKind
{
    PreviewReady,
    HoldPrepared,
    ApprovalMissing,
    ApprovalRejected,
    Unsupported
}

public enum HoldPreparationCandidateStatus
{
    PreviewAvailable,
    HoldPrepared,
    Unsupported
}

public sealed record HoldPreparationEvalCase(
    string Name,
    HoldPreparationPacket Packet);

public sealed record SanitizedHoldPreparationEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedHoldCandidateRow> Candidates,
    int CandidateCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedHoldCandidateRow(
    string SlotId,
    string CandidateId,
    CandidateCategory Category);
