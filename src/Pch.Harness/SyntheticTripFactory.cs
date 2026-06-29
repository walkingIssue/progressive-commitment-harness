using Pch.Core;

namespace Pch.Harness;

public static class SyntheticTripFactory
{
    private static readonly DateOnly StartDate = new(2027, 4, 1);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);

    public static TripSession CreateSession(int dayCount)
    {
        if (dayCount is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dayCount), "Synthetic trip day count must be between 1 and 31.");
        }

        var endDate = StartDate.AddDays(dayCount - 1);
        var mission = new TripMission(
            MissionId: $"synthetic-japan-{dayCount}",
            Purpose: dayCount == 1 ? "Focused Tokyo stopover" : $"Japan planning fixture for {dayCount} days",
            DestinationCountry: "Japan",
            StartDate: StartDate,
            EndDate: endDate,
            Travelers:
            [
                new("traveler-1", "Primary traveler", "ARN", ["reasonable walking load", "clear transit buffers"])
            ],
            Constraints:
            [
                new("constraint-budget", "budget posture", "moderate", AuthoritySource.User, false),
                new("constraint-pace", "pace", dayCount >= 7 ? "balanced" : "compact", AuthoritySource.User, true),
                new("constraint-evidence", "candidate evidence", "required", AuthoritySource.HarnessDefault, true)
            ],
            Commitments:
            [
                new(
                    "commitment-arrival",
                    CommitmentKind.Travel,
                    "Arrival window",
                    new DateTimeOffset(StartDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                    new DateTimeOffset(StartDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                    "Tokyo",
                    false,
                    false)
            ]);

        var session = new TripSession(
            $"session-{dayCount}-day",
            mission,
            evidenceTrace: new EvidenceTrace(
            [
                new("evidence-user-purpose", EvidenceKind.UserStatement, "User wants a Japan trip plan.", null, ObservedAt),
                new("evidence-country-pack", EvidenceKind.CountryPackAssumption, "Japan country pack requires transit buffers.", null, ObservedAt),
                new("evidence-fixture-candidates", EvidenceKind.CandidatePoolEvidence, "Synthetic candidates are fixture-only.", null, ObservedAt)
            ]),
            claimLedger: new ClaimLedger(
            [
                new("claim-purpose", "The trip is centered on Japan.", ["evidence-user-purpose"], true),
                new("claim-buffer", "Transit buffers should be preserved.", ["evidence-country-pack"], true)
            ]));

        session.AddCandidatePool(new CandidatePool(
            "pool-logistics",
            "Logistics",
            BuildCandidates(dayCount),
            ["constraint-budget", "constraint-pace"],
            ObservedAt));

        session.AddCandidatePool(new CandidatePool(
            "pool-all",
            "all",
            BuildCandidates(Math.Min(dayCount, 3)).Take(3).ToArray(),
            ["constraint-evidence"],
            ObservedAt));

        return session;
    }

    public static TripSession CreateBusinessTripSession()
    {
        var session = CreateSession(3);
        session.AddCandidatePool(new CandidatePool(
            "pool-business",
            "Logistics",
            [
                new(
                    "candidate-business-hotel",
                    CandidateKind.Hotel,
                    "Conference district hotel",
                    "Fixture hotel close to the meeting anchor.",
                    null,
                    null,
                    ["evidence-fixture-candidates"],
                    120)
            ],
            ["constraint-pace"],
            ObservedAt));
        return session;
    }

    public static TripSession CreateFuneralDowntimeSession()
    {
        var session = CreateSession(7);
        session.DeferSlot("meal-plan-day-2", "Family schedule is not stable yet.");
        session.AddCandidatePool(new CandidatePool(
            "pool-downtime",
            "ActivitiesDowntime",
            [
                new(
                    "candidate-quiet-garden",
                    CandidateKind.Activity,
                    "Quiet garden downtime",
                    "Low-friction downtime block near the fixed family anchor.",
                    null,
                    null,
                    ["evidence-fixture-candidates"],
                    130)
            ],
            ["constraint-pace"],
            ObservedAt));
        return session;
    }

    private static IReadOnlyList<Candidate> BuildCandidates(int dayCount)
    {
        var count = Math.Min(Math.Max(dayCount, 3), 12);
        return Enumerable.Range(1, count)
            .Select(index => new Candidate(
                CandidateId: $"candidate-{index:00}",
                Kind: index % 3 == 0 ? CandidateKind.Activity : CandidateKind.Transit,
                Title: $"Fixture option {index}",
                Summary: $"Bounded synthetic option {index} for projection tests.",
                EstimatedCost: null,
                Currency: null,
                EvidenceIds: ["evidence-fixture-candidates"],
                RelevanceScore: 100 - index))
            .ToArray();
    }
}
