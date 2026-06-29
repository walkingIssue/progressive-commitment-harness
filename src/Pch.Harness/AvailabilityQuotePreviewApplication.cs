using System.Security.Cryptography;
using System.Text;
using Pch.Core;

namespace Pch.Harness;

public sealed class AvailabilityQuotePreviewApplication
{
    public const string PreviewAcceptedCode = "availability_quote_preview_accepted";
    public const string PreviewUnavailableCode = "availability_quote_preview_unavailable";
    public const string ApprovalRequiredCode = "approval_required_preview";
    public const string InvalidRequestCode = "invalid_request";
    public const string InvalidSessionCode = "invalid_session";
    public const string MalformedInputCode = "malformed_input";
    public const string NoCompiledItineraryCode = "no_compiled_itinerary";
    public const string UnknownSlotCode = "unknown_slot";
    public const string UnknownCandidateCode = "unknown_candidate";
    public const string StaleCompilationSnapshotCode = "stale_compilation_snapshot";
    public const string CandidateOwnershipMismatchCode = "candidate_ownership_mismatch";
    public const string UnsupportedQuoteKindCode = "unsupported_quote_kind";
    public const string UnsupportedQuoteCategoryCode = "unsupported_quote_category";

    private const int MaxEvidenceReferences = 8;
    private const int MaxTraceReferences = 8;
    private const int MaxTextLength = 120;

    private readonly TripRunSnapshotBuilder _snapshotBuilder = new();

    public AvailabilityQuotePreviewContext CurrentContext(TripSession session)
    {
        if (session.LastItineraryCompilation is not { IsCompiled: true } compilation)
        {
            return new("missing", "missing");
        }

        return new(
            CompilationFingerprint: Fingerprint(compilation),
            SnapshotId: SafeText(_snapshotBuilder.Build(session).Snapshot.SnapshotId));
    }

    public AvailabilityQuotePreviewResult Preview(TripSession session, AvailabilityQuotePreviewRequest request)
    {
        var validation = Validate(session, request);
        if (!validation.IsAccepted)
        {
            return Blocked(validation.Code, validation.Summary, validation.Context);
        }

        var candidate = validation.Candidate!;
        var context = validation.Context!;
        if (request.QuoteKind is AvailabilityQuoteKind.Quote
            && (candidate.EstimatedCost.HasValue || !string.IsNullOrWhiteSpace(candidate.Currency)))
        {
            return Blocked(
                ApprovalRequiredCode,
                "Availability quote preview requires approval before quote preparation.",
                validation.Context);
        }

        var evidenceReferences = SafeReferences(candidate.EvidenceIds).Take(MaxEvidenceReferences).ToArray();
        var isAvailable = !candidate.EvidenceIds.Any(evidenceId =>
            evidenceId.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        var code = isAvailable ? PreviewAcceptedCode : PreviewUnavailableCode;
        var status = isAvailable ? "available_preview" : "unavailable_preview";
        var summary = isAvailable
            ? "Availability quote preview accepted."
            : "Availability quote preview is unavailable.";
        var preview = new AvailabilityQuotePreview(
            PreviewId: $"preview-{SafeId(validation.Slot!.SlotId)}-{SafeId(candidate.CandidateId)}",
            SlotId: SafeText(validation.Slot.SlotId),
            CandidateId: SafeText(candidate.CandidateId),
            QuoteKind: request.QuoteKind.ToString(),
            Status: status,
            IsAvailable: isAvailable,
            RequiresApproval: false,
            IsRealHold: false,
            IsRealBooking: false,
            IsRealPayment: false,
            EvidenceReferences: evidenceReferences);

        return new(
            IsAccepted: true,
            IsBlocked: false,
            Code: code,
            Summary: summary,
            Context: context,
            Preview: preview,
            EvidenceReferences: evidenceReferences,
            Trace: Trace(code, summary, accepted: true));
    }

    private AvailabilityQuotePreviewValidation Validate(TripSession session, AvailabilityQuotePreviewRequest request)
    {
        if (request is null)
        {
            return Reject(InvalidRequestCode, "Availability quote preview request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Reject(InvalidSessionCode, "Availability quote preview request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SlotId)
            || string.IsNullOrWhiteSpace(request.CandidateId)
            || string.IsNullOrWhiteSpace(request.CompilationFingerprint)
            || string.IsNullOrWhiteSpace(request.SnapshotId))
        {
            return Reject(MalformedInputCode, "Availability quote preview request failed validation.");
        }

        if (!Enum.IsDefined(request.QuoteKind))
        {
            return Reject(UnsupportedQuoteKindCode, "Availability quote preview kind is unsupported.");
        }

        if (session.LastItineraryCompilation is not { IsCompiled: true } compilation)
        {
            return Reject(NoCompiledItineraryCode, "Availability quote preview requires a compiled itinerary.");
        }

        var context = CurrentContext(session);
        if (!string.Equals(request.CompilationFingerprint, context.CompilationFingerprint, StringComparison.Ordinal)
            || !string.Equals(request.SnapshotId, context.SnapshotId, StringComparison.Ordinal))
        {
            return Reject(
                StaleCompilationSnapshotCode,
                "Availability quote preview request references stale itinerary state.",
                context);
        }

        var slot = compilation.Days
            .SelectMany(day => day.Slots)
            .FirstOrDefault(slot => string.Equals(slot.SlotId, request.SlotId, StringComparison.Ordinal));
        if (slot is null)
        {
            return Reject(UnknownSlotCode, "Availability quote preview references an unknown slot.", context);
        }

        if (slot.Kind != request.SlotKind)
        {
            return Reject(UnsupportedQuoteCategoryCode, "Availability quote preview category is unsupported.", context);
        }

        var isKnownCandidate = session.CandidatePools
            .SelectMany(pool => pool.Candidates)
            .Any(candidate => string.Equals(candidate.CandidateId, request.CandidateId, StringComparison.Ordinal));
        if (!isKnownCandidate)
        {
            return Reject(UnknownCandidateCode, "Availability quote preview references an unknown candidate.", context);
        }

        if (!session.TryGetItineraryCandidateForSlot(slot.SlotId, request.CandidateId, out var candidate))
        {
            return Reject(CandidateOwnershipMismatchCode, "Availability quote preview candidate is not trusted for the slot.", context);
        }

        if (!HasSelectedDecision(session, slot, candidate))
        {
            return Reject(CandidateOwnershipMismatchCode, "Availability quote preview candidate is not trusted for the slot.", context);
        }

        if (candidate.Kind != request.CandidateKind || !CandidateMatchesSlot(slot.Kind, candidate.Kind))
        {
            return Reject(UnsupportedQuoteCategoryCode, "Availability quote preview category is unsupported.", context);
        }

        if (slot.Kind is ItinerarySlotKind.FixedCommitment or ItinerarySlotKind.UnresolvedConfirmation)
        {
            return Reject(UnsupportedQuoteCategoryCode, "Availability quote preview category is unsupported.", context);
        }

        return new(true, PreviewAcceptedCode, "Accepted.", context, slot, candidate);
    }

    private static bool HasSelectedDecision(TripSession session, ItinerarySlot slot, Candidate candidate)
    {
        return session.ItineraryDecisions.Any(decision =>
            decision.Kind is ItinerarySlotDecisionKind.Selected
            && string.Equals(decision.SlotId, slot.SlotId, StringComparison.Ordinal)
            && string.Equals(decision.CandidateId, candidate.CandidateId, StringComparison.Ordinal)
            && decision.CandidateKind == candidate.Kind);
    }

    private static bool CandidateMatchesSlot(ItinerarySlotKind slotKind, CandidateKind candidateKind)
    {
        return slotKind switch
        {
            ItinerarySlotKind.Activity => candidateKind is CandidateKind.Activity,
            ItinerarySlotKind.Transit => candidateKind is CandidateKind.Transit or CandidateKind.Flight,
            ItinerarySlotKind.Meal => candidateKind is CandidateKind.Restaurant,
            ItinerarySlotKind.Sleep => candidateKind is CandidateKind.Hotel,
            ItinerarySlotKind.Downtime => candidateKind is CandidateKind.Activity or CandidateKind.ScheduleBlock,
            ItinerarySlotKind.FixedCommitment => candidateKind is CandidateKind.ScheduleBlock,
            ItinerarySlotKind.UnresolvedConfirmation => false,
            _ => false
        };
    }

    private static AvailabilityQuotePreviewResult Blocked(
        string code,
        string summary,
        AvailabilityQuotePreviewContext? context)
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            Code: code,
            Summary: summary,
            Context: context ?? new("unavailable", "unavailable"),
            Preview: null,
            EvidenceReferences: [],
            Trace: Trace(code, summary, accepted: false));
    }

    private static IReadOnlyList<SessionTraceEvent> Trace(string code, string summary, bool accepted)
    {
        return
        [
            new(
                accepted ? "trace-availability-preview-accepted" : $"trace-availability-preview-blocked-{code}",
                "AvailabilityQuotePreview",
                "availability_quote_preview",
                code,
                summary)
        ];
    }

    private static AvailabilityQuotePreviewValidation Reject(
        string code,
        string summary,
        AvailabilityQuotePreviewContext? context = null)
    {
        return new(false, code, summary, context, null, null);
    }

    private static string Fingerprint(ItineraryCompilationResult compilation)
    {
        var parts = compilation.Days
            .SelectMany(day => day.Slots)
            .OrderBy(slot => slot.SlotId, StringComparer.Ordinal)
            .Select(slot => string.Join('|',
                slot.SlotId,
                slot.Kind,
                slot.Date.ToString("yyyy-MM-dd"),
                slot.StartsAt?.ToString("HH:mm") ?? "none",
                slot.EndsAt?.ToString("HH:mm") ?? "none",
                slot.CommitmentId ?? "none",
                slot.PendingFieldPath ?? "none"));
        var material = string.Join(";", parts.Append(compilation.Code).Append(compilation.SlotCount.ToString()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"compilation-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }

    private static IEnumerable<string> SafeReferences(IEnumerable<string> values)
    {
        return values
            .Where(IsSafeReference)
            .Select(SafeText)
            .Distinct(StringComparer.Ordinal);
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "[redacted]";
        }

        var trimmed = value.Trim();
        if (!IsSafeReference(trimmed))
        {
            return "[redacted]";
        }

        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }

    private static string SafeId(string? value)
    {
        var safe = SafeText(value);
        return string.Equals(safe, "[redacted]", StringComparison.Ordinal) ? "redacted" : safe;
    }

    private static bool IsSafeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PROMPT", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AvailabilityQuotePreviewRequest(
    string SessionId,
    string SlotId,
    string CandidateId,
    ItinerarySlotKind SlotKind,
    CandidateKind CandidateKind,
    AvailabilityQuoteKind QuoteKind,
    string CompilationFingerprint,
    string SnapshotId,
    DateTimeOffset RequestedAt);

public sealed record AvailabilityQuotePreviewResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    AvailabilityQuotePreviewContext Context,
    AvailabilityQuotePreview? Preview,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<SessionTraceEvent> Trace);

public sealed record AvailabilityQuotePreviewContext(
    string CompilationFingerprint,
    string SnapshotId);

public sealed record AvailabilityQuotePreview(
    string PreviewId,
    string SlotId,
    string CandidateId,
    string QuoteKind,
    string Status,
    bool IsAvailable,
    bool RequiresApproval,
    bool IsRealHold,
    bool IsRealBooking,
    bool IsRealPayment,
    IReadOnlyList<string> EvidenceReferences);

public enum AvailabilityQuoteKind
{
    Availability,
    Quote
}

public sealed record AvailabilityQuotePreviewValidation(
    bool IsAccepted,
    string Code,
    string Summary,
    AvailabilityQuotePreviewContext? Context,
    ItinerarySlot? Slot,
    Candidate? Candidate);
