using Pch.Core;
using Pch.Harness;
using Pch.Providers.ModelActions;
using System.Text.Json;

namespace Pch.UI.Features.StageCockpit;

public sealed class HarnessStageCockpitService
{
    private readonly SessionLoop _loop = new();
    private readonly ProjectionService _projection = new();
    private readonly StageMachine _stageMachine = new();
    private readonly ExternalActionDecoder _externalActionDecoder = new();
    private readonly HarnessActionIntake _actionIntake = new();
    private readonly ProviderActionBridge _providerActionBridge = new();
    private readonly RuntimeActionApplication _runtimeActionApplication = new();
    private readonly TripSession _session;
    private readonly List<SessionResponseFixture> _responses = [];
    private readonly List<SuggestedActionOutcomeFixture> _suggestionOutcomes = [];
    private readonly List<ModelSuggestionRunOutcomeFixture> _modelRunOutcomes = [];
    private readonly List<MissionIntakeOutcomeFixture> _missionOutcomes = [];
    private readonly List<MissionFieldFixture> _appliedMissionFields = [];
    private readonly List<MissionFieldFixture> _pendingMissionConfirmations = [];
    private readonly List<MissionCommitmentFixture> _highPriorityCommitments = [];
    private readonly List<MemoryDigestFactFixture> _memoryDigestFacts = [];
    private readonly IReadOnlyList<SuggestedActionFixture> _suggestions =
    [
        new(
            "suggestion.accept.defer-slot",
            "Defer dinner slot",
            HarnessAction.DeferSlotKind,
            "Route a stage-allowed defer-slot action through harness decoder and intake.",
            null,
            null,
            """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }"""),
        new(
            "suggestion.blocked.booking",
            "Blocked booking handoff",
            HarnessAction.HandoffKind,
            "Rejected by harness intake because booking handoff is not allowed for the current stage.",
            null,
            "approval-review",
            """{ "target": "booking-adapter", "reason": "Mock booking handoff." }"""),
        new(
            "suggestion.decode.failure",
            "Malformed proposal",
            HarnessAction.DeferSlotKind,
            "Show a sanitized decode failure without exposing proposal payload.",
            null,
            null,
            """{ "slot_id": "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK" """)
    ];
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

    public StageCockpitFixture ApplySuggestedAction(string suggestionId)
    {
        var suggestion = _suggestions.FirstOrDefault(candidate => string.Equals(candidate.Id, suggestionId, StringComparison.Ordinal));
        if (suggestion is null)
        {
            AddBlockedSuggestionOutcome(
                suggestionId,
                "unknown",
                null,
                null,
                "PCH_UI_SUGGESTION_UNKNOWN",
                "Suggested action is not recognized by the UI seam.");
            return Current();
        }

        var decode = _externalActionDecoder.DecodeJson(suggestion.Id, suggestion.ActionKind, suggestion.JsonArguments);
        if (!decode.IsDecoded)
        {
            AddBlockedSuggestionOutcome(
                suggestion.Id,
                suggestion.ActionKind,
                suggestion.CandidateId,
                suggestion.ApprovalId,
                $"PCH_UI_DECODE_{decode.Code.ToUpperInvariant()}",
                decode.Summary);
            return Current();
        }

        var decodedAction = decode.Action!;
        _lastTurn = _actionIntake.Accept(_session, decodedAction);
        var trace = _lastTurn.Trace.FirstOrDefault();
        var traceOutcome = trace?.Outcome ?? (_lastTurn.IsBlocked ? "blocked" : "accepted");
        var candidateId = suggestion.CandidateId ?? ExtractCandidateId(decodedAction);
        var approvalId = suggestion.ApprovalId ?? ExtractApprovalId(decodedAction);

        UpsertSuggestedOutcome(new(
            suggestion.Id,
            _lastTurn.IsBlocked ? "blocked" : "accepted",
            decodedAction.Kind,
            _lastTurn.IsBlocked ? traceOutcome : "suggestion.accepted",
            candidateId,
            approvalId,
            _lastTurn.IsBlocked ? $"PCH_UI_INTAKE_{traceOutcome.ToUpperInvariant()}" : null,
            _lastTurn.IsBlocked ? _lastTurn.BlockedReason ?? trace?.Summary ?? "Harness intake blocked the suggestion." : null));

        UpsertResponse(_lastTurn.IsBlocked
            ? new(
                $"response.blocked.suggestion.{_responses.Count + 1}",
                SessionResponseState.Blocked,
                "Blocked",
                $"{traceOutcome}: {_lastTurn.BlockedReason ?? "Harness intake blocked the suggestion."}",
                suggestion.Id,
                approvalId)
            : new(
                $"response.applied.suggestion.{_session.Actions.Count}",
                SessionResponseState.Applied,
                "Applied",
                $"Harness intake accepted suggested action {decodedAction.Kind}.",
                suggestion.Id,
                approvalId));

        return Current();
    }

    public StageCockpitFixture RunModelSuggestion(string runId)
    {
        var packet = _projection.Project(_session, _session.Stage);
        var scenario = CreateModelScenario(runId, packet);
        if (scenario is null)
        {
            AddModelRunOutcome(new(
                runId,
                "blocked",
                "unknown",
                "decode_not_run",
                "not_run",
                "intake_not_run",
                "server_model.blocked",
                "PCH_UI_MODEL_RUN_UNKNOWN",
                "Server model suggestion scenario is not recognized.",
                "deterministic-mock",
                "mock-stage-action",
                null));
            return Current();
        }

        var bridge = _providerActionBridge.Bridge(scenario.Packet, scenario.Result);
        if (!bridge.IsAccepted)
        {
            AddModelRunOutcome(new(
                scenario.RunId,
                "blocked",
                scenario.Result.ActionName,
                bridge.DecodeOutcomeCode,
                "not_run",
                bridge.IntakeOutcomeCode,
                bridge.DecodeOutcomeCode,
                $"PCH_UI_BRIDGE_{bridge.DecodeOutcomeCode.ToUpperInvariant()}",
                "Provider bridge rejected the model action before harness intake.",
                scenario.Result.Provider,
                scenario.Result.Model,
                scenario.Result.RequestId));
            UpsertModelRunResponse(scenario.RunId, SessionResponseState.Blocked, bridge.DecodeOutcomeCode, "Provider bridge rejected the model action before harness intake.");
            return Current();
        }

        var runtimeProposal = bridge.RuntimeProposal!;
        var externalProposal = new ExternalActionProposal(
            runtimeProposal.ActionId,
            runtimeProposal.Kind,
            runtimeProposal.Arguments.Clone());
        var runtime = _runtimeActionApplication.Apply(_session, externalProposal);
        _lastTurn = null;
        var trace = runtime.Trace.FirstOrDefault();
        var traceOutcome = trace?.Outcome ?? (runtime.IsBlocked ? "blocked" : "accepted");
        var state = runtime.IsBlocked ? "blocked" : "accepted";

        AddModelRunOutcome(new(
            scenario.RunId,
            state,
            runtimeProposal.Kind,
            bridge.DecodeOutcomeCode,
            runtime.DecodeCode,
            runtime.IntakeCode,
            runtime.IsBlocked ? traceOutcome : "server_model.accepted",
            runtime.IsBlocked ? RuntimeErrorCode(runtime) : null,
            runtime.IsBlocked ? runtime.Summary : null,
            scenario.Result.Provider,
            scenario.Result.Model,
            scenario.Result.RequestId));

        UpsertModelRunResponse(
            scenario.RunId,
            runtime.IsBlocked ? SessionResponseState.Blocked : SessionResponseState.Applied,
            runtime.IsBlocked ? runtime.IntakeCode : "server_model.accepted",
            runtime.IsBlocked ? runtime.Summary : $"Runtime accepted model action {runtimeProposal.Kind}.");

        return Current();
    }

    public StageCockpitFixture RunMissionIntake(string runId)
    {
        var scenario = MissionIntakeScenario.Create(runId);
        if (scenario is null)
        {
            UpsertMissionOutcome(new(
                runId,
                "blocked",
                "planner_unknown_scenario",
                "not_run",
                "not_run",
                "mission_intake.blocked",
                "PCH_UI_MISSION_UNKNOWN_SCENARIO",
                "Mission intake scenario is not recognized.",
                "deterministic-mock",
                "mock-mission-planner",
                null));
            return Current();
        }

        UpsertMissionOutcome(new(
            scenario.RunId,
            scenario.PendingConfirmations.Count > 0 ? "proposed" : "applied",
            "planner_mock_accepted",
            scenario.PendingConfirmations.Count > 0 ? "pending_confirmation" : "applied",
            "memory_digest_updated",
            scenario.PendingConfirmations.Count > 0 ? "mission_intake.proposed" : "mission_intake.applied",
            null,
            null,
            "deterministic-mock",
            "mock-mission-planner",
            $"mock-{scenario.RunId}"));

        UpsertRange(_appliedMissionFields, scenario.AppliedFields, field => field.FieldId);
        UpsertRange(_pendingMissionConfirmations, scenario.PendingConfirmations, field => field.FieldId);
        UpsertRange(_highPriorityCommitments, scenario.Commitments, commitment => commitment.CommitmentId);
        UpsertRange(_memoryDigestFacts, scenario.MemoryDigestFacts, fact => fact.FactId);

        UpsertResponse(new(
            $"response.{(scenario.PendingConfirmations.Count > 0 ? "proposed" : "applied")}.mission.{scenario.RunId}",
            scenario.PendingConfirmations.Count > 0 ? SessionResponseState.Pending : SessionResponseState.Applied,
            scenario.PendingConfirmations.Count > 0 ? "Pending" : "Applied",
            scenario.PendingConfirmations.Count > 0
                ? "Mission planner produced confirmation-ready inferred fields."
                : "Mission planner applied user-stated mission facts.",
            scenario.RunId,
            null));

        return Current();
    }

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
                _session.SelectedCandidateIds.LastOrDefault() ?? "",
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
            Session: new(_session.SessionId, "Pch.Harness server-side session service", _responses.ToArray()),
            SuggestedActions: new(
                "Deterministic UI seam using harness decoder/intake",
                _suggestions,
                _suggestionOutcomes.ToArray()),
            ModelSuggestionRuns: new(
                "Server-side deterministic mock provider through provider bridge and runtime application",
                [
                    new("server-model.accept.defer-slot", "Run accepted model suggestion", HarnessAction.DeferSlotKind),
                    new("server-model.block.form-mismatch", "Run blocked model suggestion", HarnessAction.EmitFormKind),
                    new("server-model.decode.missing-argument", "Run decode-failure model suggestion", HarnessAction.DeferSlotKind)
                ],
                _modelRunOutcomes.ToArray()),
            MissionIntake: new(
                "UI-local deterministic seam pending provider mission planner, harness intake, and memory digest contracts",
                [
                    new("mission.vacation", "Plan vacation intake", "vacation"),
                    new("mission.non-vacation-commitment", "Plan commitment intake", "family-support"),
                    new("mission.pending-confirmation", "Plan confirmation intake", "pending-confirmation")
                ],
                _missionOutcomes.ToArray(),
                _appliedMissionFields.ToArray(),
                _pendingMissionConfirmations.ToArray(),
                _highPriorityCommitments.ToArray(),
                _memoryDigestFacts.ToArray()));
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

    private static string? ExtractCandidateId(HarnessAction? action)
    {
        return action is EmitChoiceSetAction choiceSet
            ? choiceSet.Choices.FirstOrDefault()?.CandidateId
            : null;
    }

    private static string? ExtractApprovalId(HarnessAction? action)
    {
        return action is RequestApprovalAction approval
            ? approval.Approval.ApprovalId
            : null;
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

    private void AddBlockedSuggestionOutcome(
        string suggestionId,
        string actionKind,
        string? candidateId,
        string? approvalId,
        string errorCode,
        string blockedReason)
    {
        UpsertSuggestedOutcome(new(
            suggestionId,
            "blocked",
            actionKind,
            "suggestion.blocked",
            candidateId,
            approvalId,
            errorCode,
            blockedReason));
        UpsertResponse(new(
            $"response.blocked.suggestion.{_responses.Count + 1}",
            SessionResponseState.Blocked,
            "Blocked",
            $"{errorCode}: {blockedReason}",
            suggestionId,
            approvalId));
    }

    private void UpsertSuggestedOutcome(SuggestedActionOutcomeFixture outcome)
    {
        var index = _suggestionOutcomes.FindIndex(existing => string.Equals(existing.SuggestionId, outcome.SuggestionId, StringComparison.Ordinal));
        if (index >= 0)
        {
            _suggestionOutcomes[index] = outcome;
        }
        else
        {
            _suggestionOutcomes.Add(outcome);
        }
    }

    private ModelRunScenario? CreateModelScenario(string runId, StagePacket packet)
    {
        var actionKind = runId switch
        {
            "server-model.accept.defer-slot" => HarnessAction.DeferSlotKind,
            "server-model.block.form-mismatch" => HarnessAction.EmitFormKind,
            "server-model.decode.missing-argument" => HarnessAction.DeferSlotKind,
            _ => null
        };
        if (actionKind is null)
        {
            return null;
        }

        using var arguments = JsonDocument.Parse(runId switch
        {
            "server-model.accept.defer-slot" => """{ "slot_id": "dinner-day-2", "reason": "Need user preference." }""",
            "server-model.block.form-mismatch" => """{ "form_id": "wrong-form", "title": "Slot collection" }""",
            "server-model.decode.missing-argument" => """{ "slot_id": "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK" }""",
            _ => "{}"
        });

        var modelPacket = new ModelActionPacket(
            packet.PacketId,
            packet.Stage,
            packet.CurrentSubtask,
            [
                "Choose one allowed harness action.",
                "Return structured action arguments only."
            ],
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["session_id"] = JsonSerializer.SerializeToElement(packet.SessionId),
                ["stage"] = JsonSerializer.SerializeToElement(packet.Stage),
                ["allowed_action_count"] = JsonSerializer.SerializeToElement(packet.AllowedActions.Count)
            },
            packet.AllowedActions.Select(action => new ModelActionDefinition(action, $"Harness action {action}.")).ToArray());

        var result = new ModelActionRunResult(
            modelPacket.PacketId,
            actionKind,
            arguments.RootElement.Clone(),
            null,
            0,
            "deterministic-mock",
            "mock-stage-action",
            $"mock-{runId}");

        return new(runId, modelPacket, result);
    }

    private void AddModelRunOutcome(ModelSuggestionRunOutcomeFixture outcome)
    {
        var index = _modelRunOutcomes.FindIndex(existing => string.Equals(existing.RunId, outcome.RunId, StringComparison.Ordinal));
        if (index >= 0)
        {
            _modelRunOutcomes[index] = outcome;
        }
        else
        {
            _modelRunOutcomes.Add(outcome);
        }
    }

    private void UpsertModelRunResponse(string runId, SessionResponseState state, string code, string summary)
    {
        UpsertResponse(new(
            $"response.{state.ToString().ToLowerInvariant()}.model-run.{runId}",
            state,
            state == SessionResponseState.Applied ? "Applied" : "Blocked",
            $"{code}: {summary}",
            runId,
            null));
    }

    private static string RuntimeErrorCode(RuntimeActionApplicationResult runtime)
    {
        return runtime.IntakeCode == "not_run"
            ? $"PCH_UI_RUNTIME_DECODE_{runtime.DecodeCode.ToUpperInvariant()}"
            : $"PCH_UI_RUNTIME_INTAKE_{runtime.IntakeCode.ToUpperInvariant()}";
    }

    private void UpsertMissionOutcome(MissionIntakeOutcomeFixture outcome)
    {
        var index = _missionOutcomes.FindIndex(existing => string.Equals(existing.RunId, outcome.RunId, StringComparison.Ordinal));
        if (index >= 0)
        {
            _missionOutcomes[index] = outcome;
        }
        else
        {
            _missionOutcomes.Add(outcome);
        }
    }

    private static void UpsertRange<T>(
        List<T> target,
        IReadOnlyList<T> source,
        Func<T, string> keySelector)
    {
        foreach (var item in source)
        {
            var key = keySelector(item);
            var index = target.FindIndex(existing => string.Equals(keySelector(existing), key, StringComparison.Ordinal));
            if (index >= 0)
            {
                target[index] = item;
            }
            else
            {
                target.Add(item);
            }
        }
    }

    private sealed record ModelRunScenario(
        string RunId,
        ModelActionPacket Packet,
        ModelActionRunResult Result);

    private sealed record MissionIntakeScenario(
        string RunId,
        IReadOnlyList<MissionFieldFixture> AppliedFields,
        IReadOnlyList<MissionFieldFixture> PendingConfirmations,
        IReadOnlyList<MissionCommitmentFixture> Commitments,
        IReadOnlyList<MemoryDigestFactFixture> MemoryDigestFacts)
    {
        public static MissionIntakeScenario? Create(string runId)
        {
            return runId switch
            {
                "mission.vacation" => new(
                    runId,
                    [
                        new("purpose", "Purpose", "vacation", "user-stated", "applied"),
                        new("destination_country", "Destination country", "Japan", "user-stated", "applied"),
                        new("date_window", "Date window", "2026-10-05 to 2026-10-19", "user-stated", "applied")
                    ],
                    [],
                    [],
                    [
                        new("memory.destination", "Destination country is Japan.", "user-stated", "destination_country"),
                        new("memory.purpose", "Trip purpose is vacation.", "user-stated", "purpose")
                    ]),
                "mission.non-vacation-commitment" => new(
                    runId,
                    [
                        new("purpose", "Purpose", "family_support", "user-stated", "applied"),
                        new("destination_country", "Destination country", "Poland", "user-stated", "applied")
                    ],
                    [],
                    [
                        new("commitment.family-anchor", "Attend family support appointment", "FixedAnchor", "high", "user-stated")
                    ],
                    [
                        new("memory.commitment.family-anchor", "High-priority family support appointment must stay fixed.", "user-stated", "commitment.family-anchor")
                    ]),
                "mission.pending-confirmation" => new(
                    runId,
                    [
                        new("destination_country", "Destination country", "Japan", "user-stated", "applied")
                    ],
                    [
                        new("pace", "Pace", "balanced", "model-inferred", "pending-confirmation"),
                        new("traveler_need", "Traveler need", "low cognitive load", "model-inferred", "pending-confirmation")
                    ],
                    [],
                    [
                        new("memory.pending.pace", "Pace preference requires user confirmation.", "model-inferred", "pace")
                    ]),
                _ => null
            };
        }
    }
}
