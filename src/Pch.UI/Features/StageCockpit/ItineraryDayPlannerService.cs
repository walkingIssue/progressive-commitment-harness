using Pch.Core;
using Pch.Harness;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.Mock;

namespace Pch.UI.Features.StageCockpit;

public sealed class ItineraryDayPlannerService
{
    private static readonly DateOnly PlannerDate = new(2027, 4, 2);
    private static readonly DateTimeOffset ConflictAt = new(2027, 4, 2, 10, 0, 0, TimeSpan.Zero);
    private readonly ItinerarySlotCompiler _slotCompiler = new();
    private readonly ICandidateExpansionSource _candidateExpansionSource;

    public ItineraryDayPlannerService(ICandidateExpansionSource? candidateExpansionSource = null)
    {
        _candidateExpansionSource = candidateExpansionSource ?? new MockCandidateExpansionSource();
    }

    public ItineraryDayPlannerResult Run(TripSession session, string runId)
    {
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

        var candidatePools = BuildCandidatePools(providerResult);
        var days = BuildDays(compilation, candidatePools);
        return new(
            new(
                runId,
                "applied",
                days.FirstOrDefault()?.DayId ?? "",
                "selected",
                "deferred",
                "none",
                null,
                null),
            days,
            candidatePools,
            BuildEvidence(candidatePools),
            BuildDigestFacts(compileSession, compilation));
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
        IReadOnlyList<ItineraryCandidatePoolFixture> candidatePools)
    {
        var poolsBySlot = candidatePools.ToDictionary(pool => pool.SlotId, StringComparer.Ordinal);
        return compilation.Days
            .Select(day => new ItineraryDayFixture(
                day.DayId,
                day.Date.ToString("yyyy-MM-dd"),
                "accepted",
                day.Slots
                    .Select(slot => BuildSlot(slot, poolsBySlot))
                    .ToArray()))
            .ToArray();
    }

    private static ItinerarySlotFixture BuildSlot(
        ItinerarySlot slot,
        IReadOnlyDictionary<string, ItineraryCandidatePoolFixture> poolsBySlot)
    {
        poolsBySlot.TryGetValue(slot.SlotId, out var pool);
        var state = slot.Kind switch
        {
            ItinerarySlotKind.FixedCommitment => "selected",
            ItinerarySlotKind.Meal when slot.SlotId.EndsWith("-lunch", StringComparison.Ordinal) => "selected",
            ItinerarySlotKind.Meal or ItinerarySlotKind.Activity or ItinerarySlotKind.Transit or ItinerarySlotKind.Downtime => "deferred",
            ItinerarySlotKind.UnresolvedConfirmation => "blocked",
            _ => "compiled"
        };
        var selectedCandidateId = state == "selected" && pool is not null
            ? pool.Candidates.FirstOrDefault()?.CandidateId
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
        string blockedOutcome)
    {
        return new(
            new(
                runId,
                "blocked",
                dayId,
                "none",
                "none",
                blockedOutcome,
                errorCode,
                blockedReason),
            [],
            [],
            [],
            []);
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
}

public sealed record ItineraryDayPlannerResult(
    ItineraryPlannerOutcomeFixture Outcome,
    IReadOnlyList<ItineraryDayFixture> Days,
    IReadOnlyList<ItineraryCandidatePoolFixture> CandidatePools,
    IReadOnlyList<ItineraryEvidenceFixture> Evidence,
    IReadOnlyList<MemoryDigestFactFixture> DigestFacts);
