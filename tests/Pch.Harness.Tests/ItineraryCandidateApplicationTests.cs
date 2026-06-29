using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class ItineraryCandidateApplicationTests
{
    private static readonly DateTimeOffset FixedNow = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AcceptedSelectionRecordsDecisionEvidenceAndTrace()
    {
        var session = CompiledSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-03",
            CandidateKind.Activity,
            FixedNow));

        Assert.True(result.IsAccepted);
        Assert.Equal("itinerary_decision_applied", result.Code);
        Assert.Single(session.ItineraryDecisions);
        Assert.Single(session.DecisionLedger.Records);
        Assert.Equal("candidate-03", result.Decision!.CandidateId);
        Assert.Contains("evidence-fixture-candidates", result.EvidenceIds);
        Assert.Single(result.Trace);
        Assert.Equal(1, result.SelectedCount);
        Assert.Equal(0, result.DeferredCount);
    }

    [Fact]
    public void AcceptedDeferRecordsDeferredSlotDecision()
    {
        var session = CompiledSession();
        var slot = Slot(session, ItinerarySlotKind.Meal);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Deferred,
            slot.Kind,
            null,
            null,
            FixedNow));

        Assert.True(result.IsAccepted);
        Assert.Equal("itinerary_decision_applied", result.Code);
        Assert.Single(session.ItineraryDecisions);
        Assert.Equal(ItinerarySlotDecisionKind.Deferred, session.ItineraryDecisions[0].Kind);
        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(1, result.DeferredCount);
    }

    [Fact]
    public void UnknownSlotBlocksWithoutMutation()
    {
        var session = CompiledSession();

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            "missing-slot",
            ItinerarySlotDecisionKind.Selected,
            ItinerarySlotKind.Activity,
            "candidate-03",
            CandidateKind.Activity,
            FixedNow));

        AssertBlocked(result, "unknown_slot", session);
    }

    [Fact]
    public void UnknownCandidateBlocksWithoutMutation()
    {
        var session = CompiledSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "missing-candidate",
            CandidateKind.Activity,
            FixedNow));

        AssertBlocked(result, "unknown_candidate", session);
    }

    [Fact]
    public void CandidateSlotMismatchBlocksWithoutMutation()
    {
        var session = CompiledSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-01",
            CandidateKind.Transit,
            FixedNow));

        AssertBlocked(result, "candidate_slot_mismatch", session);
    }

    [Fact]
    public void CategoryMismatchBlocksWithoutMutation()
    {
        var session = CompiledSession();
        var slot = Slot(session, ItinerarySlotKind.Activity);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            ItinerarySlotKind.Meal,
            "candidate-03",
            CandidateKind.Activity,
            FixedNow));

        AssertBlocked(result, "category_mismatch", session);
    }

    [Fact]
    public void MalformedInputBlocksWithoutMutation()
    {
        var session = CompiledSession();

        var result = new ItineraryCandidateApplication().Apply(session, null!);

        AssertBlocked(result, "invalid_request", session);
    }

    [Fact]
    public void ProjectionIncludesSelectedAndDeferredItineraryCounts()
    {
        var session = CompiledSession();
        var activity = Slot(session, ItinerarySlotKind.Activity);
        var meal = Slot(session, ItinerarySlotKind.Meal);
        var application = new ItineraryCandidateApplication();

        application.Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            activity.SlotId,
            ItinerarySlotDecisionKind.Selected,
            activity.Kind,
            "candidate-03",
            CandidateKind.Activity,
            FixedNow));
        application.Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            meal.SlotId,
            ItinerarySlotDecisionKind.Deferred,
            meal.Kind,
            null,
            null,
            FixedNow));

        var packet = new ProjectionService().Project(session, HarnessStage.DaySkeletonGeneration);

        Assert.Contains("selected_itinerary_count: 1", packet.LoadBearingFacts);
        Assert.Contains("deferred_itinerary_count: 1", packet.LoadBearingFacts);
    }

    [Fact]
    public void SerializedResultDoesNotContainRawCandidateSentinel()
    {
        const string sentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
        var session = CompiledSession();
        session.AddCandidatePool(new CandidatePool(
            "pool-sentinel",
            "all",
            [
                new(
                    "candidate-safe-id",
                    CandidateKind.Activity,
                    sentinel,
                    sentinel,
                    null,
                    null,
                    ["evidence-fixture-candidates"],
                    140)
            ],
            [],
            ObservedAt));
        var slot = Slot(session, ItinerarySlotKind.Activity);

        var result = new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            "candidate-safe-id",
            CandidateKind.Activity,
            FixedNow));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsAccepted);
        Assert.DoesNotContain(sentinel, serialized, StringComparison.Ordinal);
    }

    private static TripSession CompiledSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var compile = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            null,
            []));
        Assert.True(compile.IsCompiled);
        return session;
    }

    private static ItinerarySlot Slot(TripSession session, ItinerarySlotKind kind)
    {
        return session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == kind);
    }

    private static void AssertBlocked(
        ItinerarySlotApplicationResult result,
        string expectedCode,
        TripSession session)
    {
        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Decision);
        Assert.Empty(result.EvidenceIds);
        Assert.Single(result.Trace);
        Assert.Empty(session.ItineraryDecisions);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Equal(0, result.SelectedCount);
        Assert.Equal(0, result.DeferredCount);
    }
}
