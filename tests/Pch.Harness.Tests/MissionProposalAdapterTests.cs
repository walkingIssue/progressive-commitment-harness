using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class MissionProposalAdapterTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 5, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcceptedVacationMirrorAppliesThroughMissionIntake()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-vacation",
            [
                new("/mission/purpose", "Family vacation", "user", ["evidence-user-purpose"]),
                new("/mission/destination_country", "Japan", "user", ["evidence-user-country"])
            ],
            [
                new("constraint-hotel", "lodging preference", "quiet hotel", "user", false, ["evidence-user-hotel"])
            ],
            []));

        Assert.True(result.IsAccepted);
        Assert.Equal("mission_intake_applied", result.Code);
        Assert.Equal("Family vacation", session.Mission.Purpose);
        Assert.NotNull(session.MemoryDigest);
        Assert.Contains(result.Digest.LoadBearingFacts, fact => fact == "purpose: Family vacation");
    }

    [Fact]
    public void HighPriorityNonVacationCommitmentAnchorsPlanning()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-family-help",
            [
                new("/mission/purpose", "Help family move", "user", ["evidence-user-family"])
            ],
            [],
            [
                new(
                    "commitment-moving-day",
                    "administrative",
                    "Moving day",
                    FixedAt,
                    FixedAt.AddHours(10),
                    "Family apartment",
                    false,
                    false,
                    "high",
                    "strong_model_inference",
                    ["evidence-model-moving-day"])
            ]));

        Assert.True(result.IsAccepted);
        Assert.Contains(session.Mission.Commitments, commitment => commitment.CommitmentId == "commitment-moving-day");
        Assert.Contains(result.IntakeResult!.AppliedFacts, fact => fact.FieldPath == "/commitments/commitment-moving-day");
    }

    [Fact]
    public void ModelInferredFieldsAndConstraintsRemainPending()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-funeral",
            [
                new("/mission/purpose", "Funeral travel", "user", ["evidence-user-funeral"]),
                new("/mission/destination_country", "Japan", "strong_model_inference", ["evidence-model-country"])
            ],
            [
                new("constraint-pace", "pace", "very gentle", "strong_model_inference", true, ["evidence-model-pace"])
            ],
            []));

        Assert.True(result.IsAccepted);
        Assert.Equal("Funeral travel", session.Mission.Purpose);
        Assert.Contains(result.IntakeResult!.PendingConfirmations, pending => pending.FieldPath == "/mission/destination_country");
        Assert.Contains(result.IntakeResult.PendingConfirmations, pending => pending.FieldPath == "/constraints/constraint-pace");
    }

    [Fact]
    public void OverlongPayloadRejectsWithFixedCodeAndNoMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;
        var overlong = new string('x', 161);

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-overlong",
            [
                new("/mission/purpose", overlong, "user", ["evidence-user"])
            ],
            [],
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_field", result.Code);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(result.IntakeResult);
        Assert.Null(session.MemoryDigest);
        Assert.DoesNotContain(overlong, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFieldPathRejectsWithFixedCodeAndNoMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-unknown-field",
            [
                new("/mission/raw_prompt", "RAW_PROMPT_SHOULD_NOT_LEAK", "user", ["evidence-user"])
            ],
            [],
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("unsupported_field_path", result.Code);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedEnumMirrorRejectsWithFixedCodeAndNoMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-bad-enum",
            [],
            [],
            [
                new(
                    "commitment-bad",
                    "RAW_KIND_SHOULD_NOT_LEAK",
                    "Bad commitment",
                    FixedAt,
                    FixedAt.AddHours(1),
                    null,
                    false,
                    false,
                    "high",
                    "user",
                    ["evidence-bad"])
            ]));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_commitment", result.Code);
        Assert.DoesNotContain(session.Mission.Commitments, commitment => commitment.CommitmentId == "commitment-bad");
        Assert.DoesNotContain("RAW_KIND_SHOULD_NOT_LEAK", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AdapterFailureDoesNotMutateMissionDigestDecisionsTraceOrActions()
    {
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-bad-source",
            [
                new("/mission/purpose", "Should not apply", "RAW_SOURCE_SHOULD_NOT_LEAK", ["evidence-user"])
            ],
            [],
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_field", result.Code);
        Assert.NotEqual("Should not apply", session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain("RAW_SOURCE_SHOULD_NOT_LEAK", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void NullMirrorRejectsWithFixedCodeAndNoMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;

        var result = new MissionProposalAdapter().Apply(session, null!);

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_proposal", result.Code);
        Assert.Equal("Mission proposal failed validation.", result.Summary);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void NullCollectionsRejectWithFixedCodeAndNoMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var sentinel = "RAW_NULL_COLLECTION_SENTINEL_SHOULD_NOT_LEAK";

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-null-collections",
            null!,
            [],
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_proposal", result.Code);
        Assert.DoesNotContain(sentinel, result.Summary, StringComparison.Ordinal);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void NullEvidenceAndTextRejectWithoutExceptionOrRawEcho()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;
        var sentinel = "RAW_NULL_EVIDENCE_SENTINEL_SHOULD_NOT_LEAK";

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-null-evidence",
            [
                new("/mission/purpose", sentinel, "user", null!)
            ],
            [
                new("constraint-null", null!, "quiet", "user", true, null!)
            ],
            [
                new(
                    "commitment-null",
                    "activity",
                    null!,
                    FixedAt,
                    FixedAt.AddHours(1),
                    null,
                    false,
                    false,
                    "normal",
                    "user",
                    null!)
            ]));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_field", result.Code);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain(sentinel, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionIncludesBoundedMemoryDigestFactsAndPendingConfirmations()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-memory",
            [
                new("/mission/purpose", "Funeral travel", "user", ["evidence-user-funeral"]),
                new("/mission/destination_country", "Japan", "strong_model_inference", ["evidence-model-country"])
            ],
            [],
            []));

        var packet = new ProjectionService().Project(session, HarnessStage.Intake);

        Assert.True(result.IsAccepted);
        Assert.True(packet.LoadBearingFacts.Count <= 12);
        Assert.Contains(packet.LoadBearingFacts, fact => fact.StartsWith("memory: purpose: Funeral travel", StringComparison.Ordinal));
        Assert.Contains(packet.LoadBearingFacts, fact => fact.StartsWith("pending_confirmation: /mission/destination_country", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectionKeepsCoreCountersWhenMemoryDigestIsFull()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.SelectCandidates(new ChoiceSelection("logistics", ["candidate-01"], FixedAt));
        session.DeferSlot("dinner-day-2", "Waiting for family preference.");

        var result = new MissionProposalAdapter().Apply(session, new ProviderMissionProposalMirror(
            "planner-full-memory",
            [
                new("/mission/purpose", "Family travel", "user", ["evidence-user-purpose"]),
                new("/mission/destination_country", "Japan", "strong_model_inference", ["evidence-model-country"]),
                new("/mission/start_date", "2027-05-12", "strong_model_inference", ["evidence-model-start"]),
                new("/mission/end_date", "2027-05-18", "strong_model_inference", ["evidence-model-end"])
            ],
            [
                new("constraint-pace", "pace", "gentle", "strong_model_inference", true, ["evidence-model-pace"]),
                new("constraint-budget", "budget", "moderate", "strong_model_inference", false, ["evidence-model-budget"])
            ],
            []));

        var packet = new ProjectionService().Project(session, HarnessStage.Intake);

        Assert.True(result.IsAccepted);
        Assert.True(packet.LoadBearingFacts.Count <= 12);
        Assert.Contains("traveler_count: 1", packet.LoadBearingFacts);
        Assert.Contains("selected_candidate_count: 1", packet.LoadBearingFacts);
        Assert.Contains("deferred_slot_count: 1", packet.LoadBearingFacts);
        Assert.True(packet.LoadBearingFacts.Count(fact => fact.StartsWith("memory:", StringComparison.Ordinal)
            || fact.StartsWith("pending_confirmation:", StringComparison.Ordinal)) <= 4);
    }
}
