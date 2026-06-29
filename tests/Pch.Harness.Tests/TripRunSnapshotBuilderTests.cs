using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class TripRunSnapshotBuilderTests
{
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CompleteRunSnapshotComposesTrustedState()
    {
        var session = CompleteSession();

        var result = new TripRunSnapshotBuilder().Build(session);

        Assert.True(result.IsComplete);
        Assert.False(result.IsBlocked);
        Assert.Equal(TripRunSnapshotBuilder.CompleteCode, result.Code);
        Assert.Equal("Trip run snapshot complete.", result.Summary);
        Assert.Equal(session.SessionId, result.Snapshot.SessionId);
        Assert.True(result.Snapshot.Itinerary.IsCompiled);
        var decision = Assert.Single(result.Snapshot.ItineraryDecisions);
        Assert.Equal("candidate-03", decision.CandidateId);
        Assert.Contains("evidence-fixture-candidates", result.Snapshot.EvidenceReferences);
        Assert.True(result.Snapshot.MockHold.IsReadyForMockHold);
        Assert.False(result.Snapshot.MockHold.RequiresApproval);
    }

    [Fact]
    public void PendingConfirmationSnapshotUsesFixedCode()
    {
        var session = CompleteSession();
        session.ReplaceMemoryDigest(new StructuredMemoryDigest(
            "digest-pending",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Vacation"],
            [
                new(
                    "/mission/destination_country",
                    "Japan",
                    AuthoritySource.StrongModelInference,
                    "requires_confirmation",
                    ["evidence-pending"])
            ],
            ["evidence-pending"]));

        var result = new TripRunSnapshotBuilder().Build(session);

        Assert.False(result.IsComplete);
        Assert.False(result.IsBlocked);
        Assert.Equal(TripRunSnapshotBuilder.PendingConfirmationCode, result.Code);
        Assert.Equal("Trip run snapshot has pending confirmations.", result.Summary);
        Assert.Equal(1, result.Snapshot.Memory.PendingConfirmationCount);
        Assert.Single(result.Snapshot.Memory.PendingConfirmations);
    }

    [Fact]
    public void BlockedCompilerSnapshotUsesFixedCode()
    {
        var session = SyntheticTripFactory.CreateSession(1);

        var result = new TripRunSnapshotBuilder().Build(session);

        Assert.False(result.IsComplete);
        Assert.True(result.IsBlocked);
        Assert.Equal(TripRunSnapshotBuilder.BlockedCompilerCode, result.Code);
        Assert.Equal("Trip run snapshot blocked by itinerary compiler state.", result.Summary);
        Assert.False(result.Snapshot.Itinerary.IsCompiled);
        Assert.Equal("missing", result.Snapshot.Itinerary.Code);
    }

    [Fact]
    public void BlockedCandidateSnapshotRequiresSelectedItineraryDecision()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        Compile(session);

        var result = new TripRunSnapshotBuilder().Build(session);

        Assert.False(result.IsComplete);
        Assert.True(result.IsBlocked);
        Assert.Equal(TripRunSnapshotBuilder.BlockedCandidateCode, result.Code);
        Assert.Equal("Trip run snapshot requires at least one selected itinerary candidate.", result.Summary);
        Assert.Empty(result.Snapshot.ItineraryDecisions);
        Assert.Equal("not_ready", result.Snapshot.MockHold.Code);
    }

    [Fact]
    public void HoldPrepRequiredSnapshotDoesNotExposeApprovalToken()
    {
        var session = SelectedSession();

        var result = new TripRunSnapshotBuilder().Build(session);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsComplete);
        Assert.True(result.IsBlocked);
        Assert.Equal(TripRunSnapshotBuilder.HoldPrepRequiredCode, result.Code);
        Assert.Equal("Trip run snapshot requires approval before mock hold preparation.", result.Summary);
        Assert.Equal("approval_required", result.Snapshot.MockHold.Code);
        Assert.True(result.Snapshot.MockHold.RequiresApproval);
        Assert.DoesNotContain("approved-token", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotBoundsEvidenceTraceMemoryAndDecisions()
    {
        var session = CompleteSession();
        session.ReplaceMemoryDigest(new StructuredMemoryDigest(
            "digest-bounds",
            session.SessionId,
            session.Mission.MissionId,
            Enumerable.Range(1, 10).Select(index => $"fact {index}").ToArray(),
            Enumerable.Range(1, 8)
                .Select(index => new MissionPendingConfirmation(
                    $"/mission/pending-{index}",
                    $"value {index}",
                    AuthoritySource.StrongModelInference,
                    "requires_confirmation",
                    [$"evidence-pending-{index}"]))
                .ToArray(),
            Enumerable.Range(1, 20).Select(index => $"evidence-trace-{index}").ToArray()));
        AddExtraDecisions(session, 20);

        var result = new TripRunSnapshotBuilder().Build(session);

        Assert.True(result.Snapshot.Mission.Facts.Count <= 8);
        Assert.True(result.Snapshot.Memory.Facts.Count <= 6);
        Assert.True(result.Snapshot.Memory.PendingConfirmations.Count <= 6);
        Assert.True(result.Snapshot.ItineraryDecisions.Count <= 12);
        Assert.True(result.Snapshot.EvidenceReferences.Count <= 12);
        Assert.True(result.Snapshot.TraceReferences.Count <= 12);
    }

    [Fact]
    public void BuilderDoesNotMutateSessionState()
    {
        var session = CompleteSession();
        var decisionCount = session.DecisionLedger.Records.Count;
        var itineraryDecisionCount = session.ItineraryDecisions.Count;
        var approvalCount = session.ApprovalTokens.Count;
        var actionCount = session.Actions.Count;

        _ = new TripRunSnapshotBuilder().Build(session);

        Assert.Equal(decisionCount, session.DecisionLedger.Records.Count);
        Assert.Equal(itineraryDecisionCount, session.ItineraryDecisions.Count);
        Assert.Equal(approvalCount, session.ApprovalTokens.Count);
        Assert.Equal(actionCount, session.Actions.Count);
    }

    [Fact]
    public void SerializedSnapshotOmitsRawPromptProviderPayloadApprovalTokenHoldReferenceAndCandidateText()
    {
        const string sentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
        var session = SelectedSession(
            missionPurpose: "RAW_PROMPT_SHOULD_NOT_LEAK",
            candidateTitle: sentinel,
            candidateSummary: sentinel,
            candidateEvidence: "evidence-safe");
        session.RecordApproval(new ApprovalToken("approval-review", "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", FixedAt));
        session.ReplaceMemoryDigest(new StructuredMemoryDigest(
            "digest-safe",
            session.SessionId,
            session.Mission.MissionId,
            ["RAW_PROMPT_SHOULD_NOT_LEAK", "purpose: [redacted]"],
            [
                new(
                    "/mission/raw_prompt",
                    "RAW_PROMPT_SHOULD_NOT_LEAK",
                    AuthoritySource.SmallModelDraft,
                    "requires_confirmation",
                    ["RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", "evidence-safe"])
            ],
            ["RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", "evidence-safe"]));

        var result = new TripRunSnapshotBuilder().Build(session);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("approved-token", serialized, StringComparison.Ordinal);
    }

    private static TripSession CompleteSession()
    {
        var session = SelectedSession();
        session.RecordApproval(new ApprovalToken("approval-review", "approved-token", FixedAt));
        return session;
    }

    private static TripSession SelectedSession(
        string missionPurpose = "Focused Tokyo stopover",
        string candidateTitle = "Fixture option 3",
        string candidateSummary = "Bounded synthetic option 3 for projection tests.",
        string candidateEvidence = "evidence-fixture-candidates")
    {
        var session = SyntheticTripFactory.CreateSession(1);
        if (!string.Equals(session.Mission.Purpose, missionPurpose, StringComparison.Ordinal))
        {
            session.ReplaceMission(session.Mission with { Purpose = missionPurpose });
        }

        Compile(session);
        var slot = session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Activity);
        session.AddItineraryCandidatePool(slot.SlotId, new CandidatePool(
            "pool-snapshot-activity",
            "all",
            [
                new(
                    "candidate-03",
                    CandidateKind.Activity,
                    candidateTitle,
                    candidateSummary,
                    null,
                    null,
                    [candidateEvidence],
                    120)
            ],
            [],
            ObservedAt));

        var applied = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-03",
            CandidateKind.Activity,
            FixedAt));
        Assert.True(applied.IsAccepted);
        return session;
    }

    private static void Compile(TripSession session)
    {
        var compiled = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            null,
            []));
        Assert.True(compiled.IsCompiled);
    }

    private static void AddExtraDecisions(TripSession session, int count)
    {
        for (var index = 0; index < count; index++)
        {
            session.RecordItineraryDecision(new ItinerarySlotDecision(
                $"extra-decision-{index}",
                $"extra-slot-{index}",
                ItinerarySlotDecisionKind.Deferred,
                ItinerarySlotKind.Meal,
                null,
                null,
                [$"evidence-extra-{index}"]));
            session.RecordDecision(new DecisionRecord(
                $"extra-decision-record-{index}",
                HarnessStage.DaySkeletonGeneration.ToString(),
                "itinerary_defer",
                "Accepted itinerary slot defer decision.",
                AuthoritySource.User,
                FixedAt));
        }
    }
}
