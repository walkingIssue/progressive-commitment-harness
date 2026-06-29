namespace Pch.Providers.AvailabilityPreview;

public sealed record AvailabilityPreviewPacket(
    string PacketId,
    IReadOnlyList<AvailabilityPreviewCandidate> Candidates,
    string Locale,
    string? ContextDigest = null);

public sealed record AvailabilityPreviewCandidate(
    string SlotId,
    string CandidateId,
    AvailabilityPreviewCategory Category);

public sealed record AvailabilityPreviewOptions(
    string? Model = null,
    TimeSpan? Timeout = null);

public sealed record AvailabilityPreviewResult(
    string PacketId,
    AvailabilityPreviewResultKind Kind,
    IReadOnlyList<AvailabilityPreviewCandidateResult> Candidates,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record AvailabilityPreviewCandidateResult(
    string SlotId,
    string CandidateId,
    AvailabilityPreviewCategory Category,
    AvailabilityPreviewCandidateStatus Status,
    decimal? QuoteAmount,
    string? Currency,
    DateTimeOffset? ExpiresAt,
    string? ProviderQuoteReference);

public enum AvailabilityPreviewCategory
{
    Flight,
    Lodging,
    Activity,
    Dining,
    Transit
}

public enum AvailabilityPreviewResultKind
{
    QuoteReady,
    Unavailable,
    Unsupported,
    Malformed
}

public enum AvailabilityPreviewCandidateStatus
{
    QuoteReady,
    Unavailable,
    Unsupported
}

public sealed record AvailabilityPreviewEvalCase(
    string Name,
    AvailabilityPreviewPacket Packet);

public sealed record SanitizedAvailabilityPreviewEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedAvailabilityPreviewCandidateRow> Candidates,
    int CandidateCount,
    int QuoteReadyCount,
    int UnavailableCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedAvailabilityPreviewCandidateRow(
    string SlotId,
    string CandidateId,
    AvailabilityPreviewCategory Category,
    AvailabilityPreviewCandidateStatus Status);
