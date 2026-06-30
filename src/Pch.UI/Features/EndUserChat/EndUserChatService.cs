using Pch.Harness;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserChatService
{
    public const string DefaultPrompt = "Plan a calm family trip to Japan with one quiet day and no real bookings.";
    public const string ModeLabel = "Deterministic offline";
    public const string ModeState = "offline-deterministic";
    public const string RawAbsenceState = "verified";
    public const string ProviderOutcomeFallback = "deterministic_fallback_active";
    public const string ApprovalRequiredCode = "approval_required_preview";

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

    public EndUserChatState CreateInitialState()
    {
        return new(
            ModeLabel,
            ModeState,
            ModelRoleStatusEvaluator.OutcomeReady,
            ActiveRoleMarker(ModelRoleKind.DeterministicOffline),
            EndUserModelRoleSelection.DeterministicOffline,
            "none",
            EndUserLiveModelTurnService.PreflightDeterministic,
            EndUserLiveModelTurnService.LatestDeterministic,
            EndUserLiveModelTurnService.ProviderRequestNotAttempted,
            ProviderOutcomeFallback,
            "offline_ready",
            "credits_not_used",
            "none",
            "not_requested",
            RawAbsenceState,
            DefaultPrompt,
            "idle",
            "idle",
            null,
            null,
            [
                new(
                    "turn-system-ready",
                    "system",
                    "mode",
                    "ready",
                    "Deterministic offline mode is active. Live providers, bookings, holds, and payments are disabled for this run.",
                    ModeState,
                    null,
                    null),
                new(
                    "turn-assistant-start",
                    "assistant",
                    "guidance",
                    "ready",
                    "Tell me the trip you want to shape, then send it into the planner.",
                    null,
                    null,
                    null)
            ],
            InitialTasks(),
            null,
            null,
            null,
            FallbackNotice(),
            [],
            [],
            []);
    }

    public async Task<EndUserChatState> SendAsync(
        string prompt,
        string selectedModelRole = EndUserModelRoleSelection.DeterministicOffline,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = NormalizePrompt(prompt);
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        var liveSnapshot = await _liveModelTurnService
            .TryRunAsync(normalizedPrompt, normalizedRole, cancellationToken)
            .ConfigureAwait(false);
        var scenario = SelectScenario(normalizedPrompt);
        var trace = _traceRunner.Run(ScriptFor(scenario));
        var roleStatus = await EvaluateRoleStatus(cancellationToken).ConfigureAwait(false);
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

        return state with
        {
            ComposerState = "awaiting_user_input",
            FinalState = "candidate_selected",
            ChoiceSet = ChoiceSet("selected", candidateId, null),
            Tasks = UpdateTask(state.Tasks, "task-itinerary", "accepted", 72, "Candidate selected"),
            PlanTrail = PlanTrailFromState(state, candidate, "selected"),
            PlanningTimeline = TimelineFromState(state, candidate, "selected"),
            Turns = AppendTurn(state.Turns, new(
                "turn-choice-selected",
                "user",
                "choice",
                "selected",
                $"Selected {candidate.Title} for {candidate.Category}.",
                "choice_candidate_selected",
                "evidence-chat-candidate",
                null,
                candidate.CandidateId,
                candidate.Category))
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
            "Live provider calls are disabled for required smoke; the deterministic transcript remains available.",
            CanRetry: false,
            CanContinueDeterministic: true);

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
            ["cultural_immersive"] = new("cultural_immersive", "cultural_immersive", "/media/japan-card-pack/cultural-immersive.svg", "Abstract lanterns and temple lines for an immersive cultural Japan card.", "#25163f", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["scenic_relaxed"] = new("scenic_relaxed", "scenic_relaxed", "/media/japan-card-pack/scenic-relaxed.svg", "Mist, moss, sky, and coastline shapes for a relaxed scenic Japan card.", "#78b9a5", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["lively_food"] = new("lively_food", "lively_food", "/media/japan-card-pack/lively-food.svg", "Warm food market colors with lanterns and ceramic blue accents.", "#d94c3d", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["calm_morning"] = new("calm_morning", "calm_morning", "/media/japan-card-pack/calm-morning.svg", "Pale sun and soft green morning fields for a calm morning card.", "#f7ecd5", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["reflective_culture"] = new("reflective_culture", "reflective_culture", "/media/japan-card-pack/reflective-culture.svg", "Cherry, indigo, paper, and lantern glow for a reflective culture card.", "#182340", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["soft_nature"] = new("soft_nature", "soft_nature", "/media/japan-card-pack/soft-nature.svg", "Soft mountain, moss, and water shapes for a quiet nature card.", "#8fd3ca", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["restorative_downtime"] = new("restorative_downtime", "restorative_downtime", "/media/japan-card-pack/restorative-downtime.svg", "Lavender grey, warm wood, and bathhouse steam for restorative downtime.", "#d8d3e8", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["logistics_transit"] = new("logistics_transit", "logistics_transit", "/media/japan-card-pack/logistics-transit.svg", "Crisp transit linework with blue, charcoal, and signal green.", "#102235", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "ready"),
            ["mood_placeholder"] = new("mood_placeholder", "fallback", "/media/japan-card-pack/mood-placeholder.svg", "Deterministic fallback mood art used when media is missing.", "#f7efe1", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "fallback"),
            ["missing_provider_media"] = new("mood_placeholder", "fallback", "/media/japan-card-pack/mood-placeholder.svg", "Deterministic fallback mood art used when media is missing.", "#f7efe1", "generated_local", "project-generated", "Generated locally for Progressive Commitment Harness Sprint 017.", "fallback")
        };

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
            ? ModeLabel
            : "Live guarded with deterministic fallback";

    private static string ModeStateFor(EndUserLiveModelSnapshot liveSnapshot) =>
        liveSnapshot.SelectedModelRole == EndUserModelRoleSelection.DeterministicOffline
            ? ModeState
            : liveSnapshot.IsLiveAccepted()
                ? "live-model-attached"
                : "live-guarded-fallback";

    private enum EndUserChatScenario
    {
        HappyPath,
        BlockedSafety,
        PendingConfirmation
    }
}
