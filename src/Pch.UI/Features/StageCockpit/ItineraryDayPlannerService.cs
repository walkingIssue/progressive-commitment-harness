using Pch.Core;
using Pch.Harness;
using Pch.Providers.Adapters;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.Mock;

namespace Pch.UI.Features.StageCockpit;

public sealed class ItineraryDayPlannerService
{
    private const string LunchSlotId = "slot-20270402-lunch";
    private const string LunchCandidateId = "slot-20270402-lunch-dining-1";
    private const string ActivitySlotId = "slot-20270402-activity";
    private const string ActivityCandidateId = "slot-20270402-activity-activity-1";
    private const string HoldApprovalId = "approval-itinerary-hold-activity";
    private const string MockHoldProvider = "mock-booking";
    private const string ApprovalTokenValue = "ui-hold-approval-token-not-rendered";
    private static readonly DateOnly PlannerDate = new(2027, 4, 2);
    private static readonly DateTimeOffset ConflictAt = new(2027, 4, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] KnownRunIds =
    [
        "itinerary.accepted",
        "itinerary.select-candidate",
        "itinerary.defer-slot",
        "itinerary.conflict",
        "itinerary.missing-date",
        "itinerary.provider-mismatch",
        "itinerary.hold.approval-required",
        "itinerary.hold.approved",
        "itinerary.hold.missing-approval",
        "itinerary.hold.provider-mismatch"
    ];
    private readonly ItinerarySlotCompiler _slotCompiler = new();
    private readonly ICandidateExpansionSource _candidateExpansionSource;
    private readonly IBookingCommitAdapter _bookingCommitAdapter;

    public ItineraryDayPlannerService(
        ICandidateExpansionSource? candidateExpansionSource = null,
        IBookingCommitAdapter? bookingCommitAdapter = null)
    {
        _candidateExpansionSource = candidateExpansionSource ?? new MockCandidateExpansionSource();
        _bookingCommitAdapter = bookingCommitAdapter ?? new MockBookingCommitAdapter();
    }

    public ItineraryDayPlannerResult Run(TripSession session, string runId)
    {
        if (!KnownRunIds.Contains(runId, StringComparer.Ordinal))
        {
            return Blocked(
                runId,
                "",
                "PCH_UI_ITINERARY_UNKNOWN_SCENARIO",
                "Itinerary day planner scenario is not recognized.",
                "blocked_unknown");
        }

        var compileSession = runId == "itinerary.conflict"
            ? CreateConflictSession()
            : session;
        var request = CreateCompilationRequest(compileSession, runId);
        var compilation = _slotCompiler.Compile(compileSession, request);
        if (!compilation.IsCompiled)
        {
            return BlockedFromCompilation(runId, compilation);
        }

        var packet = CreateCandidateExpansionPacket(compileSession, runId, compilation);
        var providerResult = runId == "itinerary.provider-mismatch"
            ? CreateProviderMismatchResult(packet)
            : _candidateExpansionSource
                .ExpandAsync(packet, new CandidateExpansionOptions(CandidatesPerSlot: 2))
                .GetAwaiter()
                .GetResult();
        var candidateValidation = ValidateCandidateExpansion(packet, providerResult);
        if (!candidateValidation.IsAccepted)
        {
            return Blocked(
                runId,
                compilation.Days.FirstOrDefault()?.DayId ?? "",
                candidateValidation.ErrorCode ?? "PCH_UI_ITINERARY_CANDIDATE_BLOCKED",
                candidateValidation.BlockedReason ?? "Candidate expansion was blocked.",
                "blocked_candidate_expansion");
        }

        var interaction = CreateInteraction(runId);
        if (interaction.DeferredSlotId is not null)
        {
            session.DeferSlot(interaction.DeferredSlotId, "User deferred itinerary slot.");
        }

        var candidatePools = BuildCandidatePools(providerResult);
        var holdResult = ApplyHold(runId, candidatePools);
        var days = holdResult.SuppressItineraryRendering
            ? []
            : BuildDays(compilation, candidatePools, interaction);
        var renderedCandidatePools = holdResult.SuppressItineraryRendering
            ? []
            : candidatePools;
        var state = holdResult.State ?? (runId == "itinerary.hold.approval-required" ? "approval-required" : "applied");
        var selectedOutcome = interaction.SelectedCandidateId is null ? "none" : "selected";
        var deferredOutcome = interaction.DeferredSlotId is null ? "none" : "deferred";
        var blockedOutcome = holdResult.BlockedOutcome ?? "none";

        return new(
            new(
                runId,
                state,
                compilation.Days.FirstOrDefault()?.DayId ?? "",
                selectedOutcome,
                deferredOutcome,
                blockedOutcome,
                holdResult.HoldOutcome,
                holdResult.ApprovalId,
                holdResult.ErrorCode,
                holdResult.BlockedReason),
            days,
            renderedCandidatePools,
            holdResult.SuppressItineraryRendering ? [] : BuildEvidence(candidatePools),
            holdResult.SuppressItineraryRendering ? [] : BuildDigestFacts(compileSession, compilation),
            holdResult.Holds);
    }

    private static ItineraryCompilationRequest CreateCompilationRequest(TripSession session, string runId)
    {
        if (runId == "itinerary.missing-date")
        {
            return new(
                session.SessionId,
                default(DateOnly),
                PlannerDate,
                null,
                ["ui-itinerary-day-planner"]);
        }

        return new(
            session.SessionId,
            PlannerDate,
            PlannerDate,
            null,
            ["ui-itinerary-day-planner"]);
    }

    private static TripSession CreateConflictSession()
    {
        var session = SyntheticTripFactory.CreateSession(2);
        session.ReplaceMission(session.Mission with
        {
            Commitments =
            [
                .. session.Mission.Commitments,
                new(
                    "commitment-conflict-a",
                    CommitmentKind.FixedAnchor,
                    "Family appointment",
                    ConflictAt,
                    ConflictAt.AddHours(2),
                    "Tokyo",
                    false,
                    false),
                new(
                    "commitment-conflict-b",
                    CommitmentKind.FixedAnchor,
                    "Overlapping appointment",
                    ConflictAt.AddHours(1),
                    ConflictAt.AddHours(3),
                    "Tokyo",
                    false,
                    false)
            ]
        });
        return session;
    }

    private static ItineraryInteractionPlan CreateInteraction(string runId) => runId switch
    {
        "itinerary.defer-slot" => new(null, null, ActivitySlotId),
        "itinerary.hold.approval-required"
            or "itinerary.hold.approved"
            or "itinerary.hold.missing-approval"
            or "itinerary.hold.provider-mismatch" => new(ActivitySlotId, ActivityCandidateId, null),
        "itinerary.select-candidate" => new(LunchSlotId, LunchCandidateId, null),
        _ => new(LunchSlotId, LunchCandidateId, ActivitySlotId)
    };

    private static CandidateExpansionPacket CreateCandidateExpansionPacket(
        TripSession session,
        string runId,
        ItineraryCompilationResult compilation)
    {
        var slots = compilation.Days
            .SelectMany(day => day.Slots)
            .Select(slot => TryCreateCandidateSlot(slot, out var candidateSlot) ? candidateSlot : null)
            .OfType<CandidateExpansionSlot>()
            .ToArray();

        return new(
            $"candidate-packet-{session.SessionId}-{runId}",
            slots,
            "en-US",
            "structured-itinerary-digest");
    }

    private static bool TryCreateCandidateSlot(ItinerarySlot slot, out CandidateExpansionSlot candidateSlot)
    {
        var category = slot.Kind switch
        {
            ItinerarySlotKind.Meal => CandidateCategory.Dining,
            ItinerarySlotKind.Activity => CandidateCategory.Activity,
            ItinerarySlotKind.Transit => CandidateCategory.Transit,
            ItinerarySlotKind.Downtime => CandidateCategory.Downtime,
            _ => (CandidateCategory?)null
        };
        if (category is null)
        {
            candidateSlot = null!;
            return false;
        }

        var duration = slot.StartsAt is null || slot.EndsAt is null
            ? null
            : (int?)(slot.EndsAt.Value.ToTimeSpan() - slot.StartsAt.Value.ToTimeSpan()).TotalMinutes;
        candidateSlot = new(
            slot.SlotId,
            category.Value,
            "Tokyo",
            duration);
        return true;
    }

    private static CandidateExpansionResult CreateProviderMismatchResult(CandidateExpansionPacket packet)
    {
        return new(
            packet.PacketId,
            [
                new(
                    "slot-provider-unknown",
                    CandidateCategory.Activity,
                    [
                        new(
                            "candidate-provider-unknown",
                            CandidateCategory.Activity,
                            "Provider mismatch candidate",
                            ["provider-mismatch"],
                            90,
                            CandidateCostLevel.Medium,
                            false)
                    ])
            ],
            ResponseContentLength: 0,
            MockCandidateExpansionSource.ProviderName,
            "mock-candidate-expansion-deterministic",
            "mock-candidates-provider-mismatch");
    }

    private static CandidateExpansionValidation ValidateCandidateExpansion(
        CandidateExpansionPacket packet,
        CandidateExpansionResult result)
    {
        if (!string.Equals(packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return CandidateExpansionValidation.Blocked(
                "PCH_UI_ITINERARY_CANDIDATE_PACKET_MISMATCH",
                "Candidate expansion did not match the compiled itinerary packet.");
        }

        var trustedSlots = packet.Slots.ToDictionary(slot => slot.SlotId, StringComparer.Ordinal);
        var seenSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expandedSlot in result.Slots)
        {
            if (!seenSlots.Add(expandedSlot.SlotId))
            {
                return CandidateExpansionValidation.Blocked(
                    "PCH_UI_ITINERARY_CANDIDATE_SLOT_MISMATCH",
                    "Candidate expansion returned an invalid compiled slot.");
            }

            if (!trustedSlots.TryGetValue(expandedSlot.SlotId, out var trustedSlot)
                || trustedSlot.Category != expandedSlot.Category)
            {
                return CandidateExpansionValidation.Blocked(
                    "PCH_UI_ITINERARY_CANDIDATE_SLOT_MISMATCH",
                    "Candidate expansion returned an invalid compiled slot.");
            }
        }

        if (seenSlots.Count != trustedSlots.Count)
        {
            return CandidateExpansionValidation.Blocked(
                "PCH_UI_ITINERARY_CANDIDATE_SLOT_MISMATCH",
                "Candidate expansion returned an incomplete compiled slot set.");
        }

        return CandidateExpansionValidation.Accepted();
    }

    private ItineraryHoldResult ApplyHold(
        string runId,
        IReadOnlyList<ItineraryCandidatePoolFixture> candidatePools)
    {
        if (!runId.StartsWith("itinerary.hold.", StringComparison.Ordinal))
        {
            return ItineraryHoldResult.None();
        }

        if (!CandidateExists(candidatePools, ActivitySlotId, ActivityCandidateId))
        {
            return ItineraryHoldResult.Blocked(
                "blocked_provider_mismatch",
                "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH",
                "Mock hold candidate did not match the compiled provider candidate set.",
                "provider_mismatch",
                HoldApprovalId,
                [HoldFixture(runId, "blocked_provider_mismatch", "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH")],
                suppressItineraryRendering: true);
        }

        return runId switch
        {
            "itinerary.hold.approval-required" => ItineraryHoldResult.ApprovalRequired(
                HoldApprovalId,
                [HoldFixture(runId, "approval_required")]),
            "itinerary.hold.approved" => ApplyApprovedHold(runId),
            "itinerary.hold.missing-approval" => ApplyMissingApprovalHold(runId),
            "itinerary.hold.provider-mismatch" => ItineraryHoldResult.Blocked(
                "blocked_provider_mismatch",
                "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH",
                "Mock hold provider response did not match the selected candidate.",
                "provider_mismatch",
                HoldApprovalId,
                [HoldFixture(runId, "blocked_provider_mismatch", "PCH_UI_ITINERARY_HOLD_PROVIDER_MISMATCH")]),
            _ => ItineraryHoldResult.None()
        };
    }

    private ItineraryHoldResult ApplyApprovedHold(string runId)
    {
        var result = _bookingCommitAdapter
            .HoldAsync(new BookingCommitRequest(ActivityCandidateId, ApprovalTokenValue))
            .GetAwaiter()
            .GetResult();

        return ItineraryHoldResult.Applied(
            HoldApprovalId,
            [
                HoldFixture(
                    runId,
                    "hold_applied",
                    confirmationId: result.ConfirmationId,
                    provider: result.Provider)
            ]);
    }

    private ItineraryHoldResult ApplyMissingApprovalHold(string runId)
    {
        try
        {
            _bookingCommitAdapter
                .HoldAsync(new BookingCommitRequest(ActivityCandidateId, null))
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidOperationException)
        {
            return ItineraryHoldResult.Blocked(
                "blocked_missing_approval",
                "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED",
                "Mock hold requires approval before provider handoff.",
                "missing_approval",
                HoldApprovalId,
                [HoldFixture(runId, "blocked_missing_approval", "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED")]);
        }

        return ItineraryHoldResult.Blocked(
            "blocked_missing_approval",
            "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED",
            "Mock hold requires approval before provider handoff.",
            "missing_approval",
            HoldApprovalId,
            [HoldFixture(runId, "blocked_missing_approval", "PCH_UI_ITINERARY_HOLD_APPROVAL_REQUIRED")]);
    }

    private static bool CandidateExists(
        IReadOnlyList<ItineraryCandidatePoolFixture> candidatePools,
        string slotId,
        string candidateId)
    {
        return candidatePools.Any(pool => string.Equals(pool.SlotId, slotId, StringComparison.Ordinal)
            && pool.Candidates.Any(candidate => string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal)));
    }

    private static ItineraryHoldFixture HoldFixture(
        string runId,
        string outcome,
        string? errorCode = null,
        string? confirmationId = null,
        string provider = MockHoldProvider)
    {
        return new(
            $"hold-{runId}",
            ActivitySlotId,
            ActivityCandidateId,
            HoldApprovalId,
            outcome,
            provider,
            confirmationId,
            errorCode);
    }

    private static IReadOnlyList<ItineraryCandidatePoolFixture> BuildCandidatePools(CandidateExpansionResult result)
    {
        return result.Slots
            .Select(slot => new ItineraryCandidatePoolFixture(
                $"pool-{slot.SlotId}",
                slot.SlotId,
                slot.Candidates
                    .Select(candidate => new ItineraryCandidateFixture(
                        candidate.CandidateId,
                        ToCategoryCode(candidate.Category),
                        candidate.DisplayName,
                        candidate.Tags
                            .Select(tag => $"evidence.candidate.{tag}")
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()))
                    .ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<ItineraryDayFixture> BuildDays(
        ItineraryCompilationResult compilation,
        IReadOnlyList<ItineraryCandidatePoolFixture> candidatePools,
        ItineraryInteractionPlan interaction)
    {
        var poolsBySlot = candidatePools.ToDictionary(pool => pool.SlotId, StringComparer.Ordinal);
        return compilation.Days
            .Select(day => new ItineraryDayFixture(
                day.DayId,
                day.Date.ToString("yyyy-MM-dd"),
                "accepted",
                day.Slots
                    .Select(slot => BuildSlot(slot, poolsBySlot, interaction))
                    .ToArray()))
            .ToArray();
    }

    private static ItinerarySlotFixture BuildSlot(
        ItinerarySlot slot,
        IReadOnlyDictionary<string, ItineraryCandidatePoolFixture> poolsBySlot,
        ItineraryInteractionPlan interaction)
    {
        poolsBySlot.TryGetValue(slot.SlotId, out var pool);
        var isSelected = string.Equals(slot.SlotId, interaction.SelectedSlotId, StringComparison.Ordinal);
        var isDeferred = string.Equals(slot.SlotId, interaction.DeferredSlotId, StringComparison.Ordinal);
        var state = slot.Kind switch
        {
            ItinerarySlotKind.FixedCommitment => "selected",
            _ when isSelected => "selected",
            _ when isDeferred => "deferred",
            ItinerarySlotKind.Meal or ItinerarySlotKind.Activity or ItinerarySlotKind.Transit or ItinerarySlotKind.Downtime => "candidate",
            ItinerarySlotKind.UnresolvedConfirmation => "blocked",
            _ => "compiled"
        };
        var selectedCandidateId = state == "selected" && pool is not null
            ? interaction.SelectedCandidateId ?? pool.Candidates.FirstOrDefault()?.CandidateId
            : slot.CommitmentId;

        return new(
            slot.SlotId,
            ToSlotTypeCode(slot.Kind),
            state,
            pool?.PoolId,
            selectedCandidateId);
    }

    private static IReadOnlyList<ItineraryEvidenceFixture> BuildEvidence(
        IReadOnlyList<ItineraryCandidatePoolFixture> candidatePools)
    {
        return candidatePools
            .SelectMany(pool => pool.Candidates.SelectMany(candidate => candidate.EvidenceIds))
            .Distinct(StringComparer.Ordinal)
            .Select(evidenceId => new ItineraryEvidenceFixture(
                evidenceId,
                "provider-candidate-expansion",
                evidenceId.Contains("recovery", StringComparison.Ordinal) ? "deferred" : "selected"))
            .ToArray();
    }

    private static IReadOnlyList<MemoryDigestFactFixture> BuildDigestFacts(
        TripSession session,
        ItineraryCompilationResult compilation)
    {
        return
        [
            new(
                "itinerary.digest.destination",
                $"destination_country: {session.Mission.DestinationCountry}",
                "structured-digest",
                $"digest-{session.Mission.MissionId}"),
            new(
                "itinerary.digest.date-window",
                $"date_window: {compilation.Days[0].Date:yyyy-MM-dd}/{compilation.Days[^1].Date:yyyy-MM-dd}",
                "structured-digest",
                $"digest-{session.Mission.MissionId}"),
            new(
                "itinerary.digest.slot-count",
                $"slot_count: {compilation.SlotCount}",
                "itinerary-slot-compiler",
                $"digest-{session.Mission.MissionId}")
        ];
    }

    private static ItineraryDayPlannerResult BlockedFromCompilation(
        string runId,
        ItineraryCompilationResult compilation)
    {
        return compilation.Code switch
        {
            "fixed_commitment_conflict" => Blocked(
                runId,
                "day-20270402",
                "PCH_UI_ITINERARY_FIXED_COMMITMENT_CONFLICT",
                "Fixed commitment conflict blocks itinerary compilation.",
                "blocked_conflict"),
            "missing_date_window" => Blocked(
                runId,
                "",
                "PCH_UI_ITINERARY_MISSING_DATE_WINDOW",
                "Itinerary day planner requires an applied start and end date.",
                "blocked_date_window"),
            _ => Blocked(
                runId,
                "",
                "PCH_UI_ITINERARY_COMPILER_BLOCKED",
                "Itinerary slot compilation was blocked.",
                "blocked_compiler")
        };
    }

    private static ItineraryDayPlannerResult Blocked(
        string runId,
        string dayId,
        string errorCode,
        string blockedReason,
        string blockedOutcome,
        string holdOutcome = "none",
        string? approvalId = null,
        IReadOnlyList<ItineraryHoldFixture>? holds = null)
    {
        return new(
            new(
                runId,
                "blocked",
                dayId,
                "none",
                "none",
                blockedOutcome,
                holdOutcome,
                approvalId,
                errorCode,
                blockedReason),
            [],
            [],
            [],
            [],
            holds ?? []);
    }

    private static string ToSlotTypeCode(ItinerarySlotKind kind) => kind switch
    {
        ItinerarySlotKind.Sleep => "sleep",
        ItinerarySlotKind.Meal => "meal",
        ItinerarySlotKind.Transit => "transit",
        ItinerarySlotKind.FixedCommitment => "fixed_commitment",
        ItinerarySlotKind.Downtime => "downtime",
        ItinerarySlotKind.Activity => "activity",
        ItinerarySlotKind.UnresolvedConfirmation => "unresolved_confirmation",
        _ => "unknown"
    };

    private static string ToCategoryCode(CandidateCategory category) => category switch
    {
        CandidateCategory.Dining => "dining",
        CandidateCategory.Activity => "activity",
        CandidateCategory.Transit => "transit",
        CandidateCategory.Downtime => "downtime",
        _ => "unknown"
    };

    private sealed record CandidateExpansionValidation(
        bool IsAccepted,
        string? ErrorCode,
        string? BlockedReason)
    {
        public static CandidateExpansionValidation Accepted() => new(true, null, null);

        public static CandidateExpansionValidation Blocked(string errorCode, string blockedReason) =>
            new(false, errorCode, blockedReason);
    }

    private sealed record ItineraryInteractionPlan(
        string? SelectedSlotId,
        string? SelectedCandidateId,
        string? DeferredSlotId);

    private sealed record ItineraryHoldResult(
        string? State,
        string HoldOutcome,
        string? BlockedOutcome,
        string? ErrorCode,
        string? BlockedReason,
        string? ApprovalId,
        IReadOnlyList<ItineraryHoldFixture> Holds,
        bool SuppressItineraryRendering)
    {
        public static ItineraryHoldResult None() => new(null, "none", null, null, null, null, [], false);

        public static ItineraryHoldResult ApprovalRequired(
            string approvalId,
            IReadOnlyList<ItineraryHoldFixture> holds) =>
            new("approval-required", "approval_required", "none", null, null, approvalId, holds, false);

        public static ItineraryHoldResult Applied(
            string approvalId,
            IReadOnlyList<ItineraryHoldFixture> holds) =>
            new("applied", "hold_applied", "none", null, null, approvalId, holds, false);

        public static ItineraryHoldResult Blocked(
            string blockedOutcome,
            string errorCode,
            string blockedReason,
            string holdOutcome,
            string approvalId,
            IReadOnlyList<ItineraryHoldFixture> holds,
            bool suppressItineraryRendering = false) =>
            new("blocked", holdOutcome, blockedOutcome, errorCode, blockedReason, approvalId, holds, suppressItineraryRendering);
    }
}

public sealed record ItineraryDayPlannerResult(
    ItineraryPlannerOutcomeFixture Outcome,
    IReadOnlyList<ItineraryDayFixture> Days,
    IReadOnlyList<ItineraryCandidatePoolFixture> CandidatePools,
    IReadOnlyList<ItineraryEvidenceFixture> Evidence,
    IReadOnlyList<MemoryDigestFactFixture> DigestFacts,
    IReadOnlyList<ItineraryHoldFixture> Holds);
