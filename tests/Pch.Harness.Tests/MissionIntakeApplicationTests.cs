using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class MissionIntakeApplicationTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 2, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VacationProposalAppliesUserStatedFactsAndLeavesModelInferencePending()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-vacation",
            [
                new("/mission/purpose", "Cherry blossom vacation", AuthoritySource.User, ["evidence-user-purpose"]),
                new("/mission/destination_country", "Japan", AuthoritySource.StrongModelInference, ["evidence-model-country"])
            ],
            [
                new("constraint-quiet", "lodging preference", "quiet hotel", AuthoritySource.User, false, ["evidence-user-lodging"])
            ],
            []));

        Assert.True(result.IsApplied);
        Assert.Equal("Cherry blossom vacation", session.Mission.Purpose);
        Assert.Equal("Japan", session.Mission.DestinationCountry);
        Assert.Contains(result.AppliedFacts, fact => fact.FieldPath == "/mission/purpose");
        Assert.Contains(result.PendingConfirmations, pending => pending.FieldPath == "/mission/destination_country");
        Assert.Contains(result.Digest.LoadBearingFacts, fact => fact == "purpose: Cherry blossom vacation");
        Assert.Contains("evidence-user-purpose", result.Digest.TraceReferences);
    }

    [Fact]
    public void BusinessProposalAddsHardUserConstraintAndHighPriorityCommitment()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var result = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-business",
            [
                new("/mission/purpose", "Business trip with client workshop", AuthoritySource.User, ["evidence-user-business"])
            ],
            [
                new("constraint-workshop", "workshop arrival", "arrive before 09:00", AuthoritySource.User, true, ["evidence-user-workshop"])
            ],
            [
                new(
                    "commitment-client-workshop",
                    CommitmentKind.FixedAnchor,
                    "Client workshop",
                    FixedAt,
                    FixedAt.AddHours(3),
                    "Tokyo office",
                    false,
                    false,
                    CommitmentPriority.High,
                    AuthoritySource.StrongModelInference,
                    ["evidence-model-workshop"])
            ]));

        Assert.Equal("Business trip with client workshop", session.Mission.Purpose);
        Assert.Contains(session.Mission.Constraints, constraint => constraint.ConstraintId == "constraint-workshop" && constraint.IsHard);
        Assert.Contains(session.Mission.Commitments, commitment => commitment.CommitmentId == "commitment-client-workshop");
        Assert.Contains(result.AppliedFacts, fact => fact.FieldPath == "/commitments/commitment-client-workshop");
        Assert.DoesNotContain(result.PendingConfirmations, pending => pending.FieldPath == "/commitments/commitment-client-workshop");
    }

    [Fact]
    public void FuneralDowntimeProposalKeepsModelInferredPacePendingButAppliesFamilyAnchor()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-funeral",
            [
                new("/mission/purpose", "Funeral travel with quiet downtime", AuthoritySource.User, ["evidence-user-funeral"])
            ],
            [
                new("constraint-pace-inferred", "pace", "very gentle", AuthoritySource.StrongModelInference, true, ["evidence-model-pace"])
            ],
            [
                new(
                    "commitment-funeral-service",
                    CommitmentKind.FixedAnchor,
                    "Funeral service",
                    FixedAt,
                    FixedAt.AddHours(2),
                    "Family chapel",
                    true,
                    false,
                    CommitmentPriority.High,
                    AuthoritySource.User,
                    ["evidence-user-service"])
            ]));

        Assert.Contains(session.Mission.Commitments, commitment => commitment.CommitmentId == "commitment-funeral-service");
        Assert.DoesNotContain(session.Mission.Constraints, constraint => constraint.ConstraintId == "constraint-pace-inferred");
        Assert.Contains(result.PendingConfirmations, pending => pending.FieldPath == "/constraints/constraint-pace-inferred");
        Assert.Contains(result.Digest.LoadBearingFacts, fact => fact == "commitment: Funeral service");
    }

    [Fact]
    public void HelpingFamilyProposalBuildsBoundedDigestWithTraceReferences()
    {
        var session = SyntheticTripFactory.CreateSession(14);
        var proposal = new MissionIntakeProposal(
            "proposal-family",
            [
                new("/mission/purpose", "Help family move apartment", AuthoritySource.User, ["evidence-user-family"]),
                new("/mission/start_date", "2027-05-10", AuthoritySource.User, ["evidence-user-dates"]),
                new("/mission/end_date", "2027-05-17", AuthoritySource.User, ["evidence-user-dates"]),
                new("/mission/unsupported", "raw unsupported value", AuthoritySource.User, ["evidence-user-unsupported"])
            ],
            Enumerable.Range(1, 10)
                .Select(index => new ConstraintProposal(
                    $"constraint-family-{index}",
                    $"family constraint {index}",
                    $"value {index}",
                    AuthoritySource.User,
                    index % 2 == 0,
                    [$"evidence-constraint-{index}"]))
                .ToArray(),
            [
                new(
                    "commitment-moving-day",
                    CommitmentKind.Administrative,
                    "Moving day",
                    new DateTimeOffset(2027, 5, 12, 8, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 5, 12, 18, 0, 0, TimeSpan.Zero),
                    "Family apartment",
                    false,
                    false,
                    CommitmentPriority.High,
                    AuthoritySource.User,
                    ["evidence-user-moving-day"])
            ]);

        var result = new MissionIntakeApplication().Apply(session, proposal);

        Assert.Equal("Help family move apartment", session.Mission.Purpose);
        Assert.Equal(new DateOnly(2027, 5, 10), session.Mission.StartDate);
        Assert.Equal(new DateOnly(2027, 5, 17), session.Mission.EndDate);
        Assert.Contains(result.PendingConfirmations, pending => pending.FieldPath == "/mission/unsupported");
        Assert.True(result.Digest.LoadBearingFacts.Count <= 8);
        Assert.True(result.Digest.TraceReferences.Count <= 8);
        Assert.Contains("evidence-user-family", result.Digest.TraceReferences);
        Assert.DoesNotContain(result.Digest.LoadBearingFacts, fact => fact.Contains("raw unsupported value", StringComparison.Ordinal));
    }
}
