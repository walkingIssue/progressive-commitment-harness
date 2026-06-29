using Pch.Core;

namespace Pch.Harness;

public sealed class SessionLoop
{
    private readonly StageMachine _stageMachine;
    private readonly ApprovalGate _approvalGate;
    private readonly ProjectionService _projectionService;

    public SessionLoop(
        StageMachine? stageMachine = null,
        ApprovalGate? approvalGate = null,
        ProjectionService? projectionService = null)
    {
        _stageMachine = stageMachine ?? new StageMachine();
        _approvalGate = approvalGate ?? new ApprovalGate();
        _projectionService = projectionService ?? new ProjectionService();
    }

    public SessionTurnResult SubmitForm(TripSession session, FormResponse response)
    {
        session.ApplyFormResponse(response);
        return Advance(session, new DecisionRecord(
            DecisionId: $"decision-form-{session.Actions.Count + 1}",
            Stage: session.Stage.ToString(),
            ActionKind: HarnessAction.EmitFormKind,
            Summary: $"Accepted form response {response.FormId}.",
            Source: AuthoritySource.User,
            RecordedAt: response.SubmittedAt));
    }

    public SessionTurnResult SelectCandidates(TripSession session, ChoiceSelection selection)
    {
        var selectionResult = session.SelectCandidates(selection);
        if (!selectionResult.IsAccepted)
        {
            var unknown = string.Join(", ", selectionResult.UnknownCandidateIds);
            return SessionTurnResult.Blocked(
                session.Stage,
                _projectionService.Project(session, session.Stage),
                $"Unknown candidate ID(s): {unknown}.");
        }

        return Advance(session, new DecisionRecord(
            DecisionId: $"decision-choice-{session.Actions.Count + 1}",
            Stage: session.Stage.ToString(),
            ActionKind: HarnessAction.EmitChoiceSetKind,
            Summary: $"Selected {session.SelectedCandidateIds.Count} candidate(s).",
            Source: AuthoritySource.User,
            RecordedAt: selection.SelectedAt));
    }

    public SessionTurnResult Approve(TripSession session, ApprovalToken token)
    {
        var pendingAction = _stageMachine.NextSkeletonAction(session);
        if (session.Stage is not HarnessStage.ApprovalQueue || pendingAction is not RequestApprovalAction pendingApproval)
        {
            return SessionTurnResult.Blocked(
                session.Stage,
                _projectionService.Project(session, session.Stage),
                "No pending approval request for the current stage.");
        }

        if (!string.Equals(token.ApprovalId, pendingApproval.Approval.ApprovalId, StringComparison.Ordinal))
        {
            return SessionTurnResult.Blocked(
                session.Stage,
                _projectionService.Project(session, session.Stage),
                "Approval token does not match the pending approval request.");
        }

        var gatedAction = pendingApproval with
        {
            Approval = pendingApproval.Approval with { ApprovalToken = token.Token }
        };

        var gate = _approvalGate.Evaluate(gatedAction);
        if (!gate.IsAllowed)
        {
            return SessionTurnResult.Blocked(session.Stage, _projectionService.Project(session, session.Stage), gate.RefusalReason ?? "Approval refused.");
        }

        session.RecordApproval(token);
        return Advance(session, new DecisionRecord(
            DecisionId: $"decision-approval-{session.Actions.Count + 1}",
            Stage: session.Stage.ToString(),
            ActionKind: HarnessAction.RequestApprovalKind,
            Summary: $"Accepted approval token for {token.ApprovalId}.",
            Source: AuthoritySource.User,
            RecordedAt: token.ApprovedAt));
    }

    public SessionTurnResult Defer(TripSession session, string slotId, string reason)
    {
        var action = new DeferSlotAction($"action-defer-{slotId}", slotId, reason);
        session.RecordAction(action);
        session.DeferSlot(slotId, reason);

        return SessionTurnResult.Continued(
            session.Stage,
            _projectionService.Project(session, session.Stage),
            _stageMachine.NextSkeletonAction(session));
    }

    public SessionTurnResult Handoff(TripSession session, string target, string reason)
    {
        var action = new HandoffAction($"action-handoff-{target}", target, reason);
        var gate = _approvalGate.Evaluate(action);
        if (!gate.IsAllowed)
        {
            return SessionTurnResult.Blocked(session.Stage, _projectionService.Project(session, session.Stage), gate.RefusalReason ?? "Handoff refused.");
        }

        session.RecordAction(action);
        session.RecordHandoff(target, reason);

        return SessionTurnResult.Continued(
            session.Stage,
            _projectionService.Project(session, session.Stage),
            _stageMachine.NextSkeletonAction(session));
    }

    private SessionTurnResult Advance(TripSession session, DecisionRecord decision)
    {
        var action = _stageMachine.NextSkeletonAction(session);
        session.RecordAction(action);
        session.RecordDecision(decision);
        session.MoveTo(_stageMachine.Next(session.Stage));

        return SessionTurnResult.Continued(
            session.Stage,
            _projectionService.Project(session, session.Stage),
            _stageMachine.NextSkeletonAction(session),
            decision);
    }
}

public sealed record SessionTurnResult(
    HarnessStage Stage,
    StagePacket Packet,
    HarnessAction? NextAction,
    DecisionRecord? Decision,
    bool IsBlocked,
    string? BlockedReason)
{
    public static SessionTurnResult Continued(
        HarnessStage stage,
        StagePacket packet,
        HarnessAction nextAction,
        DecisionRecord? decision = null)
    {
        return new(stage, packet, nextAction, decision, false, null);
    }

    public static SessionTurnResult Blocked(HarnessStage stage, StagePacket packet, string reason)
    {
        return new(stage, packet, null, null, true, reason);
    }
}
