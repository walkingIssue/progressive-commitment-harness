using Pch.Core;

namespace Pch.Harness;

public sealed class HarnessActionIntake
{
    private readonly StageMachine _stageMachine;
    private readonly ProjectionService _projectionService;
    private readonly ApprovalGate _approvalGate;

    public HarnessActionIntake(
        StageMachine? stageMachine = null,
        ProjectionService? projectionService = null,
        ApprovalGate? approvalGate = null)
    {
        _stageMachine = stageMachine ?? new StageMachine();
        _projectionService = projectionService ?? new ProjectionService();
        _approvalGate = approvalGate ?? new ApprovalGate();
    }

    public SessionTurnResult Accept(TripSession session, HarnessAction action)
    {
        var packet = _projectionService.Project(session, session.Stage);
        if (!HarnessAction.KnownKinds.Contains(action.Kind))
        {
            return Block(session, packet, action.Kind, "unknown_action_kind", "Rejected unknown action kind.");
        }

        if (!packet.AllowedActions.Contains(action.Kind, StringComparer.Ordinal))
        {
            return Block(session, packet, action.Kind, "action_not_allowed_for_stage", "Rejected action kind for current stage.");
        }

        var validation = ValidateForStage(session, action);
        if (!validation.IsAccepted)
        {
            return Block(session, packet, action.Kind, validation.Code, validation.Summary);
        }

        var gate = _approvalGate.Evaluate(action);
        if (!gate.IsAllowed)
        {
            return Block(session, packet, action.Kind, "approval_required", "Rejected gated action without approval.");
        }

        Apply(session, action);
        var nextAction = _stageMachine.NextSkeletonAction(session);
        return SessionTurnResult.Continued(
            session.Stage,
            _projectionService.Project(session, session.Stage),
            nextAction,
            trace:
            [
                new(
                    $"trace-{session.Actions.Count}",
                    session.Stage.ToString(),
                    action.Kind,
                    "accepted",
                    "Accepted validated harness action.")
            ]);
    }

    private IntakeValidation ValidateForStage(TripSession session, HarnessAction action)
    {
        return action switch
        {
            EmitFormAction form => ValidatePendingForm(session, form),
            EmitChoiceSetAction choiceSet => choiceSet.MaxSelectable > 0 && choiceSet.Choices.Count > 0
                ? IntakeValidation.Accepted()
                : IntakeValidation.Rejected("invalid_choice_set", "Rejected invalid choice-set bounds."),
            ProposeSearchAction search => string.IsNullOrWhiteSpace(search.Query) || string.IsNullOrWhiteSpace(search.SearchSurface)
                ? IntakeValidation.Rejected("invalid_search", "Rejected search action with missing query or surface.")
                : IntakeValidation.Accepted(),
            SummarizeAction summarize => string.IsNullOrWhiteSpace(summarize.Audience)
                ? IntakeValidation.Rejected("invalid_summary", "Rejected summary action with missing audience.")
                : IntakeValidation.Accepted(),
            RequestApprovalAction approval => ValidatePendingApproval(session, approval),
            StatePatchAction patch => string.IsNullOrWhiteSpace(patch.Patch.Path)
                ? IntakeValidation.Rejected("invalid_state_patch", "Rejected state patch with missing path.")
                : IntakeValidation.Accepted(),
            DeferSlotAction defer => string.IsNullOrWhiteSpace(defer.SlotId) || string.IsNullOrWhiteSpace(defer.Reason)
                ? IntakeValidation.Rejected("invalid_defer", "Rejected defer action with missing slot or reason.")
                : IntakeValidation.Accepted(),
            HandoffAction handoff => string.IsNullOrWhiteSpace(handoff.Target) || string.IsNullOrWhiteSpace(handoff.Reason)
                ? IntakeValidation.Rejected("invalid_handoff", "Rejected handoff action with missing target or reason.")
                : IntakeValidation.Accepted(),
            _ => IntakeValidation.Rejected("unknown_action_shape", "Rejected unknown action shape.")
        };
    }

    private IntakeValidation ValidatePendingForm(TripSession session, EmitFormAction form)
    {
        if (_stageMachine.NextSkeletonAction(session) is not EmitFormAction pending)
        {
            return IntakeValidation.Rejected("no_pending_form", "Rejected form action without pending form.");
        }

        return string.Equals(form.Form.FormId, pending.Form.FormId, StringComparison.Ordinal)
            ? IntakeValidation.Accepted()
            : IntakeValidation.Rejected("form_id_mismatch", "Rejected form action that does not match pending form.");
    }

    private IntakeValidation ValidatePendingApproval(TripSession session, RequestApprovalAction approval)
    {
        if (session.Stage is not HarnessStage.ApprovalQueue
            || _stageMachine.NextSkeletonAction(session) is not RequestApprovalAction pending)
        {
            return IntakeValidation.Rejected("no_pending_approval", "Rejected approval action without pending approval.");
        }

        return string.Equals(approval.Approval.ApprovalId, pending.Approval.ApprovalId, StringComparison.Ordinal)
            ? IntakeValidation.Accepted()
            : IntakeValidation.Rejected("approval_id_mismatch", "Rejected approval action that does not match pending approval.");
    }

    private static void Apply(TripSession session, HarnessAction action)
    {
        session.RecordAction(action);
        switch (action)
        {
            case DeferSlotAction defer:
                session.DeferSlot(defer.SlotId, defer.Reason);
                break;
            case HandoffAction handoff:
                session.RecordHandoff(handoff.Target, handoff.Reason);
                break;
        }
    }

    private static SessionTurnResult Block(
        TripSession session,
        StagePacket packet,
        string kind,
        string code,
        string summary)
    {
        return SessionTurnResult.Blocked(
            session.Stage,
            packet,
            summary,
            [
                new(
                    $"trace-blocked-{code}",
                    session.Stage.ToString(),
                    kind,
                    code,
                    summary)
            ]);
    }
}

public sealed record IntakeValidation(bool IsAccepted, string Code, string Summary)
{
    public static IntakeValidation Accepted() => new(true, "accepted", "Accepted.");

    public static IntakeValidation Rejected(string code, string summary) => new(false, code, summary);
}
