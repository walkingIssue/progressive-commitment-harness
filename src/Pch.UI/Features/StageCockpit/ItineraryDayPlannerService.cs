namespace Pch.UI.Features.StageCockpit;

public sealed class ItineraryDayPlannerService
{
    public ItineraryDayPlannerResult Run(string runId)
    {
        return runId switch
        {
            "itinerary.accepted" => Accepted(),
            "itinerary.conflict" => Blocked(
                runId,
                "day-2026-10-06",
                "PCH_UI_ITINERARY_FIXED_COMMITMENT_CONFLICT",
                "Fixed commitment conflicts with the candidate slot.",
                "blocked_conflict"),
            "itinerary.missing-date" => Blocked(
                runId,
                "",
                "PCH_UI_ITINERARY_MISSING_DATE_WINDOW",
                "Itinerary day planner requires an applied start and end date.",
                "blocked_date_window"),
            _ => Blocked(
                runId,
                "",
                "PCH_UI_ITINERARY_UNKNOWN_SCENARIO",
                "Itinerary day planner scenario is not recognized.",
                "blocked_unknown")
        };
    }

    private static ItineraryDayPlannerResult Accepted()
    {
        ItineraryCandidatePoolFixture lunchPool = new(
            "pool.day-2026-10-06.lunch",
            "slot.day-2026-10-06.lunch",
            [
                new(
                    "candidate.lunch.ramen",
                    "meal",
                    "Low-friction ramen lunch",
                    ["evidence.destination.japan", "evidence.digest.pace"]),
                new(
                    "candidate.lunch.department-store",
                    "meal",
                    "Department store lunch hall",
                    ["evidence.digest.low-cognitive-load"])
            ]);

        ItineraryCandidatePoolFixture afternoonPool = new(
            "pool.day-2026-10-06.afternoon",
            "slot.day-2026-10-06.afternoon",
            [
                new(
                    "candidate.afternoon.garden",
                    "activity",
                    "Gentle garden walk",
                    ["evidence.destination.japan", "evidence.weather.buffer"]),
                new(
                    "candidate.afternoon.hotel-rest",
                    "downtime",
                    "Return to lodging buffer",
                    ["evidence.digest.low-cognitive-load"])
            ]);

        var day = new ItineraryDayFixture(
            "day-2026-10-06",
            "2026-10-06",
            "accepted",
            [
                new(
                    "slot.day-2026-10-06.morning",
                    "fixed_commitment",
                    "selected",
                    null,
                    "commitment.family-anchor"),
                new(
                    "slot.day-2026-10-06.lunch",
                    "meal",
                    "selected",
                    lunchPool.PoolId,
                    "candidate.lunch.ramen"),
                new(
                    "slot.day-2026-10-06.afternoon",
                    "activity",
                    "deferred",
                    afternoonPool.PoolId,
                    null)
            ]);

        return new(
            new(
                "itinerary.accepted",
                "applied",
                day.DayId,
                "selected",
                "deferred",
                "none",
                null,
                null),
            [day],
            [lunchPool, afternoonPool],
            [
                new("evidence.destination.japan", "memory-digest", "selected"),
                new("evidence.digest.pace", "memory-digest", "selected"),
                new("evidence.digest.low-cognitive-load", "memory-digest", "deferred"),
                new("evidence.weather.buffer", "fixture", "deferred")
            ],
            [
                new(
                    "itinerary.digest.1",
                    "destination_country: Japan",
                    "structured-digest",
                    "digest-session-7-day-synthetic-japan-7"),
                new(
                    "itinerary.digest.2",
                    "date_window: 2026-10-05/2026-10-19",
                    "structured-digest",
                    "digest-session-7-day-synthetic-japan-7")
            ]);
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
}

public sealed record ItineraryDayPlannerResult(
    ItineraryPlannerOutcomeFixture Outcome,
    IReadOnlyList<ItineraryDayFixture> Days,
    IReadOnlyList<ItineraryCandidatePoolFixture> CandidatePools,
    IReadOnlyList<ItineraryEvidenceFixture> Evidence,
    IReadOnlyList<MemoryDigestFactFixture> DigestFacts);
