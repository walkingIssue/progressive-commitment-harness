using Pch.Harness;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserChatService
{
    public const string DefaultPrompt = "Plan a calm family trip to Japan with one quiet day and no real bookings.";
    public const string ModeLabel = "Live in-harness";
    public const string ModeState = "live-in-harness";
    public const string RawAbsenceState = "verified";
    public const string ProviderOutcomeFallback = "deterministic_fallback_active";
    public const string ApprovalRequiredCode = "approval_required_preview";
    private const string DeterministicModeLabel = "Deterministic offline";
    private const string DeterministicModeState = "offline-deterministic";

    private const string PendingConfirmationCode = "end_user_chat_pending_confirmation";
    private const string FormId = "form-trip-basics";
    private const string ChoiceSetId = "choice-japan-style";
    private const string ApprovalId = "approval-preview-mock-hold";
    private const string CandidateClassic = "candidate-japan-classic-highlights";
    private const string CandidateScenic = "candidate-japan-scenic-explorer";
    private const string CandidateCulture = "candidate-japan-reflective-culture";
    private const string CandidateTransit = "candidate-japan-transit-rhythm";

    private readonly GoldenTurnTraceRunner _traceRunner;
    private readonly ModelRoleStatusEvaluator _roleStatusEvaluator;
    private readonly EndUserLiveModelTurnService _liveModelTurnService;

    public EndUserChatService()
        : this(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new MockModelRoleStatusSource()),
            new EndUserLiveModelTurnService())
    {
    }

    public EndUserChatService(
        GoldenTurnTraceRunner traceRunner,
        ModelRoleStatusEvaluator roleStatusEvaluator,
        EndUserLiveModelTurnService? liveModelTurnService = null)
    {
        _traceRunner = traceRunner ?? throw new ArgumentNullException(nameof(traceRunner));
        _roleStatusEvaluator = roleStatusEvaluator ?? throw new ArgumentNullException(nameof(roleStatusEvaluator));
        _liveModelTurnService = liveModelTurnService ?? new EndUserLiveModelTurnService();
    }

    public EndUserChatState CreateInitialState(string selectedModelRole = EndUserModelRoleSelection.InHarnessActionGenerator)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        var liveSnapshot = _liveModelTurnService.CreateSnapshot(normalizedRole);
        return new(
            ModeLabelFor(liveSnapshot),
            ModeStateFor(liveSnapshot),
            ModelRoleStatusEvaluator.OutcomeReady,
            normalizedRole == EndUserModelRoleSelection.DeterministicOffline
                ? ActiveRoleMarker(ModelRoleKind.DeterministicOffline)
                : normalizedRole,
            liveSnapshot.SelectedModelRole,
            liveSnapshot.SelectedProvider,
            liveSnapshot.LivePreflightState,
            liveSnapshot.LiveProposalState,
            liveSnapshot.HarnessValidationState,
            liveSnapshot.LatestTurnSource,
            liveSnapshot.ProviderRequestState,
            liveSnapshot.ProviderOutcome,
            liveSnapshot.ProviderHealth,
            liveSnapshot.CreditState,
            liveSnapshot.LastProviderFailureCode,
            "not_requested",
            RawAbsenceState,
            DefaultPrompt,
            "idle",
            "idle",
            liveSnapshot.ErrorCode,
            liveSnapshot.BlockedReason,
            [
                new(
                    "turn-system-ready",
                    "system",
                    "mode",
                    "ready",
                    InitialSystemText(liveSnapshot),
                    ModeStateFor(liveSnapshot),
                    null,
                    null),
                new(
                    "turn-assistant-start",
                    "assistant",
                    "guidance",
                    "ready",
                    InitialGuidanceText(liveSnapshot),
                    null,
                    null,
                    null)
            ],
            InitialTasks(),
            null,
            null,
            null,
            liveSnapshot.FailureNotice ?? (normalizedRole == EndUserModelRoleSelection.DeterministicOffline ? FallbackNotice() : null),
            [],
            [],
            []);
    }

    public async Task<EndUserChatState> SendAsync(
        string prompt,
        string selectedModelRole = EndUserModelRoleSelection.InHarnessActionGenerator,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = NormalizePrompt(prompt);
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        var liveSnapshot = await _liveModelTurnService
            .TryRunAsync(normalizedPrompt, normalizedRole, cancellationToken)
            .ConfigureAwait(false);
        var roleStatus = await EvaluateRoleStatus(cancellationToken).ConfigureAwait(false);
        if (IsLiveMode(normalizedRole))
        {
            return BuildLiveState(normalizedPrompt, liveSnapshot, roleStatus, BuildLiveTurns(normalizedPrompt, roleStatus, liveSnapshot));
        }

        var scenario = SelectScenario(normalizedPrompt);
        var trace = _traceRunner.Run(ScriptFor(scenario));
        var turns = BuildTraceTurns(normalizedPrompt, scenario, trace, roleStatus, liveSnapshot);

        var finalState = FinalStateFor(scenario, trace);
        var errorCode = ErrorCodeFor(scenario, trace) ?? liveSnapshot.ErrorCode;
        var blockedReason = trace.IsBlocked ? trace.Code : liveSnapshot.BlockedReason;

        return new(
            ModeLabelFor(liveSnapshot),
            ModeStateFor(liveSnapshot),
            roleStatus.OutcomeCode,
            normalizedRole == EndUserModelRoleSelection.DeterministicOffline
                ? ActiveRoleMarker(roleStatus.ActiveRole)
                : normalizedRole,
            liveSnapshot.SelectedModelRole,
            liveSnapshot.SelectedProvider,
            liveSnapshot.LivePreflightState,
            liveSnapshot.LiveProposalState,
            liveSnapshot.HarnessValidationState,
            liveSnapshot.LatestTurnSource,
            liveSnapshot.ProviderRequestState,
            liveSnapshot.ProviderOutcome,
            liveSnapshot.ProviderHealth,
            liveSnapshot.CreditState,
            liveSnapshot.LastProviderFailureCode,
            "not_requested",
            RawAbsenceState,
            string.Empty,
            scenario == EndUserChatScenario.PendingConfirmation ? "awaiting_user_input" : "awaiting_user_input",
            finalState,
            errorCode,
            blockedReason,
            turns,
            TasksAfterSend(scenario),
            FormCard("draft"),
            ChoiceSet("active", null, null),
            ApprovalPlate("not_requested", null),
            liveSnapshot.FailureNotice ?? FallbackNotice(),
            EvidenceFrom(trace),
            PlanTrailFrom(trace, null, null),
            TimelineFrom(trace, null, null));
    }

    public EndUserChatState Send(string prompt) =>
        SendAsync(prompt).GetAwaiter().GetResult();

    public EndUserChatState ApplyModelRole(EndUserChatState state, string selectedModelRole)
    {
        ArgumentNullException.ThrowIfNull(state);

        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        var liveSnapshot = _liveModelTurnService.CreateSnapshot(normalizedRole);
        return state with
        {
            ModeLabel = ModeLabelFor(liveSnapshot),
            ModeState = ModeStateFor(liveSnapshot),
            RoleStatusActiveRole = normalizedRole == EndUserModelRoleSelection.DeterministicOffline
                ? ActiveRoleMarker(ModelRoleKind.DeterministicOffline)
                : normalizedRole,
            SelectedModelRole = liveSnapshot.SelectedModelRole,
            SelectedProvider = liveSnapshot.SelectedProvider,
            LivePreflightState = liveSnapshot.LivePreflightState,
            LiveProposalState = liveSnapshot.LiveProposalState,
            HarnessValidationState = liveSnapshot.HarnessValidationState,
            LatestTurnSource = liveSnapshot.LatestTurnSource,
            ProviderRequestState = liveSnapshot.ProviderRequestState,
            ProviderOutcome = liveSnapshot.ProviderOutcome,
            ProviderHealth = liveSnapshot.ProviderHealth,
            CreditState = liveSnapshot.CreditState,
            LastProviderFailureCode = liveSnapshot.LastProviderFailureCode,
            ErrorCode = liveSnapshot.ErrorCode,
            BlockedReason = liveSnapshot.BlockedReason,
            ProviderFailure = liveSnapshot.FailureNotice ?? (normalizedRole == EndUserModelRoleSelection.DeterministicOffline ? FallbackNotice() : null)
        };
    }

    public EndUserChatState SubmitForm(EndUserChatState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state with
        {
            ComposerState = "awaiting_user_input",
            FinalState = "form_submitted",
            FormCard = FormCard("accepted"),
            Tasks = UpdateTask(state.Tasks, "task-basics", "accepted", 100, "Accepted"),
            Turns = AppendTurn(state.Turns, new(
                "turn-form-submitted",
                "user",
                "form",
                "accepted",
                "Trip basics were submitted and accepted by the deterministic harness path.",
                "form_card_accepted",
                "evidence-chat-purpose",
                null))
        };
    }

    public EndUserChatState SelectCandidate(EndUserChatState state, string candidateId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var candidate = state.ChoiceSet?.Candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId);
        if (candidate is null)
        {
            return BlockWithFixedError(state, "PCH_UI_CHAT_UNKNOWN_CANDIDATE", "Unknown candidate id was rejected.");
        }

        var turns = AppendTurn(state.Turns, new(
            "turn-choice-selected",
            "user",
            "choice",
            "selected",
            $"Selected {candidate.Title} for {candidate.Category}.",
            "choice_candidate_selected",
            "evidence-chat-candidate",
            null,
            candidate.CandidateId,
            candidate.Category));

        var isLiveMode = IsLiveMode(state.SelectedModelRole);
        var planningTimeline = TimelineFromState(state, candidate, "selected");
        if (isLiveMode)
        {
            turns = UpsertTurn(turns, LiveSecondTurnBlocked(candidate.CandidateId));
            planningTimeline = UpsertTimeline(planningTimeline, LiveSecondTurnTimeline(candidate));
        }

        return state with
        {
            ComposerState = "awaiting_user_input",
            FinalState = isLiveMode ? "live_second_turn_blocked" : "candidate_selected",
            ChoiceSet = ChoiceSet("selected", candidateId, null),
            Tasks = UpdateTask(state.Tasks, "task-itinerary", "accepted", 72, "Candidate selected"),
            PlanTrail = PlanTrailFromState(state, candidate, "selected"),
            PlanningTimeline = planningTimeline,
            ProviderRequestState = isLiveMode ? "second_turn_blocked" : state.ProviderRequestState,
            ProviderOutcome = isLiveMode ? "live_turn_provider_unknown_error" : state.ProviderOutcome,
            ProviderHealth = isLiveMode ? "harness_multiturn_provider_blocked" : state.ProviderHealth,
            LastProviderFailureCode = isLiveMode ? "provider_unknown_error" : state.LastProviderFailureCode,
            Turns = turns
        };
    }

    public EndUserChatState DeferCandidate(EndUserChatState state, string candidateId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var candidate = state.ChoiceSet?.Candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId);
        if (candidate is null)
        {
            return BlockWithFixedError(state, "PCH_UI_CHAT_UNKNOWN_CANDIDATE", "Unknown candidate id was rejected.");
        }

        return state with
        {
            ComposerState = "awaiting_user_input",
            FinalState = "candidate_deferred",
            ChoiceSet = ChoiceSet("deferred", null, candidateId),
            Tasks = UpdateTask(state.Tasks, "task-itinerary", "deferred", 52, "Deferred"),
            PlanTrail = PlanTrailFromState(state, candidate, "deferred"),
            PlanningTimeline = TimelineFromState(state, candidate, "deferred"),
            Turns = AppendTurn(state.Turns, new(
                "turn-choice-deferred",
                "user",
                "choice",
                "deferred",
                "A candidate was deferred without losing its candidate id or evidence references.",
                "choice_candidate_deferred",
                "evidence-chat-candidate",
                null))
        };
    }

    public EndUserChatState RequestApproval(EndUserChatState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state with
        {
            ComposerState = "blocked_harness",
            FinalState = "blocked",
            ErrorCode = "PCH_UI_CHAT_APPROVAL_REQUIRED",
            BlockedReason = ApprovalRequiredCode,
            ApprovalState = "blocked_missing_approval",
            ApprovalPlate = ApprovalPlate("blocked_missing_approval", ApprovalRequiredCode),
            Tasks = UpdateTask(state.Tasks, "task-approval", "blocked", 35, "Approval required"),
            PlanTrail = AppendPlanTrail(state.PlanTrail, new("trail-approval-blocked", "approval", "blocked", "Mock hold approval blocked", null, "evidence-chat-approval", MediaAsset("logistics_transit"), ApprovalRequiredCode)),
            PlanningTimeline = AppendTimeline(state.PlanningTimeline, new("timeline-approval-blocked", "task", "approval", "blocked", "Mock hold blocked", "Approval is required before any mock hold preview can continue.", null, null, "task-approval", null, "decision-approval-preview", "evidence-chat-approval", "turn-approval-blocked", MediaAsset("logistics_transit"), ApprovalRequiredCode)),
            Turns = AppendTurn(state.Turns, new(
                "turn-approval-blocked",
                "harness",
                "approval",
                "blocked",
                "Mock hold preparation is blocked until explicit approval is available. No hold or booking was created.",
                ApprovalRequiredCode,
                "evidence-chat-approval",
                "PCH_UI_CHAT_APPROVAL_REQUIRED"))
        };
    }

    private static IReadOnlyList<EndUserChatTurn> BuildTraceTurns(
        string normalizedPrompt,
        EndUserChatScenario scenario,
        GoldenTurnTraceResult trace,
        SanitizedModelRoleStatusEvalRow roleStatus,
        EndUserLiveModelSnapshot liveSnapshot)
    {
        var turns = new List<EndUserChatTurn>
        {
            new(
                "turn-user-1",
                "user",
                "prompt",
                "submitted",
                PromptSummary(normalizedPrompt),
                "prompt_received",
                null,
                null),
            new(
                "turn-provider-role-status",
                "provider",
                "role-status",
                "applied",
                RoleStatusText(roleStatus),
                roleStatus.OutcomeCode,
                null,
                roleStatus.ErrorCode)
        };

        if (liveSnapshot.Turn is not null)
        {
            turns.Add(liveSnapshot.Turn);
        }

        if (IsLiveMode(liveSnapshot.SelectedModelRole))
        {
            turns.Add(new(
                "turn-live-work-item-1",
                "assistant",
                liveSnapshot.LiveProposalState == EndUserLiveProposalMarkers.Accepted ? "live-work-item" : "live-blocked",
                liveSnapshot.LiveProposalState == EndUserLiveProposalMarkers.Accepted ? "applied" : "blocked",
                LiveWorkItemText(liveSnapshot),
                liveSnapshot.ProviderOutcome,
                "evidence-chat-live-model",
                liveSnapshot.ErrorCode));
            return turns;
        }

        turns.AddRange(trace.Turns.Select(turn => new EndUserChatTurn(
            turn.TurnId,
            turn.Actor,
            turn.Kind,
            StateFor(turn, scenario, trace),
            turn.Summary,
            turn.Code,
            turn.EvidenceReferences.FirstOrDefault(),
            trace.IsBlocked && turn.Kind == "blocked" ? trace.Code : null)));

        turns.Add(new(
            "turn-assistant-final",
            "assistant",
            trace.IsBlocked ? "blocked" : "final",
            FinalStateFor(scenario, trace),
            FinalText(scenario, trace),
            scenario == EndUserChatScenario.PendingConfirmation ? PendingConfirmationCode : trace.Code,
            trace.EvidenceReferences.FirstOrDefault(),
            ErrorCodeFor(scenario, trace)));

        return turns;
    }

    private static IReadOnlyList<EndUserChatTurn> BuildLiveTurns(
        string normalizedPrompt,
        SanitizedModelRoleStatusEvalRow roleStatus,
        EndUserLiveModelSnapshot liveSnapshot)
    {
        var isAccepted = liveSnapshot.IsLiveAccepted();
        var turns = new List<EndUserChatTurn>
        {
            new(
                "turn-user-1",
                "user",
                "prompt",
                "submitted",
                PromptSummary(normalizedPrompt),
                "prompt_received",
                null,
                null),
            new(
                "turn-provider-role-status",
                "provider",
                "role-status",
                roleStatus.Passed ? "applied" : "blocked",
                roleStatus.Passed
                    ? "Live model role posture was evaluated before the provider turn."
                    : $"Model role posture blocked with {roleStatus.OutcomeCode}.",
                roleStatus.OutcomeCode,
                null,
                null)
        };

        if (liveSnapshot.Turn is not null)
        {
            turns.Add(liveSnapshot.Turn);
        }

        turns.Add(new(
            "turn-live-work-item-1",
            "assistant",
            isAccepted ? "live-work-item" : "live-blocked",
            isAccepted ? "applied" : "blocked",
            LiveWorkItemText(liveSnapshot),
            liveSnapshot.ProviderOutcome,
            "evidence-chat-live-model",
            liveSnapshot.ErrorCode));

        return turns;
    }

    private static EndUserChatState BuildLiveState(
        string normalizedPrompt,
        EndUserLiveModelSnapshot liveSnapshot,
        SanitizedModelRoleStatusEvalRow roleStatus,
        IReadOnlyList<EndUserChatTurn> turns)
    {
        var isAccepted = liveSnapshot.IsLiveAccepted();
        return new(
            ModeLabelFor(liveSnapshot),
            ModeStateFor(liveSnapshot),
            roleStatus.OutcomeCode,
            liveSnapshot.SelectedModelRole,
            liveSnapshot.SelectedModelRole,
            liveSnapshot.SelectedProvider,
            liveSnapshot.LivePreflightState,
            liveSnapshot.LiveProposalState,
            liveSnapshot.HarnessValidationState,
            liveSnapshot.LatestTurnSource,
            liveSnapshot.ProviderRequestState,
            liveSnapshot.ProviderOutcome,
            liveSnapshot.ProviderHealth,
            liveSnapshot.CreditState,
            liveSnapshot.LastProviderFailureCode,
            "not_requested",
            RawAbsenceState,
            string.Empty,
            "awaiting_user_input",
            isAccepted ? "live_model_applied" : "live_model_blocked",
            liveSnapshot.ErrorCode,
            liveSnapshot.BlockedReason,
            turns,
            LiveTasks(liveSnapshot),
            isAccepted ? FormCard("draft") : null,
            null,
            null,
            liveSnapshot.FailureNotice,
            LiveEvidence(liveSnapshot),
            LivePlanTrail(liveSnapshot),
            LiveTimeline(liveSnapshot));
    }

    private static EndUserChatTurn LiveSecondTurnBlocked(string candidateId) =>
        new(
            "turn-live-model-followup",
            "provider",
            "live-model-followup",
            "blocked",
            "A second live turn was blocked with the canonical live-turn provider diagnostic. Deterministic planning remains available.",
            "live_turn_provider_unknown_error",
            "evidence-chat-live-model",
            "PCH_UI_LIVE_TURN_PROVIDER_BLOCKED",
            candidateId,
            "trip-style");

    private static EndUserPlanningTimelineItem LiveSecondTurnTimeline(EndUserCandidateOption candidate) =>
        new(
            "timeline-live-second-turn",
            "task",
            "live-model-followup",
            "blocked",
            "Second live turn blocked",
            "The selected option is preserved while the live-turn provider diagnostic is surfaced safely.",
            "day-japan-02",
            "slot-live-followup",
            "task-itinerary",
            candidate.CandidateId,
            $"decision-live-followup-{candidate.CandidateId}",
            candidate.EvidenceIds.FirstOrDefault(),
            "turn-live-model-followup",
            candidate.Media,
            "live_turn_provider_unknown_error");

    private static string LiveWorkItemText(EndUserLiveModelSnapshot liveSnapshot)
    {
        if (liveSnapshot.LiveProposalState == EndUserLiveProposalMarkers.Accepted &&
            liveSnapshot.HarnessValidationState != EndUserLiveProposalMarkers.HarnessValidationBlocked)
        {
            return "Live model output was applied by the harness and converted into this planning work item.";
        }

        if (liveSnapshot.HarnessValidationState == EndUserLiveProposalMarkers.HarnessValidationBlocked)
        {
            return "Live model output reached the harness, but validation blocked it with a fixed sanitized result.";
        }

        return "Live model output was blocked before application. Deterministic mode is available only as an explicit fallback.";
    }

    private static string InitialSystemText(EndUserLiveModelSnapshot liveSnapshot)
    {
        if (liveSnapshot.SelectedModelRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return "Deterministic offline mode is active by explicit selection. Real bookings, holds, and payments remain disabled.";
        }

        return liveSnapshot.LivePreflightState == EndUserLiveModelTurnService.PreflightReady
            ? "Live in-harness model planning is selected. Real bookings, holds, and payments remain approval-gated and mocked."
            : "Live in-harness model planning is selected, but provider configuration is missing or blocked. No deterministic plan will run unless selected explicitly.";
    }

    private static string InitialGuidanceText(EndUserLiveModelSnapshot liveSnapshot) =>
        liveSnapshot.SelectedModelRole == EndUserModelRoleSelection.DeterministicOffline
            ? "Tell me the trip you want to shape, then send it through the deterministic harness fixture."
            : "Tell me the trip you want to shape. The next response should come from the configured live model path, or show a live-provider block.";

    private async Task<SanitizedModelRoleStatusEvalRow> EvaluateRoleStatus(CancellationToken cancellationToken)
    {
        var row = await _roleStatusEvaluator.EvaluateAsync(
            [new ModelRoleStatusEvalCase("end-user-chat-model-role", CreateRoleStatusPacket())],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row.Single();
    }

    private static ModelRoleStatusPacket CreateRoleStatusPacket() =>
        new(
            "packet-end-user-chat-role-status",
            [
                new ModelRoleRequest(ModelRoleKind.DeterministicOffline, ModelRoleProviderMode.OfflineDeterministic, true, true),
                new ModelRoleRequest(ModelRoleKind.SmallModel, ModelRoleProviderMode.HostedSmallModel, false, false),
                new ModelRoleRequest(ModelRoleKind.StrongModel, ModelRoleProviderMode.HostedStrongModel, false, false),
                new ModelRoleRequest(ModelRoleKind.LiveProviderDisabled, ModelRoleProviderMode.LiveProviderDisabled, false, false)
            ],
            ModelRoleKind.DeterministicOffline,
            AllowFallback: false,
            Locale: "en-US",
            ContextDigest: "end-user-chat-offline-deterministic");

    private static IReadOnlyList<EndUserTask> InitialTasks() =>
    [
        new("task-basics", "Understand trip basics", "not_started", 0, "Ready", BasicsSteps("not_started"), true),
        new("task-destination", "Destination ideas", "not_started", 0, "Ready", [new("destination-ideas", "Sketch destination options", "not_started")], false),
        new("task-itinerary", "Itinerary options", "not_started", 0, "Ask", ItinerarySteps("not_started"), true),
        new("task-approval", "Book and confirm", "not_started", 0, "Approval gated", [new("approval-mock-hold", "Review mock hold preview", "not_started")], false)
    ];

    private static IReadOnlyList<EndUserTask> TasksAfterSend(EndUserChatScenario scenario) =>
    [
        new("task-basics", "Understand trip basics", "active", 65, "Needs details", BasicsSteps("active"), true),
        new("task-destination", "Destination ideas", "accepted", 100, "Ready", [new("destination-ideas", "Sketch destination options", "accepted")], false),
        new("task-itinerary", "Itinerary options", scenario == EndUserChatScenario.PendingConfirmation ? "needs_user" : "active", 48, "Needs review", ItinerarySteps("active"), true),
        new("task-approval", "Book and confirm", "not_started", 0, "Approval gated", [new("approval-mock-hold", "Review mock hold preview", "not_started")], false)
    ];

    private static IReadOnlyList<EndUserTaskStep> BasicsSteps(string state) =>
    [
        new("trip-purpose", "Trip purpose", state == "not_started" ? "not_started" : "accepted"),
        new("travel-style", "Travel style", state == "not_started" ? "not_started" : "needs_user"),
        new("budget-range", "Budget range", state == "not_started" ? "not_started" : "needs_user"),
        new("preferred-dates", "Preferred dates", state == "not_started" ? "not_started" : "needs_user")
    ];

    private static IReadOnlyList<EndUserTaskStep> ItinerarySteps(string state) =>
    [
        new("build-option-a", "Build option A", state == "not_started" ? "not_started" : "active"),
        new("build-option-b", "Build option B", state == "not_started" ? "not_started" : "active"),
        new("compare-options", "Compare options", "needs_user"),
        new("get-choice", "Get your choice", "needs_user")
    ];

    private static EndUserFormCard FormCard(string state) =>
        new(
            FormId,
            "Trip basics",
            "Confirm the structured details before itinerary work continues.",
            state,
            [
                new("field-trip-purpose", "Purpose", "Vacation", true, state, "evidence-chat-purpose"),
                new("field-travel-style", "Travel style", "Balanced culture and quiet time", true, state, "evidence-chat-style"),
                new("field-budget-range", "Budget range", "Moderate", false, state, "evidence-chat-budget")
            ],
            ["evidence-chat-purpose", "evidence-chat-style"]);

    private static EndUserChoiceSetCard ChoiceSet(string state, string? selectedCandidateId, string? deferredCandidateId) =>
        new(
            ChoiceSetId,
            "Choose an itinerary direction",
            "Pick the direction that feels closest, or defer one while we gather more detail.",
            state,
            selectedCandidateId,
            deferredCandidateId,
            [
                Candidate(CandidateClassic, "reflective-culture", "culture", "reflective_culture", "Classic Japan highlights", "Tokyo, Kyoto, and Osaka with cultural landmarks and local favorites.", selectedCandidateId, deferredCandidateId, ["evidence-chat-candidate", "evidence-chat-route-a"]),
                Candidate(CandidateCulture, "reflective-culture", "culture", "cultural_immersive", "Temple mornings and neighborhood evenings", "A slower cultural route with calm mornings and local evening walks.", selectedCandidateId, deferredCandidateId, ["evidence-chat-candidate", "evidence-chat-route-c"]),
                Candidate(CandidateScenic, "soft-nature", "nature", "soft_nature", "Scenic Japan explorer", "Mountains, hot springs, and coastal towns for a quieter route.", selectedCandidateId, deferredCandidateId, ["evidence-chat-candidate", "evidence-chat-route-b"]),
                Candidate(CandidateTransit, "logistics-transit", "transit", "missing_provider_media", "Transit rhythm and easy transfers", "Clean route timing and low-friction station changes.", selectedCandidateId, deferredCandidateId, ["evidence-chat-candidate", "evidence-chat-route-transit"])
            ]);

    private static EndUserCandidateOption Candidate(
        string candidateId,
        string mood,
        string tone,
        string mediaAssetId,
        string title,
        string summary,
        string? selectedCandidateId,
        string? deferredCandidateId,
        IReadOnlyList<string> evidenceIds)
    {
        var state = string.Equals(candidateId, selectedCandidateId, StringComparison.Ordinal)
            ? "selected"
            : string.Equals(candidateId, deferredCandidateId, StringComparison.Ordinal)
                ? "deferred"
                : "available";
        return new(candidateId, "itinerary", "trip-style", mood, tone, title, summary, state, "deterministic-fixture", MediaAsset(mediaAssetId), evidenceIds);
    }

    private static EndUserApprovalPlate ApprovalPlate(string state, string? blockedReason) =>
        new(
            ApprovalId,
            "Mock hold preview",
            state,
            state == "blocked_missing_approval" ? ApprovalRequiredCode : "not_requested",
            blockedReason,
            ["evidence-chat-approval"]);

    private static EndUserProviderFailureNotice FallbackNotice() =>
        new(
            "notice-deterministic-fallback",
            ProviderOutcomeFallback,
            "deterministic",
            "Deterministic fixture mode is active by explicit selection. Real booking, hold, and payment calls remain mocked.",
            CanRetry: false,
            CanContinueDeterministic: true);

    private static IReadOnlyList<EndUserTask> LiveTasks(EndUserLiveModelSnapshot liveSnapshot)
    {
        var state = liveSnapshot.IsLiveAccepted() ? "active" : "blocked";
        var label = liveSnapshot.IsLiveAccepted() ? "Live applied" : "Live blocked";
        return
        [
            new("task-live-model", "Live model turn", state, liveSnapshot.IsLiveAccepted() ? 70 : 20, label, [new("live-provider-turn", "Run live model through harness", state)], true),
            new("task-itinerary", "Itinerary options", liveSnapshot.IsLiveAccepted() ? "needs_user" : "not_started", liveSnapshot.IsLiveAccepted() ? 35 : 0, liveSnapshot.IsLiveAccepted() ? "Needs review" : "Waiting", ItinerarySteps(liveSnapshot.IsLiveAccepted() ? "active" : "not_started"), true),
            new("task-approval", "Book and confirm", "not_started", 0, "Approval gated", [new("approval-mock-hold", "Review mock hold preview", "not_started")], false)
        ];
    }

    private static IReadOnlyList<EndUserEvidenceItem> LiveEvidence(EndUserLiveModelSnapshot liveSnapshot) =>
    [
        new("evidence-chat-live-model", "Live model boundary", "provider", liveSnapshot.ProviderOutcome),
        new("evidence-chat-no-booking", "Booking and payment side effects remain mocked", "approval", ApprovalRequiredCode)
    ];

    private static IReadOnlyList<EndUserPlanTrailItem> LivePlanTrail(EndUserLiveModelSnapshot liveSnapshot) =>
    [
        new(
            "trail-live-model-turn",
            "live-model",
            liveSnapshot.IsLiveAccepted() ? "accepted" : "blocked",
            liveSnapshot.IsLiveAccepted() ? "Live model turn applied" : "Live model turn blocked",
            null,
            "evidence-chat-live-model",
            MediaAsset(liveSnapshot.IsLiveAccepted() ? "calm_morning" : "logistics_transit"),
            liveSnapshot.ProviderOutcome)
    ];

    private static IReadOnlyList<EndUserPlanningTimelineItem> LiveTimeline(EndUserLiveModelSnapshot liveSnapshot) =>
    [
        new(
            "timeline-live-model-turn",
            "task",
            "live-model",
            liveSnapshot.IsLiveAccepted() ? "accepted" : "blocked",
            liveSnapshot.IsLiveAccepted() ? "Live model applied" : "Live model blocked",
            liveSnapshot.IsLiveAccepted()
                ? "The provider result reached the harness boundary."
                : "The provider result did not reach an accepted harness planning turn.",
            null,
            null,
            "task-live-model",
            null,
            "decision-live-model-turn",
            "evidence-chat-live-model",
            "turn-live-model-run",
            MediaAsset(liveSnapshot.IsLiveAccepted() ? "calm_morning" : "logistics_transit"),
            liveSnapshot.ProviderOutcome)
    ];

    private static IReadOnlyList<EndUserEvidenceItem> EvidenceFrom(GoldenTurnTraceResult trace)
    {
        var evidence = trace.EvidenceReferences
            .Take(4)
            .Select(reference => new EndUserEvidenceItem(reference, "Canonical trace evidence", "trace", trace.Code))
            .ToList();

        evidence.Add(new("evidence-chat-candidate", "Candidate provenance retained", "candidate", "candidate_pool_ready"));
        evidence.Add(new("evidence-chat-approval", "Approval gate retained", "approval", ApprovalRequiredCode));
        return evidence;
    }

    private static IReadOnlyList<EndUserPlanTrailItem> PlanTrailFrom(
        GoldenTurnTraceResult trace,
        EndUserCandidateOption? selected,
        EndUserCandidateOption? deferred)
    {
        var items = new List<EndUserPlanTrailItem>
        {
            new("trail-mission-facts", "mission", "accepted", "Mission facts accepted", null, trace.EvidenceReferences.FirstOrDefault(), MediaAsset("calm_morning"), trace.Code),
            new("trail-pending-confirmations", "confirmation", "pending", "Travel style and dates pending confirmation", null, "evidence-chat-style", MediaAsset("restorative_downtime"), PendingConfirmationCode),
            new("trail-availability", "availability", trace.IsBlocked ? "blocked" : "quote-ready", "Availability preview remains gated", null, "evidence-chat-approval", MediaAsset("logistics_transit"), trace.IsBlocked ? trace.Code : "availability_preview_ready")
        };

        if (selected is not null)
        {
            items.Add(new("trail-selected-option", "selected-option", "selected", selected.Title, selected.CandidateId, selected.EvidenceIds.FirstOrDefault(), selected.Media, "choice_candidate_selected"));
        }

        if (deferred is not null)
        {
            items.Add(new("trail-deferred-option", "deferred-option", "deferred", deferred.Title, deferred.CandidateId, deferred.EvidenceIds.FirstOrDefault(), deferred.Media, "choice_candidate_deferred"));
        }

        return items;
    }

    private static IReadOnlyList<EndUserPlanTrailItem> PlanTrailFromState(
        EndUserChatState state,
        EndUserCandidateOption candidate,
        string stateName)
    {
        var outcome = stateName == "selected" ? "choice_candidate_selected" : "choice_candidate_deferred";
        var kind = stateName == "selected" ? "selected-option" : "deferred-option";
        var trailId = stateName == "selected" ? "trail-selected-option" : "trail-deferred-option";
        return AppendPlanTrail(
            state.PlanTrail.Where(item => item.TrailId != trailId).ToArray(),
            new(trailId, kind, stateName, candidate.Title, candidate.CandidateId, candidate.EvidenceIds.FirstOrDefault(), candidate.Media, outcome));
    }

    private static IReadOnlyList<EndUserPlanningTimelineItem> TimelineFrom(
        GoldenTurnTraceResult trace,
        EndUserCandidateOption? selected,
        EndUserCandidateOption? deferred)
    {
        var items = new List<EndUserPlanningTimelineItem>
        {
            new("timeline-day-1-mission", "day", "mission", "accepted", "Day 1 direction", "Culture-first Japan trip facts accepted.", "day-japan-01", "slot-morning", null, null, "decision-mission-facts", trace.EvidenceReferences.FirstOrDefault(), "turn-03", MediaAsset("calm_morning"), trace.Code),
            new("timeline-day-1-confirmation", "day", "confirmation", "pending", "Style confirmation", "Dates and travel style remain confirmation-ready.", "day-japan-01", "slot-planning", null, null, "decision-pending-confirmation", "evidence-chat-style", "turn-assistant-final", MediaAsset("restorative_downtime"), PendingConfirmationCode),
            new("timeline-day-2-availability", "day", "availability", trace.IsBlocked ? "blocked" : "quote-ready", "Availability guarded", "Quote and hold-adjacent work remains approval gated.", "day-japan-02", "slot-availability", null, null, "decision-availability-preview", "evidence-chat-approval", "turn-assistant-final", MediaAsset("logistics_transit"), trace.IsBlocked ? trace.Code : "availability_preview_ready"),
            new("timeline-task-basics", "task", "task", "accepted", "Understand trip basics", "Mission facts and deterministic transcript are ready.", null, null, "task-basics", null, "decision-task-basics", trace.EvidenceReferences.FirstOrDefault(), "turn-03", MediaAsset("calm_morning"), trace.Code),
            new("timeline-task-itinerary", "task", "task", "active", "Compare itinerary choices", "Candidate cards are ready for select or defer.", null, null, "task-itinerary", null, "decision-choice-set", "evidence-chat-candidate", "turn-assistant-final", MediaAsset("reflective_culture"), "candidate_pool_ready"),
            new("timeline-task-approval", "task", "task", "not_started", "Approval gate", "Mock hold work is blocked until explicit approval.", null, null, "task-approval", null, "decision-approval-preview", "evidence-chat-approval", "turn-assistant-final", MediaAsset("logistics_transit"), ApprovalRequiredCode)
        };

        if (selected is not null)
        {
            items.Add(TimelineCandidateItem(selected, "selected"));
        }

        if (deferred is not null)
        {
            items.Add(TimelineCandidateItem(deferred, "deferred"));
        }

        return items;
    }

    private static IReadOnlyList<EndUserPlanningTimelineItem> TimelineFromState(
        EndUserChatState state,
        EndUserCandidateOption candidate,
        string stateName)
    {
        var timelineId = stateName == "selected" ? "timeline-selected-option" : "timeline-deferred-option";
        return AppendTimeline(
            state.PlanningTimeline.Where(item => item.TimelineId != timelineId).ToArray(),
            TimelineCandidateItem(candidate, stateName));
    }

    private static EndUserPlanningTimelineItem TimelineCandidateItem(
        EndUserCandidateOption candidate,
        string stateName)
    {
        var outcome = stateName == "selected" ? "choice_candidate_selected" : "choice_candidate_deferred";
        var timelineId = stateName == "selected" ? "timeline-selected-option" : "timeline-deferred-option";
        var title = stateName == "selected" ? $"Selected {candidate.Title}" : $"Deferred {candidate.Title}";
        return new(timelineId, "day", "candidate", stateName, title, candidate.Summary, "day-japan-02", "slot-itinerary-choice", null, candidate.CandidateId, $"decision-{candidate.CandidateId}", candidate.EvidenceIds.FirstOrDefault(), stateName == "selected" ? "turn-choice-selected" : "turn-choice-deferred", candidate.Media, outcome);
    }

    private static IReadOnlyList<EndUserPlanTrailItem> AppendPlanTrail(
        IReadOnlyList<EndUserPlanTrailItem> items,
        EndUserPlanTrailItem item) =>
        items.Concat([item]).ToArray();

    private static IReadOnlyList<EndUserPlanningTimelineItem> AppendTimeline(
        IReadOnlyList<EndUserPlanningTimelineItem> items,
        EndUserPlanningTimelineItem item) =>
        items.Concat([item]).ToArray();

    private static IReadOnlyList<EndUserPlanningTimelineItem> UpsertTimeline(
        IReadOnlyList<EndUserPlanningTimelineItem> items,
        EndUserPlanningTimelineItem item) =>
        items.Where(existing => existing.TimelineId != item.TimelineId).Concat([item]).ToArray();

    private static IReadOnlyList<EndUserTask> UpdateTask(
        IReadOnlyList<EndUserTask> tasks,
        string taskId,
        string state,
        int progress,
        string label) =>
        tasks.Select(task => task.TaskId == taskId
            ? task with
            {
                State = state,
                Progress = progress,
                StatusLabel = label,
                IsExpanded = true,
                Steps = task.Steps.Select(step => step with { State = state }).ToArray()
            }
            : task).ToArray();

    private static IReadOnlyList<EndUserChatTurn> AppendTurn(
        IReadOnlyList<EndUserChatTurn> turns,
        EndUserChatTurn turn) =>
        turns.Concat([turn]).ToArray();

    private static IReadOnlyList<EndUserChatTurn> UpsertTurn(
        IReadOnlyList<EndUserChatTurn> turns,
        EndUserChatTurn turn) =>
        turns.Where(existing => existing.TurnId != turn.TurnId).Concat([turn]).ToArray();

    private static EndUserChatState BlockWithFixedError(
        EndUserChatState state,
        string errorCode,
        string reason) =>
        state with
        {
            ComposerState = "blocked_harness",
            FinalState = "blocked",
            ErrorCode = errorCode,
            BlockedReason = reason,
            Turns = AppendTurn(state.Turns, new(
                "turn-fixed-block",
                "harness",
                "blocked",
                "blocked",
                "The request was blocked by a fixed UI safety code.",
                errorCode,
                null,
                errorCode))
        };

    private static IReadOnlyList<string> KnownCandidateIds() =>
    [
        CandidateClassic,
        CandidateCulture,
        CandidateScenic,
        CandidateTransit
    ];

    private static EndUserMediaAsset MediaAsset(string assetId)
    {
        var assets = MediaManifest();
        return assets.TryGetValue(assetId, out var asset)
            ? asset
            : assets["mood_placeholder"];
    }

    private static IReadOnlyDictionary<string, EndUserMediaAsset> MediaManifest() =>
        new Dictionary<string, EndUserMediaAsset>(StringComparer.Ordinal)
        {
            ["cultural_immersive"] = PromptStudioAsset("backdrop.cultural.sakura_temple.cultural_immersive", "cultural_immersive", "Sakura temple prompt-studio mood art.", "#f4a8b8"),
            ["scenic_relaxed"] = PromptStudioAsset("backdrop.scenic.fuji_lake.scenic_relaxed", "scenic_relaxed", "Fuji lake prompt-studio scenic mood art.", "#8ab7cb"),
            ["lively_food"] = PromptStudioAsset("backdrop.food.ramen_steam.food_cozy", "food_cozy", "Ramen steam prompt-studio food mood art.", "#d06d4c"),
            ["calm_morning"] = PromptStudioAsset("backdrop.logistics.map_planning.family_easy", "family_easy", "Gentle map-planning prompt-studio morning mood art.", "#f6d7a7"),
            ["reflective_culture"] = PromptStudioAsset("backdrop.cultural.vermilion_torii.spiritual_serene", "spiritual_serene", "Vermilion torii prompt-studio reflective culture art.", "#b14d3f"),
            ["soft_nature"] = PromptStudioAsset("backdrop.scenic.fuji_lake.scenic_relaxed", "scenic_relaxed", "Fuji lake prompt-studio soft nature art.", "#8ab7cb"),
            ["restorative_downtime"] = PromptStudioAsset("backdrop.scenic.onsen_valley.wellness_restorative", "wellness_restorative", "Onsen valley prompt-studio restorative mood art.", "#a9c6b2"),
            ["logistics_transit"] = PromptStudioAsset("backdrop.urban.station_grid.budget_practical", "budget_practical", "Station grid prompt-studio logistics art.", "#334a67"),
            ["mood_placeholder"] = PromptStudioAsset("backdrop.cultural.craft_district.arts_design", "arts_design", "Craft district prompt-studio fallback art.", "#c7a36b", "fallback"),
            ["missing_provider_media"] = PromptStudioAsset("backdrop.cultural.craft_district.arts_design", "arts_design", "Craft district prompt-studio fallback art.", "#c7a36b", "fallback")
        };

    private static EndUserMediaAsset PromptStudioAsset(
        string assetId,
        string mood,
        string alt,
        string dominantColor,
        string state = "ready") =>
        new(
            assetId,
            mood,
            $"/media/japan-prompt-studio-pack/{assetId}.png",
            alt,
            dominantColor,
            "prompt_studio_generated_local",
            "project-generated",
            "Generated locally by pch-prompt-studio for Progressive Commitment Harness Sprint 021.",
            state);

    private static EndUserChatScenario SelectScenario(string normalizedPrompt)
    {
        if (normalizedPrompt.Contains("block", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            return EndUserChatScenario.BlockedSafety;
        }

        if (normalizedPrompt.Contains("maybe", StringComparison.OrdinalIgnoreCase)
            || normalizedPrompt.Contains("confirm", StringComparison.OrdinalIgnoreCase))
        {
            return EndUserChatScenario.PendingConfirmation;
        }

        return EndUserChatScenario.HappyPath;
    }

    private static GoldenTurnTraceScript ScriptFor(EndUserChatScenario scenario) =>
        scenario == EndUserChatScenario.BlockedSafety
            ? GoldenTurnTraceRunner.BlockedSafetyScript
            : GoldenTurnTraceRunner.HappyPathScript;

    private static string StateFor(
        GoldenTurnTraceTurn turn,
        EndUserChatScenario scenario,
        GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked && turn.Kind == "blocked")
        {
            return "blocked";
        }

        if (scenario == EndUserChatScenario.PendingConfirmation && turn.Stage is "mission_intake" or "itinerary_compile")
        {
            return "pending";
        }

        return turn.Kind switch
        {
            "user" or "assistant" => "applied",
            "blocked" => "blocked",
            _ => trace.IsBlocked ? "blocked" : "applied"
        };
    }

    private static string FinalStateFor(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return "blocked";
        }

        return scenario == EndUserChatScenario.PendingConfirmation ? "pending" : "applied";
    }

    private static string? ErrorCodeFor(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return trace.Code;
        }

        return scenario == EndUserChatScenario.PendingConfirmation ? PendingConfirmationCode : null;
    }

    private static string NormalizePrompt(string prompt)
    {
        var trimmed = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt.Trim();
        return trimmed.Length <= 280 ? trimmed : trimmed[..280];
    }

    private static string PromptSummary(string prompt) =>
        $"Trip request accepted with {prompt.Length} characters. Raw prompt text is kept out of transcript storage.";

    private static string RoleStatusText(SanitizedModelRoleStatusEvalRow roleStatus) =>
        roleStatus.Passed
            ? "Offline deterministic model role is active; live provider roles are disabled for this run."
            : $"Model role posture blocked with {roleStatus.OutcomeCode}.";

    private static string FinalText(EndUserChatScenario scenario, GoldenTurnTraceResult trace)
    {
        if (trace.IsBlocked)
        {
            return "Blocked by the deterministic safety gate before any live provider or booking step.";
        }

        return scenario == EndUserChatScenario.PendingConfirmation
            ? "Pending confirmation before final itinerary and availability steps."
            : "Final deterministic trip plan is ready with canonical evidence markers.";
    }

    private static string ActiveRoleMarker(ModelRoleKind? role) =>
        role switch
        {
            ModelRoleKind.DeterministicOffline => "deterministic-offline",
            ModelRoleKind.SmallModel => "small-model",
            ModelRoleKind.StrongModel => "strong-model",
            ModelRoleKind.LiveProviderDisabled => "live-provider-disabled",
            _ => "none"
        };

    private static string ModeLabelFor(EndUserLiveModelSnapshot liveSnapshot) =>
        liveSnapshot.SelectedModelRole == EndUserModelRoleSelection.DeterministicOffline
            ? DeterministicModeLabel
            : liveSnapshot.IsLiveAccepted()
                ? "Live in-harness attached"
                : liveSnapshot.LivePreflightState == EndUserLiveModelTurnService.PreflightReady
                    ? "Live in-harness ready"
                    : "Live in-harness blocked";

    private static string ModeStateFor(EndUserLiveModelSnapshot liveSnapshot) =>
        liveSnapshot.SelectedModelRole == EndUserModelRoleSelection.DeterministicOffline
            ? DeterministicModeState
            : liveSnapshot.IsLiveAccepted()
                ? "live-model-attached"
                : liveSnapshot.LivePreflightState == EndUserLiveModelTurnService.PreflightReady
                    ? "live-model-ready"
                    : "live-model-blocked";

    private static bool IsLiveMode(string selectedModelRole) =>
        selectedModelRole != EndUserModelRoleSelection.DeterministicOffline;

    private enum EndUserChatScenario
    {
        HappyPath,
        BlockedSafety,
        PendingConfirmation
    }
}
