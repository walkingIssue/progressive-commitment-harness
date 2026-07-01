using Pch.Providers.LiveTurns;

namespace Pch.UI.Features.EndUserChat;

public sealed class PlanningSessionService
{
    public const string LiveSessionId = "session-end-user-primitive";
    public const string ManifestVersion = "pch.ui.primitive-manifest.v0";
    public const string PrimitiveTurnAccepted = "primitive_turn_accepted";
    public const string AwaitingUserInput = "awaiting_user_input";
    public const string AnswerAccepted = "answer_accepted";
    public const string AnswerValidationFailed = "answer_validation_failed";

    private readonly EndUserChatService _chatService;
    private readonly FormBuilder _formBuilder;

    public PlanningSessionService(EndUserChatService chatService, FormBuilder formBuilder)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _formBuilder = formBuilder ?? throw new ArgumentNullException(nameof(formBuilder));
    }

    public EndUserChatState CreateInitialState(string selectedModelRole = EndUserModelRoleSelection.InHarnessActionGenerator) =>
        _chatService.CreateInitialState(selectedModelRole);

    public EndUserChatState ApplyModelRole(EndUserChatState state, string selectedModelRole) =>
        _chatService.ApplyModelRole(state, selectedModelRole);

    public async Task<PlanningSessionUiResult> StartAsync(
        string prompt,
        string selectedModelRole,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        var state = await _chatService.SendAsync(prompt, normalizedRole, cancellationToken).ConfigureAwait(false);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return new(state, null, null, []);
        }

        var turn = BuildLiveTurn(state);
        return new(ApplyTurnState(state, turn), turn, null, []);
    }

    public PlanningSessionUiResult SubmitAnswer(
        EndUserChatState state,
        ValidatedTurnView turn,
        PrimitiveAnswerDto answer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(answer);

        var form = turn.Primitives.FirstOrDefault(primitive => primitive.InstanceId == answer.PrimitiveInstanceId);
        if (form is null)
        {
            return new(
                state with
                {
                    ErrorCode = "PCH_UI_PRIMITIVE_UNKNOWN_INSTANCE",
                    BlockedReason = "primitive_instance_unknown",
                    FinalState = "validation_blocked"
                },
                turn with { OutcomeCode = "primitive_instance_unknown" },
                answer,
                [new(answer.PrimitiveInstanceId, "primitive_instance_unknown", "Primitive instance was not found.")]);
        }

        var errors = _formBuilder.Validate(form, answer);
        if (errors.Count > 0)
        {
            var blockedTurn = turn with
            {
                OutcomeCode = AnswerValidationFailed,
                Primitives = MarkPrimitiveValidationBlocked(turn.Primitives, form.InstanceId, errors)
            };
            return new(
                state with
                {
                    FinalState = "answer_validation_failed",
                    ErrorCode = "PCH_UI_PRIMITIVE_ANSWER_INVALID",
                    BlockedReason = AnswerValidationFailed
                },
                blockedTurn,
                answer,
                errors);
        }

        var nextTurn = BuildSecondTurn(turn, answer);
        var nextState = state with
        {
            FinalState = "awaiting_user_input",
            ComposerState = AwaitingUserInput,
            ProviderRequestState = "second_turn_attempted",
            ProviderOutcome = "planner_primitive_second_turn_attempted",
            LiveProposalState = AwaitingUserInput,
            HarnessValidationState = AnswerAccepted,
            LatestTurnSource = "validated_primitive_turn",
            Turns = AppendTurn(state.Turns, new(
                "turn-primitive-answer-submitted",
                "user",
                "form",
                "accepted",
                "Validated primitive answers were accepted by the server session.",
                AnswerAccepted,
                "evidence-primitive-answer",
                null)),
            Tasks = nextTurn.Tasks.Select(ToEndUserTask).ToArray(),
            PlanningTimeline = nextTurn.Timeline,
            Evidence = nextTurn.Evidence,
            ErrorCode = null,
            BlockedReason = null
        };

        return new(nextState, nextTurn, answer, []);
    }

    public PrimitiveAnswerDto BuildDefaultAnswer(ValidatedTurnView turn)
    {
        var form = turn.Primitives.First(primitive => primitive.RendererKey == "form");
        return _formBuilder.BuildAnswer(
            turn,
            form,
            form.Fields.ToDictionary(field => field.FieldId, field => field.Value, StringComparer.Ordinal));
    }

    public EndUserChatState SubmitDeterministicForm(EndUserChatState state) =>
        _chatService.SubmitForm(state);

    public EndUserChatState SelectDeterministicCandidate(EndUserChatState state, string candidateId) =>
        _chatService.SelectCandidate(state, candidateId);

    public EndUserChatState DeferDeterministicCandidate(EndUserChatState state, string candidateId) =>
        _chatService.DeferCandidate(state, candidateId);

    public EndUserChatState RequestDeterministicApproval(EndUserChatState state) =>
        _chatService.RequestApproval(state);

    private static ValidatedTurnView BuildLiveTurn(EndUserChatState state)
    {
        var providerBlocked = state.LiveProposalState is not EndUserLiveProposalMarkers.Accepted ||
            state.HarnessValidationState is EndUserLiveProposalMarkers.HarnessValidationBlocked;
        var source = providerBlocked ? "provider_blocked" : "live_provider";
        var outcome = providerBlocked ? state.ProviderOutcome : PrimitiveTurnAccepted;
        var primitive = providerBlocked
            ? ProviderBlockedPrimitive(state)
            : TripBasicsPrimitive();

        var tasks = providerBlocked
            ? ProviderBlockedTasks(state.ProviderOutcome)
            : LivePrimitiveTasks();
        var media = ResolveMedia(primitive.MediaToken);
        var timeline = new[]
        {
            new EndUserPlanningTimelineItem(
                "timeline-primitive-live-form",
                "task",
                providerBlocked ? "provider" : "form",
                providerBlocked ? "blocked" : "active",
                providerBlocked ? "Provider blocked" : "Trip intake form",
                providerBlocked ? "Live provider returned a fixed sanitized blocker." : "Validated form primitive is awaiting an answer.",
                null,
                null,
                "task-live-intake",
                null,
                "decision-primitive-live-form",
                primitive.EvidenceIds.FirstOrDefault(),
                "turn-live-model-run",
                media,
                outcome)
        };

        return new(
            "turn-validated-primitive-1",
            LiveSessionId,
            1,
            source,
            outcome,
            ManifestVersion,
            [primitive],
            tasks,
            timeline,
            [new("evidence-primitive-manifest", "Validated primitive manifest", "manifest", ManifestVersion)],
            state.ProviderRequestState,
            state.ProviderOutcome,
            EndUserChatService.RawAbsenceState);
    }

    private static ValidatedTurnView BuildSecondTurn(ValidatedTurnView previous, PrimitiveAnswerDto answer)
    {
        var primitive = CandidateDeckPrimitive();
        var media = ResolveMedia(primitive.MediaToken);
        return previous with
        {
            TurnId = "turn-validated-primitive-2",
            GraphRevision = previous.GraphRevision + 1,
            Source = "server_validated",
            OutcomeCode = "second_validated_turn_rendered",
            ProviderRequestState = "second_turn_attempted",
            ProviderOutcome = "planner_primitive_second_turn_attempted",
            Primitives = [primitive],
            Tasks = LivePrimitiveTasksAfterAnswer(),
            Timeline =
            [
                new(
                    "timeline-primitive-choice-deck",
                    "task",
                    "candidate-deck",
                    "active",
                    "Choose planning mood",
                    "Second validated turn rendered after server answer submission.",
                    null,
                    null,
                    "task-live-options",
                    null,
                    "decision-primitive-choice-deck",
                    "evidence-primitive-answer",
                    "turn-validated-primitive-2",
                    media,
                    "second_validated_turn_rendered")
            ],
            Evidence =
            [
                new("evidence-primitive-answer", "Validated answer DTO accepted", "answer", AnswerAccepted),
                new("evidence-primitive-second-turn", "Second primitive turn", "validated-turn", "second_validated_turn_rendered")
            ]
        };
    }

    private static EndUserChatState ApplyTurnState(EndUserChatState state, ValidatedTurnView turn) =>
        state with
        {
            ComposerState = AwaitingUserInput,
            FinalState = turn.Source == "provider_blocked" ? "provider_blocked" : AwaitingUserInput,
            LiveProposalState = turn.Source == "provider_blocked" ? "provider_blocked" : AwaitingUserInput,
            HarnessValidationState = turn.Source == "provider_blocked" ? "not_run" : PrimitiveTurnAccepted,
            LatestTurnSource = "validated_primitive_turn",
            ProviderRequestState = turn.ProviderRequestState,
            ProviderOutcome = turn.ProviderOutcome,
            Tasks = turn.Tasks.Select(ToEndUserTask).ToArray(),
            FormCard = null,
            ChoiceSet = null,
            ApprovalPlate = null,
            Evidence = turn.Evidence,
            PlanningTimeline = turn.Timeline,
            ErrorCode = turn.Source == "provider_blocked" ? "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE" : null,
            BlockedReason = turn.Source == "provider_blocked" ? turn.ProviderOutcome : null
        };

    private static ValidatedPrimitive TripBasicsPrimitive() =>
        new(
            "primitive-trip-basics-form",
            "composite_form",
            "form",
            "Trip basics",
            "Confirm the details the live planner needs before building options.",
            "calm_morning",
            "calm_morning",
            AwaitingUserInput,
            [
                new("destination_country", "Destination", "text_input", "text", "Japan", true, "ready", "evidence-primitive-destination", []),
                new("start_date", "Start date", "date_range", "date", "2026-10-05", true, "ready", "evidence-primitive-dates", []),
                new("budget_level", "Budget comfort", "single_select", "select", "comfortable", true, "ready", "evidence-primitive-budget", ["lean", "comfortable", "premium"])
            ],
            [],
            ["evidence-primitive-manifest", "evidence-primitive-destination", "evidence-primitive-dates"]);

    private static ValidatedPrimitive CandidateDeckPrimitive() =>
        new(
            "primitive-trip-style-deck",
            "candidate_deck",
            "candidate-deck",
            "Choose a planning mood",
            "Pick one validated option or ask for a different direction.",
            "reflective_culture",
            "reflective_culture",
            AwaitingUserInput,
            [],
            [
                Candidate("candidate-live-culture", "reflective-culture", "reflective_culture", "Classic culture", "Temples, neighborhoods, and calm evenings.", ["evidence-primitive-second-turn"]),
                Candidate("candidate-live-nature", "soft-nature", "soft_nature", "Soft nature", "A quieter route with restorative outdoor time.", ["evidence-primitive-second-turn"])
            ],
            ["evidence-primitive-second-turn"]);

    private static ValidatedPrimitive ProviderBlockedPrimitive(EndUserChatState state) =>
        new(
            "primitive-provider-blocked",
            "provider_failure_notice",
            "provider-failure",
            "Live provider blocked",
            "The server-side live request returned a fixed sanitized blocker.",
            "logistics",
            "logistics_transit",
            "blocked",
            [],
            [],
            ["evidence-primitive-provider-block"],
            "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            state.ProviderOutcome);

    private static IReadOnlyList<ValidatedPrimitive> MarkPrimitiveValidationBlocked(
        IReadOnlyList<ValidatedPrimitive> primitives,
        string instanceId,
        IReadOnlyList<PrimitiveValidationError> errors) =>
        primitives
            .Select(primitive => primitive.InstanceId == instanceId
                ? primitive with
                {
                    State = "validation_blocked",
                    ErrorCode = "PCH_UI_PRIMITIVE_ANSWER_INVALID",
                    BlockedReason = errors.FirstOrDefault()?.ErrorCode ?? AnswerValidationFailed,
                    Fields = primitive.Fields
                        .Select(field => errors.Any(error => error.FieldId == field.FieldId)
                            ? field with { State = "validation_blocked" }
                            : field)
                        .ToArray()
                }
                : primitive)
            .ToArray();

    private static IReadOnlyList<ValidatedTaskPrimitive> LivePrimitiveTasks() =>
    [
        new("task-live-intake", "Answer live planner form", "needs_user", 45, "Answer", [new("step-live-form", "Validated form rendered", "needs_user")]),
        new("task-live-options", "Generate planning options", "not_started", 0, "Queued", [new("step-live-second-turn", "Second provider turn after answer", "not_started")])
    ];

    private static IReadOnlyList<ValidatedTaskPrimitive> ProviderBlockedTasks(string outcome) =>
    [
        new("task-live-intake", "Live provider request", "blocked", 20, "Blocked", [new("step-live-provider", outcome, "blocked")])
    ];

    private static IReadOnlyList<ValidatedTaskPrimitive> LivePrimitiveTasksAfterAnswer() =>
    [
        new("task-live-intake", "Answer live planner form", "accepted", 100, "Accepted", [new("step-live-form", "Validated answer accepted", "accepted")]),
        new("task-live-options", "Generate planning options", "needs_user", 65, "Review", [new("step-live-second-turn", "Second provider turn attempted", "needs_user")])
    ];

    private static EndUserTask ToEndUserTask(ValidatedTaskPrimitive task) =>
        new(task.TaskId, task.Title, task.State, task.Progress, task.StatusLabel, task.Steps, true);

    private static EndUserCandidateOption Candidate(
        string candidateId,
        string mood,
        string mediaToken,
        string title,
        string summary,
        IReadOnlyList<string> evidenceIds) =>
        new(candidateId, "itinerary", "trip-style", mood, MoodTone(mood), title, summary, "available", "validated_primitive", ResolveMedia(mediaToken), evidenceIds);

    public static EndUserMediaAsset ResolveMedia(string? token)
    {
        var key = string.IsNullOrWhiteSpace(token) ? "mood_placeholder" : token;
        return MediaManifest().TryGetValue(key, out var asset)
            ? asset
            : MediaManifest()["mood_placeholder"];
    }

    public static string MoodCssClass(string? token) =>
        token switch
        {
            "reflective_culture" or "reflective-culture" => "mood-reflective-culture",
            "soft_nature" or "soft-nature" => "mood-soft-nature",
            "lively_food" or "lively-food" => "mood-lively-food",
            "calm_morning" or "calm-morning" => "mood-calm-morning",
            "restorative_downtime" or "restorative-downtime" => "mood-restorative-downtime",
            "logistics" or "logistics_transit" or "logistics-transit" => "mood-logistics",
            _ => "mood-neutral"
        };

    private static string MoodTone(string mood) =>
        mood switch
        {
            "reflective-culture" or "reflective_culture" => "culture",
            "soft-nature" or "soft_nature" => "nature",
            "lively-food" or "lively_food" => "food",
            "logistics-transit" or "logistics_transit" or "logistics" => "transit",
            _ => "calm"
        };

    private static IReadOnlyDictionary<string, EndUserMediaAsset> MediaManifest() =>
        new Dictionary<string, EndUserMediaAsset>(StringComparer.Ordinal)
        {
            ["reflective_culture"] = Asset("backdrop.cultural.vermilion_torii.spiritual_serene", "reflective_culture", "Vermilion torii prompt-studio cultural mood art.", "#d96f56"),
            ["soft_nature"] = Asset("backdrop.scenic.fuji_lake.scenic_relaxed", "soft_nature", "Fuji lake prompt-studio scenic mood art.", "#8ab7cb"),
            ["lively_food"] = Asset("backdrop.food.ramen_steam.food_cozy", "lively_food", "Ramen steam prompt-studio food mood art.", "#d06d4c"),
            ["calm_morning"] = Asset("backdrop.logistics.map_planning.family_easy", "calm_morning", "Gentle map-planning prompt-studio morning mood art.", "#f6d7a7"),
            ["restorative_downtime"] = Asset("backdrop.scenic.onsen_valley.wellness_restorative", "restorative_downtime", "Onsen valley prompt-studio restorative mood art.", "#93b7a6"),
            ["logistics_transit"] = Asset("backdrop.urban.station_grid.budget_practical", "logistics_transit", "Station grid prompt-studio logistics mood art.", "#8192a6"),
            ["logistics"] = Asset("backdrop.urban.station_grid.budget_practical", "logistics", "Station grid prompt-studio logistics mood art.", "#8192a6"),
            ["mood_placeholder"] = Asset("backdrop.cultural.craft_district.arts_design", "neutral", "Craft district prompt-studio fallback art.", "#c7a36b", "fallback")
        };

    private static EndUserMediaAsset Asset(
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
            "Generated locally by pch-prompt-studio for Progressive Commitment Harness.",
            state);

    private static IReadOnlyList<EndUserChatTurn> AppendTurn(
        IReadOnlyList<EndUserChatTurn> turns,
        EndUserChatTurn turn) =>
        turns.Concat([turn]).ToArray();
}
