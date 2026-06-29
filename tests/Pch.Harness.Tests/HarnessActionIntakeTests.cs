using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class HarnessActionIntakeTests
{
    [Fact]
    public void RejectsActionKindThatIsNotAllowedForCurrentStageWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Intake);

        var result = new HarnessActionIntake().Accept(
            session,
            new HandoffAction("handoff-1", "strong-model-auditor", "Review trace."));

        Assert.True(result.IsBlocked);
        Assert.Equal("Rejected action kind for current stage.", result.BlockedReason);
        Assert.Empty(session.Actions);
        Assert.Single(result.Trace);
        Assert.Equal("action_not_allowed_for_stage", result.Trace.Single().Outcome);
    }

    [Fact]
    public void RejectsFormActionThatDoesNotMatchPendingFormWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new HarnessActionIntake().Accept(
            session,
            new EmitFormAction(
                "form-action",
                new FormRequest("wrong-form", "Wrong", "Continue", [])));

        Assert.True(result.IsBlocked);
        Assert.Empty(session.Actions);
        Assert.Equal("form_id_mismatch", result.Trace.Single().Outcome);
    }

    [Fact]
    public void AcceptsPendingFormActionAndReturnsReplayableTrace()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var pending = Assert.IsType<EmitFormAction>(new StageMachine().NextSkeletonAction(session));

        var result = new HarnessActionIntake().Accept(session, pending);

        Assert.False(result.IsBlocked);
        Assert.Single(session.Actions);
        Assert.Single(result.Trace);
        Assert.Equal("accepted", result.Trace.Single().Outcome);
        Assert.Equal(HarnessStage.Intake, result.Stage);
    }

    [Fact]
    public void RejectsApprovalActionOutsideApprovalStageWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Logistics);

        var result = new HarnessActionIntake().Accept(
            session,
            new RequestApprovalAction(
                "approval-action",
                new ApprovalRequest("approval-review", "mock-booking", "Approve.", ["booking"], null, null, "token")));

        Assert.True(result.IsBlocked);
        Assert.Empty(session.Actions);
        Assert.Equal("action_not_allowed_for_stage", result.Trace.Single().Outcome);
    }

    [Fact]
    public void RejectsMismatchedApprovalActionWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);

        var result = new HarnessActionIntake().Accept(
            session,
            new RequestApprovalAction(
                "approval-action",
                new ApprovalRequest("other-approval", "mock-booking", "Approve.", ["booking"], null, null, "token")));

        Assert.True(result.IsBlocked);
        Assert.Empty(session.Actions);
        Assert.Equal("approval_id_mismatch", result.Trace.Single().Outcome);
    }

    [Fact]
    public void RejectsPendingApprovalWithoutTokenThroughGate()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);
        var pending = Assert.IsType<RequestApprovalAction>(new StageMachine().NextSkeletonAction(session));

        var result = new HarnessActionIntake().Accept(session, pending);

        Assert.True(result.IsBlocked);
        Assert.Empty(session.Actions);
        Assert.Equal("approval_required", result.Trace.Single().Outcome);
    }

    [Fact]
    public void AcceptsMatchingApprovalWithToken()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.ApprovalQueue);
        var pending = Assert.IsType<RequestApprovalAction>(new StageMachine().NextSkeletonAction(session));
        var withToken = pending with
        {
            Approval = pending.Approval with { ApprovalToken = "approved-token" }
        };

        var result = new HarnessActionIntake().Accept(session, withToken);

        Assert.False(result.IsBlocked);
        Assert.Single(session.Actions);
        Assert.Equal("accepted", result.Trace.Single().Outcome);
    }

    [Fact]
    public void RejectsBookingHandoffWithoutApprovalGate()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.EvidencePacket);

        var result = new HarnessActionIntake().Accept(
            session,
            new HandoffAction("handoff-booking", "booking-adapter", "booking spend handoff"));

        Assert.True(result.IsBlocked);
        Assert.Empty(session.Handoffs);
        Assert.Equal("approval_required", result.Trace.Single().Outcome);
    }

    [Fact]
    public void AcceptsStageAppropriateDeferAndRecordsSlot()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Meals);

        var result = new HarnessActionIntake().Accept(
            session,
            new DeferSlotAction("defer-dinner", "dinner-day-2", "Need user preference."));

        Assert.False(result.IsBlocked);
        Assert.Single(session.DeferredSlots);
        Assert.Single(session.Actions);
    }
}
