using Pch.Core;
using Pch.Harness;

namespace Pch.UI.Features.StageCockpit;

public sealed class HarnessStageCockpitService
{
    private readonly SessionLoop _loop = new();
    private readonly ProjectionService _projection = new();
    private readonly StageMachine _stageMachine = new();
    private readonly TripSession _session;
    private readonly List<SessionResponseFixture> _responses = [];
    private SessionTurnResult? _lastTurn;

    public HarnessStageCockpitService()
    {
        _session = SyntheticTripFactory.CreateSession(7);
        _session.MoveTo(HarnessStage.SlotCollection);
        _responses.Add(new(
            "response.pending.form",
            SessionResponseState.Pending,
            "Pending",
            "Slot collection form is waiting for user apply.",
            "slot-collection",
            null));
    }

    public StageCockpitFixture Current()
    {
        var packet = _lastTurn?.Packet ?? _projection.Project(_session, _session.Stage);
        var nextAction = _lastTurn?.NextAction ?? _stageMachine.NextSkeletonAction(_session);
        return BuildFixture(packet, nextAction);
    }

    public StageCockpitFixture ApplyForm(IReadOnlyDictionary<string, string> values)
    {
        if (_stageMachine.NextSkeletonAction(_session) is not EmitFormAction pendingForm)
        {
            var blocked = SessionTurnResult.Blocked(
                _session.Stage,
                _projection.Project(_session, _session.Stage),
                $"Cannot apply form while pending harness action is {_stageMachine.NextSkeletonAction(_session).Kind}.");
            _lastTurn = blocked;
            UpsertResponse(new(
                $"response.blocked.form.{_responses.Count + 1}",
                SessionResponseState.Blocked,
                "Blocked",
                blocked.BlockedReason ?? "Form apply was blocked.",
                _session.Stage.ToString(),
                CurrentApprovalId()));

            return Current();
        }

        var form = pendingForm.Form;
        var response = new FormResponse(
            form.FormId,
            form.Fields.ToDictionary(
                field => field.FieldId,
                field => values.TryGetValue(field.FieldId, out var value) ? (string?)value : field.CurrentValue,
                StringComparer.Ordinal),
            DateTimeOffset.UtcNow);

        _lastTurn = _loop.SubmitForm(_session, response);
        UpsertResponse(_lastTurn.IsBlocked
            ? new(
                $"response.blocked.form.{_responses.Count + 1}",
                SessionResponseState.Blocked,
                "Blocked",
                _lastTurn.BlockedReason ?? "Form apply was blocked.",
                response.FormId,
                null)
            : new(
                $"response.applied.form.{_session.Actions.Count}",
                SessionResponseState.Applied,
                "Applied",
                $"Harness accepted form response {response.FormId}.",
                response.FormId,
                null));

        return Current();
    }

    public StageCockpitFixture SelectCandidate(string candidateId)
    {
        _lastTurn = _loop.SelectCandidates(_session, new ChoiceSelection(
            "harness-choice-set",
            [candidateId],
            DateTimeOffset.UtcNow));

        UpsertResponse(_lastTurn.IsBlocked
            ? new(
                $"response.blocked.choice.{_responses.Count + 1}",
                SessionResponseState.Blocked,
                "Blocked",
                _lastTurn.BlockedReason ?? "Candidate selection was blocked.",
                candidateId,
                null)
            : new(
                $"response.applied.choice.{_session.SelectedCandidateIds.Count}",
                SessionResponseState.Applied,
                "Applied",
                $"Harness selected candidate {candidateId}.",
                candidateId,
                null));

        return Current();
    }

    public StageCockpitFixture TriggerBlockedSelection() => SelectCandidate("candidate-missing-from-harness");

    public StageCockpitFixture RequestApprovalStage()
    {
        _session.MoveTo(HarnessStage.ApprovalQueue);
        _lastTurn = SessionTurnResult.Continued(
            _session.Stage,
            _projection.Project(_session, _session.Stage),
            _stageMachine.NextSkeletonAction(_session));

        var approvalId = CurrentApprovalId();
        UpsertResponse(new(
            "response.approval.required",
            SessionResponseState.ApprovalRequired,
            "Approval required",
            "Harness is waiting for an approval token before adapter handoff.",
            approvalId,
            approvalId));

        return Current();
    }

    public StageCockpitFixture ApproveCurrentRequest()
    {
        var approvalId = CurrentApprovalId();
        _lastTurn = _loop.Approve(_session, new ApprovalToken(
            approvalId,
            $"ui-approval-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            DateTimeOffset.UtcNow));

        UpsertResponse(_lastTurn.IsBlocked
            ? new(
                "response.blocked.approval",
                SessionResponseState.Blocked,
                "Blocked",
                _lastTurn.BlockedReason ?? "Approval was blocked.",
                approvalId,
                approvalId)
            : new(
                "response.applied.approval",
                SessionResponseState.Applied,
                "Applied",
                $"Harness accepted approval token for {approvalId}.",
                approvalId,
                approvalId));

        return Current();
    }

    private StageCockpitFixture BuildFixture(StagePacket packet, HarnessAction? nextAction)
    {
        var formAction = nextAction as EmitFormAction ?? ResolveFormAction();
        var choiceAction = nextAction as EmitChoiceSetAction;
        var approvalAction = nextAction as RequestApprovalAction;
        var candidates = packet.Candidates.Count > 0 ? packet.Candidates : choiceAction?.Choices ?? [];
        var approvalId = approvalAction?.Approval.ApprovalId ?? CurrentApprovalId();

        return new(
            Packet: new(
                Id: packet.PacketId,
                Name: packet.Stage,
                Summary: packet.CurrentSubtask,
                State: _lastTurn?.IsBlocked == true ? "Blocked" : $"Harness stage {packet.Stage}",
                Source: "Pch.Harness session service",
                RequiredSlotCount: formAction.Form.Fields.Count(field => field.Required),
                CompletedSlotCount: formAction.Form.Fields.Count(field => field.Required && !string.IsNullOrWhiteSpace(field.CurrentValue)),
                AllowedOutputs: packet.AllowedActions,
                Authority: string.Join(" ", packet.AuthorityHints)),
            GeneratedForm: new(
                formAction.Form.Title,
                formAction.Form.Fields.Select(field => new GeneratedFieldFixture(
                    field.FieldId,
                    field.Label,
                    field.FieldType == "textarea" ? "text" : field.FieldType,
                    field.CurrentValue ?? "",
                    field.Required,
                    field.Options.Select(option => new FieldOptionFixture(option, option)).ToArray())).ToArray()),
            ChoiceSet: new(
                choiceAction?.ActionId ?? "harness-candidates",
                choiceAction?.Title ?? "Harness Candidates",
                _session.SelectedCandidateIds.LastOrDefault() ?? candidates.FirstOrDefault()?.CandidateId ?? "",
                candidates.Select(candidate => new ChoiceCandidateFixture(
                    candidate.CandidateId,
                    candidate.Title,
                    candidate.Summary,
                    $"{candidate.Kind}; evidence: {string.Join(", ", candidate.EvidenceIds)}")).ToArray()),
            Approval: new(
                approvalId,
                "Approval Gate",
                approvalAction?.Approval.Prompt ?? "Approval token required before adapter handoff or spend/booking action.",
                approvalAction?.Approval.ActionId ?? "adapter handoff",
                _session.HasApprovalToken(approvalId) ? "approved" : "approval-required"),
            Trace: new("harness trace", BuildClaims(packet, approvalId)),
            Session: new(_session.SessionId, "Pch.Harness server-side session service", _responses.ToArray()));
    }

    private EmitFormAction ResolveFormAction()
    {
        return _stageMachine.NextSkeletonAction(_session) as EmitFormAction
            ?? new EmitFormAction(
                "action-ui-form-fallback",
                new FormRequest(
                    "ui-readonly-stage",
                    "Current stage",
                    "Apply",
                    [
                        new("destination_country", "Destination country", "text", true, _session.Mission.DestinationCountry, []),
                        new("purpose", "Purpose", "text", true, _session.Mission.Purpose, [])
                    ]));
    }

    private string CurrentApprovalId()
    {
        return (_stageMachine.NextSkeletonAction(_session) as RequestApprovalAction)?.Approval.ApprovalId
            ?? "approval-review";
    }

    private static IReadOnlyList<EvidenceClaimFixture> BuildClaims(StagePacket packet, string approvalId)
    {
        return
        [
            new("claim.session", $"Session {packet.SessionId} is at stage {packet.Stage}.", "harness", packet.PacketId),
            .. packet.LoadBearingFacts.Select((fact, index) => new EvidenceClaimFixture($"claim.fact.{index + 1}", fact, "harness-fact", packet.PacketId)),
            new("claim.approval", "Adapter handoff remains blocked until an approval token is accepted.", "harness-policy", approvalId)
        ];
    }

    private void UpsertResponse(SessionResponseFixture response)
    {
        var index = _responses.FindIndex(existing => string.Equals(existing.Id, response.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            _responses[index] = response;
        }
        else
        {
            _responses.Add(response);
        }
    }
}
