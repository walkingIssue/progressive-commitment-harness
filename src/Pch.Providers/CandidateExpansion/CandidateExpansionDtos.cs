namespace Pch.Providers.CandidateExpansion;

public sealed record CandidateExpansionPacket(
    string PacketId,
    IReadOnlyList<CandidateExpansionSlot> Slots,
    string Locale,
    string? ContextDigest = null);

public sealed record CandidateExpansionSlot(
    string SlotId,
    CandidateCategory Category,
    string? LocationHint = null,
    int? DurationMinutes = null);

public sealed record CandidateExpansionOptions(
    string? Model = null,
    int CandidatesPerSlot = 2);

public sealed record CandidateExpansionResult(
    string PacketId,
    IReadOnlyList<CandidateSlotExpansion> Slots,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record CandidateSlotExpansion(
    string SlotId,
    CandidateCategory Category,
    IReadOnlyList<ItineraryCandidate> Candidates);

public sealed record ItineraryCandidate(
    string CandidateId,
    CandidateCategory Category,
    string DisplayName,
    IReadOnlyList<string> Tags,
    int? DurationMinutes,
    CandidateCostLevel CostLevel,
    bool RequiresBooking);

public enum CandidateCategory
{
    Dining,
    Activity,
    Transit,
    Downtime
}

public enum CandidateCostLevel
{
    Free,
    Low,
    Medium,
    High
}

public sealed record CandidateExpansionEvalCase(
    string Name,
    CandidateExpansionPacket Packet);

public sealed record SanitizedCandidateExpansionEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedCandidateSlotEvalRow> Slots,
    int TotalCandidateCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedCandidateSlotEvalRow(
    string SlotId,
    CandidateCategory Category,
    int CandidateCount);
