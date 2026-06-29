using Pch.Core;

namespace Pch.Harness;

public sealed class TripRunSnapshotBuilder
{
    public const string CompleteCode = "complete";
    public const string PendingConfirmationCode = "pending_confirmation";
    public const string BlockedCompilerCode = "blocked_compiler";
    public const string BlockedCandidateCode = "blocked_candidate";
    public const string HoldPrepRequiredCode = "hold_prep_required";

    private const int MaxMissionFacts = 8;
    private const int MaxMemoryFacts = 6;
    private const int MaxPendingConfirmations = 6;
    private const int MaxItineraryDecisions = 12;
    private const int MaxEvidenceReferences = 12;
    private const int MaxTraceReferences = 12;
    private const int MaxTextLength = 120;

    public TripRunSnapshotResult Build(TripSession session)
    {
        var snapshot = BuildSnapshot(session);
        var (code, summary) = ResolveOutcome(session, snapshot);
        return new TripRunSnapshotResult(
            IsComplete: code is CompleteCode,
            IsBlocked: code is BlockedCompilerCode or BlockedCandidateCode or HoldPrepRequiredCode,
            Code: code,
            Summary: summary,
            Snapshot: snapshot);
    }

    private static TripRunSnapshot BuildSnapshot(TripSession session)
    {
        var evidenceReferences = EvidenceReferences(session).ToArray();
        var traceReferences = TraceReferences(session).ToArray();
        var compilation = session.LastItineraryCompilation;
        return new TripRunSnapshot(
            SnapshotId: $"trip-run-{session.SessionId}-{session.Mission.MissionId}",
            SessionId: session.SessionId,
            Mission: MissionFacts(session),
            Memory: MemorySummary(session.MemoryDigest),
            Itinerary: ItinerarySummary(compilation),
            ItineraryDecisions: session.ItineraryDecisions
                .Take(MaxItineraryDecisions)
                .Select(DecisionSummary)
                .ToArray(),
            MockHold: HoldStatus(session),
            EvidenceReferences: evidenceReferences,
            TraceReferences: traceReferences);
    }

    private static (string Code, string Summary) ResolveOutcome(TripSession session, TripRunSnapshot snapshot)
    {
        if (session.LastItineraryCompilation is not { IsCompiled: true })
        {
            return (BlockedCompilerCode, "Trip run snapshot blocked by itinerary compiler state.");
        }

        if (snapshot.Memory.PendingConfirmationCount > 0)
        {
            return (PendingConfirmationCode, "Trip run snapshot has pending confirmations.");
        }

        if (!session.ItineraryDecisions.Any(decision => decision.Kind is ItinerarySlotDecisionKind.Selected))
        {
            return (BlockedCandidateCode, "Trip run snapshot requires at least one selected itinerary candidate.");
        }

        if (!session.ApprovalTokens.Any(token => !string.IsNullOrWhiteSpace(token.Token)))
        {
            return (HoldPrepRequiredCode, "Trip run snapshot requires approval before mock hold preparation.");
        }

        return (CompleteCode, "Trip run snapshot complete.");
    }

    private static TripRunMissionFacts MissionFacts(TripSession session)
    {
        var mission = session.Mission;
        var facts = new[]
        {
            $"purpose: {SafeText(mission.Purpose)}",
            $"destination_country: {SafeText(mission.DestinationCountry)}",
            $"date_window: {mission.StartDate:yyyy-MM-dd}/{mission.EndDate:yyyy-MM-dd}",
            $"day_count: {mission.DayCount}",
            $"traveler_count: {mission.Travelers.Count}",
            $"constraint_count: {mission.Constraints.Count}",
            $"commitment_count: {mission.Commitments.Count}"
        }.Take(MaxMissionFacts).ToArray();

        return new(
            MissionId: SafeText(mission.MissionId),
            DestinationCountry: SafeText(mission.DestinationCountry),
            StartDate: mission.StartDate,
            EndDate: mission.EndDate,
            DayCount: mission.DayCount,
            TravelerCount: mission.Travelers.Count,
            Facts: facts);
    }

    private static TripRunMemorySummary MemorySummary(StructuredMemoryDigest? digest)
    {
        if (digest is null)
        {
            return new(null, 0, 0, [], [], []);
        }

        return new(
            DigestId: SafeText(digest.DigestId),
            FactCount: digest.LoadBearingFacts.Count,
            PendingConfirmationCount: digest.PendingConfirmations.Count,
            Facts: digest.LoadBearingFacts
                .Where(IsSafeReference)
                .Select(SafeText)
                .Take(MaxMemoryFacts)
                .ToArray(),
            PendingConfirmations: digest.PendingConfirmations
                .Take(MaxPendingConfirmations)
                .Select(pending => new TripRunPendingConfirmation(
                    FieldPath: SafeText(pending.FieldPath),
                    Source: pending.Source.ToString(),
                    ReasonCode: SafeText(pending.ReasonCode),
                    EvidenceIds: SafeReferences(pending.EvidenceIds).ToArray()))
                .ToArray(),
            TraceReferences: SafeReferences(digest.TraceReferences).Take(MaxTraceReferences).ToArray());
    }

    private static TripRunItinerarySummary ItinerarySummary(ItineraryCompilationResult? compilation)
    {
        if (compilation is null)
        {
            return new(false, "missing", 0, 0, 0, []);
        }

        return new(
            IsCompiled: compilation.IsCompiled,
            Code: SafeText(compilation.Code),
            DayCount: compilation.Days.Count,
            SlotCount: compilation.SlotCount,
            ConflictCount: compilation.ConflictCount,
            Conflicts: compilation.Conflicts
                .Take(MaxTraceReferences)
                .Select(conflict => new TripRunConflictSummary(
                    ConflictId: SafeText(conflict.ConflictId),
                    Code: SafeText(conflict.Code),
                    CommitmentIds: SafeReferences(conflict.CommitmentIds).ToArray()))
                .ToArray());
    }

    private static TripRunItineraryDecisionSummary DecisionSummary(ItinerarySlotDecision decision)
    {
        return new(
            DecisionId: SafeText(decision.DecisionId),
            SlotId: SafeText(decision.SlotId),
            Kind: decision.Kind.ToString(),
            SlotKind: decision.SlotKind.ToString(),
            CandidateId: decision.CandidateId is null ? null : SafeText(decision.CandidateId),
            CandidateKind: decision.CandidateKind?.ToString(),
            EvidenceIds: SafeReferences(decision.EvidenceIds).ToArray());
    }

    private static TripRunMockHoldStatus HoldStatus(TripSession session)
    {
        var hasSelection = session.ItineraryDecisions.Any(decision => decision.Kind is ItinerarySlotDecisionKind.Selected);
        var hasApproval = session.ApprovalTokens.Any(token => !string.IsNullOrWhiteSpace(token.Token));
        if (!hasSelection)
        {
            return new("not_ready", true, false, "Mock hold preparation requires an itinerary selection.");
        }

        if (!hasApproval)
        {
            return new("approval_required", true, false, "Mock hold preparation requires approval.");
        }

        return new("ready_for_mock_hold", false, true, "Mock hold preparation placeholder is ready.");
    }

    private static IEnumerable<string> EvidenceReferences(TripSession session)
    {
        return SafeReferences(session.EvidenceTrace.Items.Select(item => item.EvidenceId))
            .Concat(session.MemoryDigest is null ? [] : SafeReferences(session.MemoryDigest.TraceReferences))
            .Concat(session.ItineraryDecisions.SelectMany(decision => SafeReferences(decision.EvidenceIds)))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceReferences);
    }

    private static IEnumerable<TripRunTraceReference> TraceReferences(TripSession session)
    {
        return session.DecisionLedger.Records
            .Where(record => IsSafeReference(record.DecisionId) && IsSafeReference(record.ActionKind))
            .Select(record => new TripRunTraceReference(
                TraceId: SafeText(record.DecisionId),
                Kind: SafeText(record.ActionKind)))
            .Take(MaxTraceReferences);
    }

    private static IEnumerable<string> SafeReferences(IEnumerable<string> values)
    {
        return values
            .Where(IsSafeReference)
            .Select(SafeText)
            .Distinct(StringComparer.Ordinal);
    }

    private static string SafeText(string value)
    {
        var trimmed = value.Trim();
        if (!IsSafeReference(trimmed))
        {
            return "[redacted]";
        }

        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
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
            && !value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record TripRunSnapshotResult(
    bool IsComplete,
    bool IsBlocked,
    string Code,
    string Summary,
    TripRunSnapshot Snapshot);

public sealed record TripRunSnapshot(
    string SnapshotId,
    string SessionId,
    TripRunMissionFacts Mission,
    TripRunMemorySummary Memory,
    TripRunItinerarySummary Itinerary,
    IReadOnlyList<TripRunItineraryDecisionSummary> ItineraryDecisions,
    TripRunMockHoldStatus MockHold,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<TripRunTraceReference> TraceReferences);

public sealed record TripRunMissionFacts(
    string MissionId,
    string DestinationCountry,
    DateOnly StartDate,
    DateOnly EndDate,
    int DayCount,
    int TravelerCount,
    IReadOnlyList<string> Facts);

public sealed record TripRunMemorySummary(
    string? DigestId,
    int FactCount,
    int PendingConfirmationCount,
    IReadOnlyList<string> Facts,
    IReadOnlyList<TripRunPendingConfirmation> PendingConfirmations,
    IReadOnlyList<string> TraceReferences);

public sealed record TripRunPendingConfirmation(
    string FieldPath,
    string Source,
    string ReasonCode,
    IReadOnlyList<string> EvidenceIds);

public sealed record TripRunItinerarySummary(
    bool IsCompiled,
    string Code,
    int DayCount,
    int SlotCount,
    int ConflictCount,
    IReadOnlyList<TripRunConflictSummary> Conflicts);

public sealed record TripRunConflictSummary(
    string ConflictId,
    string Code,
    IReadOnlyList<string> CommitmentIds);

public sealed record TripRunItineraryDecisionSummary(
    string DecisionId,
    string SlotId,
    string Kind,
    string SlotKind,
    string? CandidateId,
    string? CandidateKind,
    IReadOnlyList<string> EvidenceIds);

public sealed record TripRunMockHoldStatus(
    string Code,
    bool RequiresApproval,
    bool IsReadyForMockHold,
    string Summary);

public sealed record TripRunTraceReference(
    string TraceId,
    string Kind);
