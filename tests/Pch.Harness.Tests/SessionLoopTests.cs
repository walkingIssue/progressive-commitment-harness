using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class SessionLoopTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FormResponseAdvancesStageAndPersistsFlatValues()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var result = new SessionLoop().SubmitForm(
            session,
            new FormResponse(
                "mission-intake",
                new Dictionary<string, string?> { ["purpose"] = "family travel", ["budget"] = "moderate" },
                FixedNow));

        Assert.Equal(HarnessStage.SlotCollection, session.Stage);
        Assert.Equal("family travel", session.FormValues["mission-intake.purpose"]);
        Assert.Equal(HarnessStage.SlotCollection, result.Stage);
        Assert.IsType<EmitFormAction>(result.NextAction);
        Assert.Single(session.DecisionLedger.Records);
    }

    [Fact]
    public void CandidateSelectionPersistsKnownCandidateIds()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Logistics);

        var result = new SessionLoop().SelectCandidates(
            session,
            new ChoiceSelection("logistics", ["candidate-02"], FixedNow));

        Assert.Equal(["candidate-02"], session.SelectedCandidateIds);
        Assert.Equal(HarnessStage.Meals, result.Stage);
        Assert.Contains(result.Packet.LoadBearingFacts, fact => fact == "selected_candidate_count: 1");
    }

    [Fact]
    public void UnknownCandidateSelectionBlocksWithoutPartialSelection()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Logistics);

        var result = new SessionLoop().SelectCandidates(
            session,
            new ChoiceSelection("logistics", ["candidate-02", "missing-candidate"], FixedNow));

        Assert.True(result.IsBlocked);
        Assert.Contains("missing-candidate", result.BlockedReason, StringComparison.Ordinal);
        Assert.Empty(session.SelectedCandidateIds);
        Assert.Equal(HarnessStage.Logistics, session.Stage);
    }

    [Fact]
    public void ApprovalTokenAllowsApprovalQueueToAdvance()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);

        var result = new SessionLoop().Approve(session, new ApprovalToken("approval-review", "approved-token", FixedNow));

        Assert.False(result.IsBlocked);
        Assert.True(session.HasApprovalToken("approval-review"));
        Assert.Equal(HarnessStage.MockedBooking, session.Stage);
    }

    [Fact]
    public void BlankApprovalTokenBlocksAndIsNotRecorded()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);

        var result = new SessionLoop().Approve(session, new ApprovalToken("approval-review", "   ", FixedNow));

        Assert.True(result.IsBlocked);
        Assert.False(session.HasApprovalToken("approval-review"));
        Assert.Empty(session.ApprovalTokens);
        Assert.Equal(HarnessStage.ApprovalQueue, session.Stage);
    }

    [Fact]
    public void DeferStaysInCurrentStageAndUpdatesProjection()
    {
        var session = SyntheticTripFactory.CreateSession(14);
        session.MoveTo(HarnessStage.Meals);

        var result = new SessionLoop().Defer(session, "dinner-day-4", "Waiting for family preference.");

        Assert.Equal(HarnessStage.Meals, session.Stage);
        Assert.Single(session.DeferredSlots);
        Assert.Contains(result.Packet.LoadBearingFacts, fact => fact == "deferred_slot_count: 1");
        Assert.IsType<EmitChoiceSetAction>(result.NextAction);
    }

    [Fact]
    public void SafeHandoffIsRecordedWithoutAdvancingStage()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        session.MoveTo(HarnessStage.EvidencePacket);

        var result = new SessionLoop().Handoff(session, "strong-model-auditor", "Review evidence trace.");

        Assert.False(result.IsBlocked);
        Assert.Single(session.Handoffs);
        Assert.Equal(HarnessStage.EvidencePacket, result.Stage);
    }

    [Fact]
    public void BookingHandoffWithoutApprovalIsBlocked()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        session.MoveTo(HarnessStage.MockedBooking);

        var result = new SessionLoop().Handoff(session, "booking-adapter", "booking spend handoff");

        Assert.True(result.IsBlocked);
        Assert.Contains("approval token", result.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Handoffs);
    }
}
