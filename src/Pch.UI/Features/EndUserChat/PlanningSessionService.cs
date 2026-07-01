using System.Security.Cryptography;
using System.Text;
using Pch.Core;
using Pch.Harness;
using Pch.Providers.Errors;
using Pch.Providers.LivePreflight;
using Pch.Providers.LiveTurns;
using Pch.Providers.PlannerPrimitives;

namespace Pch.UI.Features.EndUserChat;

public delegate Task<PlannerModelResult> PlannerPrimitiveModelRunner(
    PlannerModelRequest request,
    PlannerModelOptions options,
    CancellationToken cancellationToken);

public sealed class PlanningSessionService
{
    public const string LiveSessionId = "session-end-user-primitive";
    public const string PrimitiveTurnAccepted = PlannerPrimitiveValidator.AcceptedCode;
    public const string AwaitingUserInput = PlannerPrimitiveValidator.AwaitingUserInputCode;
    public const string AnswerAccepted = "answer_accepted";
    public const string AnswerValidationFailed = "answer_validation_failed";

    private readonly EndUserChatService _chatService;
    private readonly FormBuilder _formBuilder;
    private readonly Func<IReadOnlyDictionary<string, string?>> _environment;
    private readonly PlannerPrimitiveModelRunner? _plannerRunner;
    private readonly PlannerToolManifestCompiler _manifestCompiler = new();
    private readonly PlannerPrimitiveValidator _validator = new();

    public PlanningSessionService(
        EndUserChatService chatService,
        FormBuilder formBuilder,
        Func<IReadOnlyDictionary<string, string?>>? environment = null,
        PlannerPrimitiveModelRunner? plannerRunner = null)
    {
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _formBuilder = formBuilder ?? throw new ArgumentNullException(nameof(formBuilder));
        _environment = environment ?? (() => new Dictionary<string, string?>(StringComparer.Ordinal));
        _plannerRunner = plannerRunner;
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

        var session = CreatePrimitiveSession();
        var manifest = _manifestCompiler.Compile(session, HarnessStage.Intake);
        var model = await RunPlannerModelAsync(
            manifest,
            prompt,
            "run-end-user-planner-primitive",
            "turn-end-user-planner-primitive",
            "attempted",
            cancellationToken).ConfigureAwait(false);
        var canonical = model.Result is null
            ? ProviderBlockedTurn(manifest, model.ProviderOutcome)
            : ValidateModelResult(session, manifest, model.Result);
        var turn = ProjectTurn(canonical, manifest, model.ProviderRequestState, model.ProviderOutcome);

        return new(ApplyTurnState(state, turn), turn, null, []);
    }

    public async Task<PlanningSessionUiResult> SubmitAnswer(
        EndUserChatState state,
        EndUserValidatedTurnView turn,
        PrimitiveAnswerDto answer,
        CancellationToken cancellationToken = default)
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

        var session = CreatePrimitiveSession();
        var manifest = _manifestCompiler.Compile(session, HarnessStage.Intake);
        var model = await RunPlannerModelAsync(
            manifest,
            SecondTurnPrompt(answer),
            "run-end-user-planner-primitive-followup",
            "turn-end-user-planner-primitive-followup",
            "second_turn_attempted",
            cancellationToken).ConfigureAwait(false);
        var canonical = model.Result is null
            ? ProviderBlockedTurn(manifest, model.ProviderOutcome)
            : ValidateModelResult(session, manifest, model.Result);
        var nextTurn = ProjectTurn(canonical, manifest, model.ProviderRequestState, model.ProviderOutcome);
        var answeredState = state with
        {
            Turns = AppendTurn(state.Turns, new(
                "turn-primitive-answer-submitted",
                "user",
                "form",
                "accepted",
                "Validated primitive answers were accepted by the server session.",
                AnswerAccepted,
                "evidence-primitive-answer",
                null))
        };
        var nextState = ApplyTurnState(answeredState, nextTurn) with
        {
            HarnessValidationState = nextTurn.Source == "provider_blocked" ? "not_run" : nextTurn.OutcomeCode,
            LatestTurnSource = nextTurn.Source == "provider_blocked" ? "provider_blocked" : "validated_primitive_turn",
            Turns = AppendTurn(answeredState.Turns, new(
                "turn-live-model-followup",
                "assistant",
                "live-model-followup",
                nextTurn.Source == "provider_blocked" ? "blocked" : "awaiting_user_input",
                nextTurn.Source == "provider_blocked"
                    ? "The follow-up live planner turn returned a fixed sanitized blocker."
                    : "The follow-up live planner turn rendered validated primitives.",
                nextTurn.ProviderOutcome,
                nextTurn.Evidence.FirstOrDefault()?.EvidenceId,
                null))
        };

        return new(nextState, nextTurn, answer, []);
    }

    public PrimitiveAnswerDto BuildDefaultAnswer(EndUserValidatedTurnView turn)
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

    private static string SecondTurnPrompt(PrimitiveAnswerDto answer)
    {
        var fieldList = string.Join(
            ",",
            answer.FieldValues.Keys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .Take(12));
        return $"Continue from the validated primitive answer. Use only trusted server state and field ids: {fieldList}.";
    }

    private async Task<PlannerModelRun> RunPlannerModelAsync(
        PlannerToolManifest manifest,
        string prompt,
        string runId,
        string turnId,
        string attemptedState,
        CancellationToken cancellationToken)
    {
        var environment = WithKeyFileAvailability(_environment());
        var options = PlannerModelOptions.FromEnvironment(environment);
        if (_plannerRunner is null)
        {
            return new("not_attempted", PlannerPrimitiveRunner.OutcomeProviderUnavailable, null);
        }

        var request = new PlannerModelRequest(
            RunId: runId,
            TurnId: turnId,
            Manifest: ToProviderMirror(manifest),
            Locale: "en-US",
            RuntimePrompt: prompt,
            PromptDigest: PromptDigest(prompt));

        try
        {
            var result = await _plannerRunner(request, options, cancellationToken).ConfigureAwait(false);
            return new(attemptedState, PlannerPrimitiveRunner.OutcomeAccepted, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(attemptedState, OutcomeForException(ex), null);
        }
    }

    private Pch.Core.ValidatedTurnView ValidateModelResult(
        TripSession session,
        PlannerToolManifest manifest,
        PlannerModelResult result)
    {
        var proposal = new PlannerPrimitiveTurnProposal(
            ProposalId: "proposal-end-user-planner-primitive",
            ManifestId: result.ManifestId,
            SchemaVersion: manifest.SchemaVersion,
            GraphRevision: result.GraphRevision,
            SessionId: result.SessionId,
            Stage: manifest.Stage,
            Primitives: result.Primitives.Select(primitive => ToHarnessPrimitive(manifest, primitive)).ToArray());

        return _validator.Validate(session, manifest, proposal).View!;
    }

    private static PlannerPrimitiveInstance ToHarnessPrimitive(
        PlannerToolManifest manifest,
        PlannerPrimitiveInvocation primitive)
    {
        var definition = manifest.AllowedPrimitives.FirstOrDefault(item => string.Equals(item.PrimitiveId, primitive.PrimitiveId, StringComparison.Ordinal))
            ?? manifest.AllowedPrimitives.First(item => item.PrimitiveId == PlannerPrimitiveIds.AssistantMessage);
        var fieldPath = string.IsNullOrWhiteSpace(primitive.FieldPath) ? null : primitive.FieldPath;

        var candidateId = primitive.PrimitiveId is PlannerPrimitiveIds.CandidateDeck
                or PlannerPrimitiveIds.SingleSelect
                or PlannerPrimitiveIds.MultiSelect
                or PlannerPrimitiveIds.RankedChoice
            ? primitive.CandidateIds.FirstOrDefault(id => manifest.AllowedCandidateIds.Contains(id, StringComparer.Ordinal))
            : null;
        var safeMoodToken = SafeMoodToken(definition, manifest, primitive.MoodToken);

        return new(
            InstanceId: FixedInstanceId(definition.PrimitiveId, fieldPath),
            PrimitiveId: primitive.PrimitiveId,
            SchemaVersion: manifest.SchemaVersion,
            RendererKey: definition.RendererKey,
            Label: FixedLabel(definition.PrimitiveId),
            Prompt: FixedPrompt(definition.PrimitiveId),
            FieldPath: fieldPath,
            SlotId: null,
            CandidateId: candidateId,
            TaskId: null,
            MoodToken: safeMoodToken,
            MediaToken: definition.SupportsMedia && !string.IsNullOrWhiteSpace(safeMoodToken) ? $"media:{safeMoodToken}" : null,
            AnswerSchema: definition.AnswerSchema,
            Answers: DefaultAnswers(definition.AnswerSchema, fieldPath, primitive.CandidateIds),
            EvidenceReferences: ["evidence-planner-primitive"],
            DependencyReferences: []);
    }

    private static string FixedInstanceId(string primitiveId, string? fieldPath) =>
        fieldPath switch
        {
            "/mission/destination_country" => "primitive-destination-country",
            "/mission/start_date" => "primitive-trip-dates",
            "/mission/end_date" => "primitive-trip-dates",
            "/mission/purpose" => "primitive-trip-purpose",
            "/constraints/pace" => "primitive-pace-constraint",
            "/constraints/budget" => "primitive-budget-constraint",
            _ => primitiveId switch
            {
                PlannerPrimitiveIds.DateRange => "primitive-trip-dates",
                PlannerPrimitiveIds.ConfirmationQuestion => "primitive-confirmation",
                PlannerPrimitiveIds.ToolSearchRequest => "primitive-tool-search-request",
                PlannerPrimitiveIds.ToolGapRequest => "primitive-tool-gap-request",
                PlannerPrimitiveIds.AssistantMessage => "primitive-assistant-message",
                _ => "primitive-trip-detail"
            }
        };

    private static string? SafeMoodToken(
        Pch.Core.PlannerPrimitiveDefinition definition,
        PlannerToolManifest manifest,
        string? moodToken)
    {
        if (!definition.SupportsMood)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(moodToken) &&
            manifest.AllowedMoodTokens.Contains(moodToken, StringComparer.Ordinal)
                ? moodToken
                : PlannerMoodTokens.CalmMorning;
    }

    private static string FixedLabel(string primitiveId) =>
        primitiveId switch
        {
            PlannerPrimitiveIds.DateRange => "Dates",
            PlannerPrimitiveIds.ConfirmationQuestion => "Confirmation",
            PlannerPrimitiveIds.ToolSearchRequest => "Tool search",
            PlannerPrimitiveIds.ToolGapRequest => "Tool gap",
            _ => "Trip detail"
        };

    private static string FixedPrompt(string primitiveId) =>
        primitiveId switch
        {
            PlannerPrimitiveIds.DateRange => "Confirm travel dates.",
            PlannerPrimitiveIds.ConfirmationQuestion => "Confirm this planning detail.",
            PlannerPrimitiveIds.ToolSearchRequest => "Planner requested a tool search.",
            PlannerPrimitiveIds.ToolGapRequest => "Planner reported a tool gap.",
            _ => "Confirm this trip planning detail."
        };

    private static IReadOnlyDictionary<string, string?> DefaultAnswers(
        PlannerAnswerSchema schema,
        string? fieldPath,
        IReadOnlyList<string> candidateIds)
    {
        return schema.Kind switch
        {
            PlannerAnswerSchemaKinds.Text => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = fieldPath switch
                {
                    "/mission/destination_country" => "Japan",
                    "/constraints/pace" => "balanced",
                    "/constraints/budget" => "comfortable",
                    _ => "trip-planning"
                }
            },
            PlannerAnswerSchemaKinds.DateRange => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["start"] = "2027-04-01",
                ["end"] = "2027-04-07"
            },
            PlannerAnswerSchemaKinds.Confirmation => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = schema.Options.FirstOrDefault() ?? "confirm"
            },
            PlannerAnswerSchemaKinds.SingleSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["selected"] = candidateIds.FirstOrDefault() ?? "selected"
            },
            PlannerAnswerSchemaKinds.MultiSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["selected"] = candidateIds.FirstOrDefault() ?? "selected"
            },
            PlannerAnswerSchemaKinds.RankedChoice => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ranked"] = candidateIds.FirstOrDefault() ?? "ranked"
            },
            _ => new Dictionary<string, string?>(StringComparer.Ordinal)
        };
    }

    private static Pch.Core.ValidatedTurnView ProviderBlockedTurn(PlannerToolManifest manifest, string providerOutcome) =>
        new(
            TurnId: "validated-turn-provider-blocked",
            SessionId: manifest.SessionId,
            GraphRevision: manifest.GraphRevision,
            Source: "provider_blocked",
            Code: providerOutcome,
            Primitives:
            [
                new(
                    InstanceId: "primitive-provider-blocked",
                    PrimitiveId: PlannerPrimitiveIds.AssistantMessage,
                    RendererKey: "provider-failure",
                    Label: "Live provider blocked",
                    Prompt: "The server-side live request returned a fixed sanitized blocker.",
                    FieldPath: null,
                    SlotId: null,
                    CandidateId: null,
                    TaskId: "task-live-intake",
                    MoodToken: PlannerMoodTokens.Logistics,
                    MediaToken: null,
                    AnswerSchema: new PlannerAnswerSchema(PlannerAnswerSchemaKinds.None, false, null, null, []),
                    Answers: new Dictionary<string, string?>(StringComparer.Ordinal),
                    EvidenceReferences: ["evidence-primitive-provider-block"])
            ],
            TaskRailItemRefs: ["task-live-intake"],
            TimelineAnchorRefs: [],
            EvidenceReferences: ["evidence-primitive-provider-block"],
            SanitizationStatus: "sanitized");

    private static EndUserValidatedTurnView ProjectTurn(
        Pch.Core.ValidatedTurnView canonical,
        PlannerToolManifest manifest,
        string providerRequestState,
        string providerOutcome)
    {
        var isProviderBlocked = string.Equals(canonical.Source, "provider_blocked", StringComparison.Ordinal);
        var primitives = isProviderBlocked || canonical.Primitives.Count == 0
            ? [ProviderBlockedPrimitive(providerOutcome)]
            : ProjectPrimitives(canonical);
        var tasks = canonical.TaskRailItemRefs.Count > 0
            ? canonical.TaskRailItemRefs.Select(TaskFromRef).ToArray()
            : (isProviderBlocked ? ProviderBlockedTasks(providerOutcome) : LivePrimitiveTasks());
        var firstPrimitive = primitives.First();
        var media = ResolveMedia(firstPrimitive.MediaToken);

        return new(
            TurnId: canonical.TurnId,
            SessionId: canonical.SessionId,
            GraphRevision: canonical.GraphRevision,
            Source: canonical.Source,
            OutcomeCode: canonical.Code,
            ManifestVersion: manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Primitives: primitives,
            Tasks: tasks,
            Timeline:
            [
                new(
                    "timeline-primitive-live-form",
                    "task",
                    isProviderBlocked ? "provider" : "form",
                    isProviderBlocked ? "blocked" : "active",
                    isProviderBlocked ? "Provider blocked" : "Trip intake form",
                    isProviderBlocked ? "Live provider returned a fixed sanitized blocker." : "Validated primitive is awaiting an answer.",
                    null,
                    null,
                    "task-live-intake",
                    null,
                    "decision-primitive-live-form",
                    canonical.EvidenceReferences.FirstOrDefault(),
                    canonical.TurnId,
                    media,
                    canonical.Code)
            ],
            Evidence: canonical.EvidenceReferences.Count > 0
                ? canonical.EvidenceReferences.Select(id => new EndUserEvidenceItem(id, "Validated primitive evidence", "validated-turn", canonical.Code)).ToArray()
                : [new("evidence-primitive-manifest", "Validated primitive manifest", "manifest", manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            ProviderRequestState: providerRequestState,
            ProviderOutcome: providerOutcome,
            RawAbsenceState: EndUserChatService.RawAbsenceState,
            CanonicalTurn: canonical);
    }

    private static IReadOnlyList<ValidatedPrimitive> ProjectPrimitives(Pch.Core.ValidatedTurnView canonical)
    {
        var formViews = canonical.Primitives
            .Where(primitive => primitive.AnswerSchema.Required && primitive.PrimitiveId != PlannerPrimitiveIds.CandidateDeck)
            .ToArray();
        if (formViews.Length > 0)
        {
            return [ProjectForm(formViews, canonical)];
        }

        var deck = canonical.Primitives.FirstOrDefault(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.CandidateDeck);
        return deck is null
            ? [ProjectMessage(canonical.Primitives.First(), canonical)]
            : [ProjectDeck(deck, canonical)];
    }

    private static ValidatedPrimitive ProjectForm(
        IReadOnlyList<ValidatedPrimitiveView> primitives,
        Pch.Core.ValidatedTurnView canonical)
    {
        return new(
            InstanceId: "primitive-trip-basics-form",
            PrimitiveId: "composite_form",
            RendererKey: "form",
            Title: "Trip basics",
            Prompt: "Confirm the details the live planner needs before building options.",
            MoodToken: PlannerMoodTokens.CalmMorning,
            MediaToken: PlannerMoodTokens.CalmMorning,
            State: AwaitingUserInput,
            Fields: primitives.Select(ProjectField).ToArray(),
            Candidates: [],
            EvidenceIds: canonical.EvidenceReferences.Count > 0 ? canonical.EvidenceReferences : ["evidence-primitive-manifest"]);
    }

    private static ValidatedPrimitiveField ProjectField(ValidatedPrimitiveView primitive)
    {
        var fieldId = FieldId(primitive);
        return new(
            FieldId: fieldId,
            Label: primitive.Label ?? LabelFor(fieldId),
            PrimitiveId: primitive.PrimitiveId,
            RendererKey: FieldRenderer(primitive),
            Value: FieldValue(primitive),
            IsRequired: primitive.AnswerSchema.Required,
            State: "ready",
            EvidenceId: primitive.EvidenceReferences.FirstOrDefault(),
            AllowedValues: primitive.AnswerSchema.Options);
    }

    private static ValidatedPrimitive ProjectMessage(ValidatedPrimitiveView primitive, Pch.Core.ValidatedTurnView canonical) =>
        new(
            primitive.InstanceId,
            primitive.PrimitiveId,
            "provider-failure",
            primitive.Label ?? "Live planner update",
            primitive.Prompt ?? "The live planner returned a sanitized update.",
            primitive.MoodToken ?? PlannerMoodTokens.Logistics,
            primitive.MediaToken ?? "logistics_transit",
            canonical.Code,
            [],
            [],
            primitive.EvidenceReferences.Count > 0 ? primitive.EvidenceReferences : canonical.EvidenceReferences,
            canonical.Code,
            canonical.Code);

    private static ValidatedPrimitive ProjectDeck(ValidatedPrimitiveView primitive, Pch.Core.ValidatedTurnView canonical) =>
        new(
            primitive.InstanceId,
            primitive.PrimitiveId,
            "candidate-deck",
            primitive.Label ?? "Choose a planning mood",
            primitive.Prompt ?? "Pick one validated option or ask for a different direction.",
            primitive.MoodToken ?? PlannerMoodTokens.ReflectiveCulture,
            primitive.MediaToken ?? PlannerMoodTokens.ReflectiveCulture,
            AwaitingUserInput,
            [],
            [
                Candidate("candidate-live-culture", "reflective-culture", PlannerMoodTokens.ReflectiveCulture, "Classic culture", "Temples, neighborhoods, and calm evenings.", primitive.EvidenceReferences),
                Candidate("candidate-live-nature", "soft-nature", PlannerMoodTokens.SoftNature, "Soft nature", "A quieter route with restorative outdoor time.", primitive.EvidenceReferences)
            ],
            primitive.EvidenceReferences.Count > 0 ? primitive.EvidenceReferences : canonical.EvidenceReferences);

    private static string FieldId(ValidatedPrimitiveView primitive) =>
        primitive.FieldPath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? primitive.InstanceId;

    private static string LabelFor(string fieldId) =>
        fieldId.Replace('_', ' ');

    private static string FieldRenderer(ValidatedPrimitiveView primitive) =>
        primitive.AnswerSchema.Kind switch
        {
            PlannerAnswerSchemaKinds.DateRange => "date",
            PlannerAnswerSchemaKinds.SingleSelect or PlannerAnswerSchemaKinds.Confirmation => "select",
            _ => "text"
        };

    private static string FieldValue(ValidatedPrimitiveView primitive) =>
        primitive.AnswerSchema.Kind switch
        {
            PlannerAnswerSchemaKinds.DateRange => primitive.Answers.TryGetValue("start", out var start) ? start ?? string.Empty : string.Empty,
            PlannerAnswerSchemaKinds.SingleSelect => primitive.Answers.TryGetValue("selected", out var selected) ? selected ?? string.Empty : string.Empty,
            PlannerAnswerSchemaKinds.Confirmation => primitive.Answers.TryGetValue("value", out var value) ? value ?? string.Empty : string.Empty,
            _ => primitive.Answers.TryGetValue("value", out var text) ? text ?? string.Empty : string.Empty
        };

    private static EndUserChatState ApplyTurnState(EndUserChatState state, EndUserValidatedTurnView turn) =>
        state with
        {
            ComposerState = AwaitingUserInput,
            FinalState = turn.Source == "provider_blocked" || turn.Primitives.Any(primitive => primitive.RendererKey == "provider-failure")
                ? "provider_blocked"
                : AwaitingUserInput,
            LiveProposalState = turn.Source == "provider_blocked" ? "provider_blocked" : AwaitingUserInput,
            HarnessValidationState = turn.Source == "provider_blocked" ? "not_run" : turn.OutcomeCode,
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

    private static ValidatedPrimitive ProviderBlockedPrimitive(string providerOutcome) =>
        new(
            "primitive-provider-blocked",
            PlannerPrimitiveIds.AssistantMessage,
            "provider-failure",
            "Live provider blocked",
            "The server-side live request returned a fixed sanitized blocker.",
            PlannerMoodTokens.Logistics,
            "logistics_transit",
            "blocked",
            [],
            [],
            ["evidence-primitive-provider-block"],
            "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            providerOutcome);

    private static IReadOnlyList<ValidatedTaskPrimitive> LivePrimitiveTasks() =>
    [
        new("task-live-intake", "Answer live planner form", "needs_user", 45, "Answer", [new("step-live-form", "Validated form rendered", "needs_user")]),
        new("task-live-options", "Generate planning options", "not_started", 0, "Queued", [new("step-live-second-turn", "Second provider turn after answer", "not_started")])
    ];

    private static ValidatedTaskPrimitive TaskFromRef(string taskId) =>
        new(taskId, "Live planner task", "needs_user", 45, "Review", [new($"step-{taskId}", "Validated primitive task", "needs_user")]);

    private static IReadOnlyList<ValidatedTaskPrimitive> ProviderBlockedTasks(string outcome) =>
    [
        new("task-live-intake", "Live provider request", "blocked", 20, "Blocked", [new("step-live-provider", outcome, "blocked")])
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

    private static TripSession CreatePrimitiveSession()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        session.MoveTo(HarnessStage.Intake);
        return session;
    }

    private static PlannerToolManifestMirror ToProviderMirror(PlannerToolManifest manifest) =>
        new(
            manifest.ManifestId,
            manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.GraphRevision,
            manifest.SessionId,
            manifest.Stage,
            manifest.AllowedPrimitives
                .Where(primitive => primitive.AnswerSchema.Required)
                .Select(primitive => new Pch.Providers.PlannerPrimitives.PlannerPrimitiveDefinition(
                    primitive.PrimitiveId,
                    primitive.PrimitiveId,
                    primitive.RendererKey))
                .ToArray(),
            manifest.AllowedFieldPaths,
            manifest.AllowedMoodTokens,
            manifest.MaxPrimitiveCount);

    private static IReadOnlyDictionary<string, string?> WithKeyFileAvailability(IReadOnlyDictionary<string, string?> source)
    {
        var environment = new Dictionary<string, string?>(source, StringComparer.Ordinal);
        if (!HasValue(environment, "PCH_LIVE_MODEL_KEY_AVAILABLE") &&
            (KeyFilePresent(environment, "OPENROUTER_API_KEY_FILE") || KeyFilePresent(environment, "OPENAI_API_KEY_FILE")))
        {
            environment["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true";
        }

        return environment;
    }

    private static bool KeyFilePresent(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var path) &&
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        new FileInfo(path).Length > 0;

    private static bool HasValue(IReadOnlyDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string PromptDigest(string prompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prompt ?? string.Empty));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string OutcomeForException(Exception exception)
    {
        if (exception is PlannerModelGuardException guard)
        {
            return guard.OutcomeCode;
        }

        return ProviderFailureClassifier.Classify(exception) switch
        {
            ProviderFailureClass.ProviderCreditExhausted => PlannerPrimitiveRunner.OutcomeCreditExhausted,
            ProviderFailureClass.ProviderRateLimited => PlannerPrimitiveRunner.OutcomeRateLimited,
            ProviderFailureClass.ProviderTimeout => PlannerPrimitiveRunner.OutcomeTimeout,
            ProviderFailureClass.ProviderEmptyContent => PlannerPrimitiveRunner.OutcomeEmptyContent,
            ProviderFailureClass.ProviderMalformedJson => PlannerPrimitiveRunner.OutcomeMalformedJson,
            ProviderFailureClass.ProviderSchemaInvalid => PlannerPrimitiveRunner.OutcomeSchemaInvalid,
            _ => PlannerPrimitiveRunner.OutcomeProviderUnavailable
        };
    }

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
            PlannerMoodTokens.ReflectiveCulture or "reflective-culture" => "mood-reflective-culture",
            PlannerMoodTokens.SoftNature or "soft-nature" => "mood-soft-nature",
            PlannerMoodTokens.LivelyFood or "lively-food" => "mood-lively-food",
            PlannerMoodTokens.CalmMorning or "calm-morning" => "mood-calm-morning",
            PlannerMoodTokens.RestorativeDowntime or "restorative-downtime" => "mood-restorative-downtime",
            PlannerMoodTokens.Logistics or "logistics_transit" or "logistics-transit" => "mood-logistics",
            _ => "mood-neutral"
        };

    private static string MoodTone(string mood) =>
        mood switch
        {
            "reflective-culture" or PlannerMoodTokens.ReflectiveCulture => "culture",
            "soft-nature" or PlannerMoodTokens.SoftNature => "nature",
            "lively-food" or PlannerMoodTokens.LivelyFood => "food",
            "logistics-transit" or "logistics_transit" or PlannerMoodTokens.Logistics => "transit",
            _ => "calm"
        };

    private static IReadOnlyDictionary<string, EndUserMediaAsset> MediaManifest() =>
        new Dictionary<string, EndUserMediaAsset>(StringComparer.Ordinal)
        {
            [PlannerMoodTokens.ReflectiveCulture] = Asset("backdrop.cultural.vermilion_torii.spiritual_serene", PlannerMoodTokens.ReflectiveCulture, "Vermilion torii prompt-studio cultural mood art.", "#d96f56"),
            [PlannerMoodTokens.SoftNature] = Asset("backdrop.scenic.fuji_lake.scenic_relaxed", PlannerMoodTokens.SoftNature, "Fuji lake prompt-studio scenic mood art.", "#8ab7cb"),
            [PlannerMoodTokens.LivelyFood] = Asset("backdrop.food.ramen_steam.food_cozy", PlannerMoodTokens.LivelyFood, "Ramen steam prompt-studio food mood art.", "#d06d4c"),
            [PlannerMoodTokens.CalmMorning] = Asset("backdrop.logistics.map_planning.family_easy", PlannerMoodTokens.CalmMorning, "Gentle map-planning prompt-studio morning mood art.", "#f6d7a7"),
            [PlannerMoodTokens.RestorativeDowntime] = Asset("backdrop.scenic.onsen_valley.wellness_restorative", PlannerMoodTokens.RestorativeDowntime, "Onsen valley prompt-studio restorative mood art.", "#93b7a6"),
            ["logistics_transit"] = Asset("backdrop.urban.station_grid.budget_practical", "logistics_transit", "Station grid prompt-studio logistics mood art.", "#8192a6"),
            [PlannerMoodTokens.Logistics] = Asset("backdrop.urban.station_grid.budget_practical", PlannerMoodTokens.Logistics, "Station grid prompt-studio logistics mood art.", "#8192a6"),
            ["mood_placeholder"] = Asset("backdrop.cultural.craft_district.arts_design", PlannerMoodTokens.Neutral, "Craft district prompt-studio fallback art.", "#c7a36b", "fallback")
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

    private sealed record PlannerModelRun(
        string ProviderRequestState,
        string ProviderOutcome,
        PlannerModelResult? Result);
}
