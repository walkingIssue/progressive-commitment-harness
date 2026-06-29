using Pch.Core;

namespace Pch.Harness;

public sealed class ProjectionService
{
    private const int MaxCandidatesPerPacket = 6;
    private const int MaxFactsPerPacket = 8;
    private const int MaxConstraintsPerPacket = 8;
    private const int MaxMemoryFactsPerPacket = 4;
    private const int MaxPendingConfirmationsPerPacket = 4;

    public StagePacket Project(TripSession session, HarnessStage stage)
    {
        var candidates = session.CandidatePools
            .Where(pool => string.Equals(pool.Stage, stage.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(pool.Stage, "all", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pool => pool.Candidates)
            .OrderByDescending(candidate => candidate.RelevanceScore)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Take(MaxCandidatesPerPacket)
            .Select(ToSummary)
            .ToArray();

        return new StagePacket(
            PacketId: $"packet-{session.SessionId}-{stage}".ToLowerInvariant(),
            SessionId: session.SessionId,
            Stage: stage.ToString(),
            CurrentSubtask: SubtaskFor(stage),
            LoadBearingFacts: BuildFacts(session).Take(MaxFactsPerPacket).ToArray(),
            Candidates: candidates,
            Constraints: session.Mission.Constraints.Select(FormatConstraint).Take(MaxConstraintsPerPacket).ToArray(),
            AuthorityHints: [
                "State writes must come through StatePatchProposal.",
                "Small-model drafts cannot auto-apply protected state.",
                "Every user-visible claim must cite evidence IDs."
            ],
            AllowedActions: AllowedActionsFor(stage),
            TraceRequirements: [
                "Preserve candidate IDs.",
                "Do not invent prices, openings, bookings, or unsupported facts.",
                "Use null for unknown values, not empty strings."
            ]);
    }

    public StagePacket StableFixturePacket()
    {
        var session = SyntheticTripFactory.CreateSession(dayCount: 7);
        return Project(session, HarnessStage.SlotCollection);
    }

    public IReadOnlyList<StagePacket> ProjectSyntheticTrips(params int[] dayCounts)
    {
        return dayCounts
            .Select(dayCount => Project(SyntheticTripFactory.CreateSession(dayCount), HarnessStage.Logistics))
            .ToArray();
    }

    public static CandidateSummary ToSummary(Candidate candidate)
    {
        return new CandidateSummary(
            candidate.CandidateId,
            candidate.Kind.ToString(),
            candidate.Title,
            candidate.Summary,
            candidate.EvidenceIds);
    }

    private static IReadOnlyList<string> BuildFacts(TripSession session)
    {
        return
        [
            $"purpose: {session.Mission.Purpose}",
            $"destination_country: {session.Mission.DestinationCountry}",
            $"date_window: {session.Mission.StartDate:yyyy-MM-dd}/{session.Mission.EndDate:yyyy-MM-dd}",
            $"day_count: {session.Mission.DayCount}",
            .. MemoryFacts(session),
            $"traveler_count: {session.Mission.Travelers.Count}",
            $"selected_candidate_count: {session.SelectedCandidateIds.Count}",
            $"deferred_slot_count: {session.DeferredSlots.Count}",
            .. session.Mission.Commitments
                .OrderBy(commitment => commitment.StartsAt)
                .Select(commitment => $"commitment: {commitment.Title}")
        ];
    }

    private static IReadOnlyList<string> MemoryFacts(TripSession session)
    {
        if (session.MemoryDigest is null)
        {
            return [];
        }

        var pending = session.MemoryDigest.PendingConfirmations
            .Take(MaxPendingConfirmationsPerPacket)
            .Select(pending => $"pending_confirmation: {pending.FieldPath} ({pending.ReasonCode})")
            .ToArray();
        var factLimit = Math.Max(0, MaxMemoryFactsPerPacket - pending.Length);

        return
        [
            .. session.MemoryDigest.LoadBearingFacts
                .Take(factLimit)
                .Select(fact => $"memory: {fact}"),
            .. pending
        ];
    }

    private static string FormatConstraint(Constraint constraint)
    {
        var strength = constraint.IsHard ? "hard" : "soft";
        return $"{constraint.ConstraintId}: {constraint.Label}={constraint.Value} ({strength}, {constraint.Source})";
    }

    private static string SubtaskFor(HarnessStage stage) => stage switch
    {
        HarnessStage.Intake => "Collect mission and traveler basics.",
        HarnessStage.SlotCollection => "Find missing dates, commitments, budget, and traveler needs.",
        HarnessStage.Posture => "Classify trip posture and planning strictness.",
        HarnessStage.DaySkeletonGeneration => "Create a bounded day skeleton.",
        HarnessStage.Logistics => "Compare travel and lodging candidates.",
        HarnessStage.Meals => "Collapse meal options without inventing availability.",
        HarnessStage.ActivitiesDowntime => "Balance activities, downtime, and fixed anchors.",
        HarnessStage.ConflictVerify => "Check conflicts against commitments and constraints.",
        HarnessStage.ApprovalQueue => "Request explicit approval for irreversible or spend actions.",
        HarnessStage.MockedBooking => "Run approval-gated mocked hold or booking actions.",
        HarnessStage.EvidencePacket => "Assemble bookable packet and evidence trace.",
        _ => "Continue current stage."
    };

    private static IReadOnlyList<string> AllowedActionsFor(HarnessStage stage) => stage switch
    {
        HarnessStage.Intake or HarnessStage.SlotCollection => [HarnessAction.EmitFormKind, HarnessAction.DeferSlotKind],
        HarnessStage.ApprovalQueue => [HarnessAction.RequestApprovalKind, HarnessAction.SummarizeKind],
        HarnessStage.MockedBooking => [HarnessAction.RequestApprovalKind, HarnessAction.HandoffKind],
        HarnessStage.EvidencePacket => [HarnessAction.SummarizeKind, HarnessAction.HandoffKind],
        _ => [HarnessAction.EmitChoiceSetKind, HarnessAction.ProposeSearchKind, HarnessAction.StatePatchKind, HarnessAction.DeferSlotKind]
    };
}
