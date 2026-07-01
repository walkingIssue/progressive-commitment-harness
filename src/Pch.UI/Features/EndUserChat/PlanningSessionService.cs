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
    private readonly PlannerPrimitiveValidator _validator = new();
    private readonly PlannerTurnContextBuilder _turnContextBuilder = new();

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
        var context = CreateContext();
        return await StartAsync(context, prompt, selectedModelRole, cancellationToken).ConfigureAwait(false);
    }

    public PlanningSessionContext CreateContext(string? sessionId = null)
    {
        var mission = new TripMission(
            "mission-live-planning",
            "live_planning",
            "unconfirmed_destination",
            new DateOnly(2027, 1, 1),
            new DateOnly(2027, 1, 7),
            [new("traveler-live-user", "Traveler", null, [])],
            [],
            []);
        var session = new TripSession(sessionId ?? $"session-live-planning-{Guid.NewGuid():N}", mission);
        session.MoveTo(HarnessStage.Intake);
        return new PlanningSessionContext(session);
    }

    public async Task<PlanningSessionUiResult> StartAsync(
        PlanningSessionContext context,
        string prompt,
        string selectedModelRole,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedModelRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            var deterministicState = await _chatService.SendAsync(prompt, normalizedRole, cancellationToken).ConfigureAwait(false);
            return new(deterministicState, null, null, []);
        }

        var state = _chatService.CreateInitialState(normalizedRole) with
        {
            Prompt = string.Empty,
            Turns = AppendTurn(
                _chatService.CreateInitialState(normalizedRole).Turns,
                new(
                    "turn-user-live-prompt",
                    "user",
                    "prompt",
                    "submitted",
                    $"Live prompt received with {Math.Max(0, prompt?.Length ?? 0)} characters. Raw prompt text is not persisted in UI state.",
                    "prompt_received",
                    "evidence-live-prompt",
                    null))
        };
        var turnContext = _turnContextBuilder.Build(
            context.HarnessContext,
            new PlannerTurnContextRequest(context.Session.SessionId, prompt ?? string.Empty, "en-US", []));
        var manifest = turnContext.Manifest;
        var model = await RunPlannerModelAsync(
            manifest,
            FirstTurnPrompt(turnContext, prompt ?? string.Empty),
            turnContext,
            "run-end-user-planner-primitive",
            "turn-end-user-planner-primitive",
            "attempted",
            cancellationToken).ConfigureAwait(false);
        var canonical = model.Result is null
            ? ProviderBlockedTurn(manifest, model.ProviderOutcome)
            : ValidateModelResult(context.Session, manifest, model.Result);
        _turnContextBuilder.RecordValidatedTurn(context.HarnessContext, canonical);
        var turn = ProjectTurn(canonical, manifest, model.ProviderRequestState, model.ProviderOutcome);
        context.LastTurn = turn;
        context.AddTrace(PlanningSessionTraceEntry.FromModelRun(model, turn, null));

        return new(ApplyTurnState(state, turn), turn, null, []);
    }

    public async Task<PlanningSessionUiResult> SubmitAnswer(
        EndUserChatState state,
        EndUserValidatedTurnView turn,
        PrimitiveAnswerDto answer,
        CancellationToken cancellationToken = default)
    {
        var context = CreateContext(turn.SessionId);
        if (turn.CanonicalTurn is not null)
        {
            _turnContextBuilder.RecordValidatedTurn(context.HarnessContext, turn.CanonicalTurn);
        }

        context.LastTurn = turn;
        return await SubmitAnswer(context, state, turn, answer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlanningSessionUiResult> SubmitAnswer(
        PlanningSessionContext context,
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

        var answerApplication = context.HarnessContext.ApplyAnswers(new PlannerAnswerApplicationRequest(
            context.Session.SessionId,
            turn.GraphRevision,
            ToPlannerPrimitiveAnswers(turn, form, answer)));
        if (answerApplication.IsBlocked)
        {
            var blockedTurn = turn with
            {
                OutcomeCode = answerApplication.Code,
                Primitives = MarkPrimitiveValidationBlocked(
                    turn.Primitives,
                    form.InstanceId,
                    [new(answer.PrimitiveInstanceId, answerApplication.Code, answerApplication.Summary)])
            };
            return new(
                state with
                {
                    FinalState = "answer_validation_failed",
                    ErrorCode = "PCH_UI_PRIMITIVE_ANSWER_INVALID",
                    BlockedReason = answerApplication.Code
                },
                blockedTurn,
                answer,
                [new(answer.PrimitiveInstanceId, answerApplication.Code, answerApplication.Summary)]);
        }

        context.RecordAnswer(answer);
        context.Session.ApplyFormResponse(new FormResponse(
            answer.PrimitiveInstanceId,
            answer.FieldValues.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal),
            DateTimeOffset.UtcNow));
        var turnContext = _turnContextBuilder.Build(
            context.HarnessContext,
            new PlannerTurnContextRequest(context.Session.SessionId, string.Empty, "en-US", []));
        var manifest = turnContext.Manifest;
        var model = await RunPlannerModelAsync(
            manifest,
            SecondTurnPrompt(turnContext),
            turnContext,
            "run-end-user-planner-primitive-followup",
            "turn-end-user-planner-primitive-followup",
            "second_turn_attempted",
            cancellationToken).ConfigureAwait(false);
        var canonical = model.Result is null
            ? ProviderBlockedTurn(manifest, model.ProviderOutcome)
            : ValidateModelResult(context.Session, manifest, model.Result);
        _turnContextBuilder.RecordValidatedTurn(context.HarnessContext, canonical);
        var nextTurn = ProjectTurn(canonical, manifest, model.ProviderRequestState, model.ProviderOutcome);
        context.LastTurn = nextTurn;
        context.AddTrace(PlanningSessionTraceEntry.FromModelRun(model, nextTurn, answer));
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

    private static IReadOnlyList<PlannerPrimitiveAnswer> ToPlannerPrimitiveAnswers(
        EndUserValidatedTurnView turn,
        ValidatedPrimitive primitive,
        PrimitiveAnswerDto answer)
    {
        if (turn.CanonicalTurn is null || primitive.RendererKey != "form")
        {
            var evidenceIds = primitive.EvidenceIds
                .Concat(primitive.Fields.Select(field => field.EvidenceId).OfType<string>())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var selectedOptions = answer.FieldValues.Values
                .Where(value => primitive.Candidates.Any(candidate => string.Equals(candidate.CandidateId, value, StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return
            [
                new(
                    $"answer-{answer.PrimitiveInstanceId}",
                    answer.PrimitiveInstanceId,
                    primitive.PrimitiveId,
                    answer.FieldValues.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal),
                    selectedOptions,
                    evidenceIds)
            ];
        }

        return primitive.Fields
            .Select(field =>
            {
                var canonical = turn.CanonicalTurn.Primitives.FirstOrDefault(item => string.Equals(FieldId(item), field.FieldId, StringComparison.Ordinal))
                    ?? turn.CanonicalTurn.Primitives.First(item => string.Equals(item.PrimitiveId, field.PrimitiveId, StringComparison.Ordinal));
                var value = answer.FieldValues.TryGetValue(field.FieldId, out var submitted) ? submitted : field.Value;
                return new PlannerPrimitiveAnswer(
                    $"answer-{canonical.InstanceId}",
                    canonical.InstanceId,
                    canonical.PrimitiveId,
                    AnswerValuesFor(canonical, value),
                    SelectedOptionsFor(canonical, value),
                    canonical.EvidenceReferences.Count > 0 ? canonical.EvidenceReferences : primitive.EvidenceIds);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> AnswerValuesFor(ValidatedPrimitiveView primitive, string value)
    {
        var trimmed = value.Trim();
        return primitive.AnswerSchema.Kind switch
        {
            PlannerAnswerSchemaKinds.DateRange => DateRangeAnswerValues(trimmed),
            PlannerAnswerSchemaKinds.Date => new Dictionary<string, string?>(StringComparer.Ordinal) { ["value"] = FirstIsoDate(trimmed) ?? trimmed },
            PlannerAnswerSchemaKinds.Boolean => new Dictionary<string, string?>(StringComparer.Ordinal) { ["checked"] = BoolValue(trimmed) },
            PlannerAnswerSchemaKinds.SingleSelect => new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = trimmed },
            PlannerAnswerSchemaKinds.MultiSelect => new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = trimmed },
            PlannerAnswerSchemaKinds.RankedChoice => new Dictionary<string, string?>(StringComparer.Ordinal) { ["ranked"] = trimmed },
            PlannerAnswerSchemaKinds.Confirmation => new Dictionary<string, string?>(StringComparer.Ordinal) { ["value"] = trimmed },
            _ => new Dictionary<string, string?>(StringComparer.Ordinal) { ["value"] = trimmed }
        };
    }

    private static IReadOnlyList<string> SelectedOptionsFor(ValidatedPrimitiveView primitive, string value)
    {
        if (primitive.Options.Count == 0)
        {
            return [];
        }

        var allowed = primitive.Options.Select(option => option.OptionId).ToHashSet(StringComparer.Ordinal);
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> DateRangeAnswerValues(string value)
    {
        var dates = IsoDates(value);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["start"] = dates.FirstOrDefault() ?? value,
            ["end"] = dates.Skip(1).FirstOrDefault() ?? dates.FirstOrDefault() ?? value
        };
    }

    private static string BoolValue(string value) =>
        value is "true" or "checked" or "confirm" or "on" ? "true" : "false";

    private static string FirstTurnPrompt(PlannerTurnContext context, string prompt)
    {
        var factSummary = string.Join(
            "; ",
            context.AcceptedFacts
                .OrderBy(fact => fact.FieldPath, StringComparer.Ordinal)
                .Take(8)
                .Select(fact => $"{fact.FieldPath}={fact.Value}"));
        return $"Create validated planning primitives for this user request: {prompt}. Prompt category: {context.PromptCategory}. Current accepted facts: {factSummary}.";
    }

    private static string SecondTurnPrompt(PlannerTurnContext context)
    {
        var values = string.Join(
            "; ",
            context.SubmittedAnswers
                .SelectMany(answer => answer.Values.Select(pair => $"{answer.PrimitiveInstanceId}.{pair.Key}={pair.Value}"))
                .OrderBy(value => value, StringComparer.Ordinal)
                .Take(12));
        var acceptedFacts = string.Join(
            "; ",
            context.AcceptedFacts
                .OrderBy(fact => fact.FieldPath, StringComparer.Ordinal)
                .Take(12)
                .Select(fact => $"{fact.FieldPath}={fact.Value}"));
        var turnSummaries = string.Join(
            "; ",
            context.ValidatedTurnSummaries
                .Select(turn => $"{turn.TurnId}:{turn.Code}:{string.Join(',', turn.RenderedPrimitiveIds)}")
                .Take(8));
        return $"Continue from the validated primitive answer. Submitted values: {values}. Accepted facts: {acceptedFacts}. Prior validated turns: {turnSummaries}.";
    }

    private static string StateSummary(PlannerTurnContext context)
    {
        var facts = string.Join(
            "; ",
            context.AcceptedFacts
                .OrderBy(fact => fact.FieldPath, StringComparer.Ordinal)
                .Take(10)
                .Select(fact => $"{fact.FieldPath}={fact.Value}"));
        var answers = string.Join(
            "; ",
            context.SubmittedAnswers
                .SelectMany(answer => answer.Values.Select(pair => $"{answer.PrimitiveInstanceId}.{pair.Key}={pair.Value}"))
                .OrderBy(value => value, StringComparer.Ordinal)
                .Take(10));
        return $"stage={context.Stage}; graph={context.GraphRevision}; category={context.PromptCategory}; facts={facts}; answers={answers}";
    }

    private async Task<PlannerModelRun> RunPlannerModelAsync(
        PlannerToolManifest manifest,
        string prompt,
        PlannerTurnContext turnContext,
        string runId,
        string turnId,
        string attemptedState,
        CancellationToken cancellationToken)
    {
        var environment = WithKeyFileAvailability(_environment());
        var options = WithPlannerPrimitiveTokenBudget(PlannerModelOptions.FromEnvironment(environment), environment);
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
            PromptDigest: PromptDigest(prompt))
        {
            SanitizedStateSummary = StateSummary(turnContext),
            SubmittedAnswers = turnContext.SubmittedAnswers
                .Select(answer => new PlannerSubmittedAnswer(
                    answer.AnswerId,
                    answer.Values.Keys.FirstOrDefault() ?? answer.PrimitiveInstanceId,
                    answer.Values.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "submitted",
                    answer.PrimitiveInstanceId))
                .ToArray(),
            ContextToolResults = turnContext.ToolContextReferences
                .Select(reference => new PlannerContextToolResult(
                    "mock_context_provider",
                    reference.ReferenceId,
                    "planning_context",
                    reference.SourceClass,
                    reference.EvidenceReferences,
                    reference.Summary,
                    reference.Summary,
                    "session"))
                .ToArray()
        };

        try
        {
            var result = await _plannerRunner(request, options, cancellationToken).ConfigureAwait(false);
            if (NeedsTaskDecompositionRepair(result))
            {
                var repairedRequest = request with
                {
                    RunId = $"{runId}-task-repair",
                    RuntimePrompt = $"{prompt} Required semantic repair: include one task_decomposition primitive plus tasks[] records with task ids, titles, and primitiveRefs that reference the emitted primitive instance ids. Do not change safe user-facing primitive content except as needed to add canonical task decomposition."
                };
                result = await _plannerRunner(repairedRequest, options, cancellationToken).ConfigureAwait(false);
            }

            return new(attemptedState, PlannerPrimitiveRunner.OutcomeAccepted, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(attemptedState, OutcomeForException(ex), null);
        }
    }

    private static bool NeedsTaskDecompositionRepair(PlannerModelResult result) =>
        result.OutputKind == PlannerModelOutputKind.CompositeForm &&
        (!result.Primitives.Any(primitive => primitive.PrimitiveId == PlannerPrimitiveIds.TaskDecomposition) || result.Tasks.Count == 0);

    private static PlannerModelOptions WithPlannerPrimitiveTokenBudget(
        PlannerModelOptions options,
        IReadOnlyDictionary<string, string?> environment)
    {
        const int defaultMaxTokens = 4_000;
        if (environment.TryGetValue("PCH_PLANNER_PRIMITIVE_MAX_TOKENS", out var configured) &&
            int.TryParse(configured, out var maxTokens) &&
            maxTokens is >= 1_200 and <= 8_000)
        {
            return options with { MaxTokens = maxTokens };
        }

        return options with { MaxTokens = Math.Max(options.MaxTokens, defaultMaxTokens) };
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
            Primitives: result.Primitives.Select(primitive => ToHarnessPrimitive(manifest, primitive, result.Tasks)).ToArray());

        return _validator.Validate(session, manifest, proposal).View!;
    }

    private static PlannerPrimitiveInstance ToHarnessPrimitive(
        PlannerToolManifest manifest,
        PlannerPrimitiveInvocation primitive,
        IReadOnlyList<PlannerTaskInvocation> tasks)
    {
        var definition = manifest.AllowedPrimitives.FirstOrDefault(item => string.Equals(item.PrimitiveId, primitive.PrimitiveId, StringComparison.Ordinal))
            ?? manifest.AllowedPrimitives.First(item => item.PrimitiveId == PlannerPrimitiveIds.AssistantMessage);
        var fieldPath = string.IsNullOrWhiteSpace(primitive.FieldPath) ? null : primitive.FieldPath;
        if (definition.AnswerSchema.Kind is PlannerAnswerSchemaKinds.SingleSelect or PlannerAnswerSchemaKinds.MultiSelect or PlannerAnswerSchemaKinds.RankedChoice &&
            primitive.Options.Count == 0 &&
            primitive.CandidateIds.Count == 0 &&
            definition.AnswerSchema.Options.Count == 0)
        {
            definition = manifest.AllowedPrimitives.FirstOrDefault(item => item.PrimitiveId == PlannerPrimitiveIds.TextInput)
                ?? definition;
        }

        var candidateId = primitive.PrimitiveId is PlannerPrimitiveIds.CandidateDeck
                or PlannerPrimitiveIds.SingleSelect
                or PlannerPrimitiveIds.MultiSelect
                or PlannerPrimitiveIds.RankedChoice
            ? primitive.CandidateIds.FirstOrDefault(id => manifest.AllowedCandidateIds.Contains(id, StringComparer.Ordinal))
            : null;
        var safeMoodToken = SafeMoodToken(definition, manifest, primitive.MoodToken);
        var mediaToken = SafeMediaToken(definition, manifest, primitive.MediaToken, safeMoodToken);
        var evidenceRefs = SafeRefs(primitive.EvidenceRefs, "evidence-planner-primitive");
        var toolRefs = SafeRefs(primitive.ToolContextRefs, null);
        var trustedTaskRefs = primitive.TaskRefs
            .Where(IsSafeId)
            .Where(taskId => manifest.AllowedTaskIds.Contains(taskId, StringComparer.Ordinal))
            .ToArray();

        return new(
            InstanceId: SafePrimitiveInstanceId(primitive, definition.PrimitiveId, fieldPath),
            PrimitiveId: definition.PrimitiveId,
            SchemaVersion: manifest.SchemaVersion,
            RendererKey: definition.RendererKey,
            Label: SafeDisplayText(primitive.Label, definition.PrimitiveId),
            Prompt: SafeDisplayText(primitive.PromptText, "Validated planner primitive"),
            FieldPath: fieldPath,
            SlotId: null,
            CandidateId: candidateId,
            TaskId: trustedTaskRefs.FirstOrDefault(),
            MoodToken: safeMoodToken,
            MediaToken: mediaToken,
            AnswerSchema: definition.AnswerSchema,
            Answers: ModelAuthoredAnswers(definition.AnswerSchema, primitive, fieldPath),
            EvidenceReferences: evidenceRefs,
            DependencyReferences: [])
        {
            HelpText = SafeDisplayText(primitive.HelpText, null),
            Options = SafeOptions(primitive.Options, definition, manifest),
            Defaults = SafeDefaults(definition.AnswerSchema, primitive.DefaultValue),
            TaskReferences = trustedTaskRefs
                .Select(taskId => new PlannerTaskReference(
                    taskId,
                    SafeDisplayText(tasks.FirstOrDefault(task => string.Equals(task.TaskId, taskId, StringComparison.Ordinal))?.Title, taskId),
                    null,
                    evidenceRefs,
                    toolRefs))
                .ToArray(),
            TaskDecomposition = definition.PrimitiveId == PlannerPrimitiveIds.TaskDecomposition
                ? tasks
                    .Where(task => IsSafeId(task.TaskId))
                    .Select((task, index) => new PlannerTaskDecompositionItem(
                        task.TaskId,
                        SafeDisplayText(task.Title, task.TaskId),
                        index == 0 ? PlannerTaskStates.Active : PlannerTaskStates.Pending,
                        index,
                        [],
                        evidenceRefs))
                    .ToArray()
                : [],
            ToolContextReferences = toolRefs,
            RendererHints = primitive.RendererHints
                .Where(pair => IsSafeId(pair.Key) && !ContainsUnsafeText(pair.Value))
                .Select(pair => new PlannerRendererHint(pair.Key, pair.Value))
                .ToArray()
        };
    }

    private static IReadOnlyList<PlannerPrimitiveDefault> SafeDefaults(PlannerAnswerSchema schema, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsUnsafeText(value))
        {
            return [];
        }

        return schema.Kind switch
        {
            PlannerAnswerSchemaKinds.Date => FirstIsoDate(value) is { } date ? [new("value", date)] : [],
            PlannerAnswerSchemaKinds.DateRange => IsoDates(value) switch
            {
                { Count: >= 2 } dates => [new("start", dates[0]), new("end", dates[1])],
                { Count: 1 } dates => [new("start", dates[0]), new("end", dates[0])],
                _ => []
            },
            PlannerAnswerSchemaKinds.Boolean => [new("checked", BoolValue(value))],
            PlannerAnswerSchemaKinds.None or PlannerAnswerSchemaKinds.TaskDecomposition => [],
            _ => [new("value", SafeDisplayText(value, null))]
        };
    }

    private static string SafePrimitiveInstanceId(
        PlannerPrimitiveInvocation primitive,
        string primitiveId,
        string? fieldPath)
    {
        if (!string.IsNullOrWhiteSpace(primitive.InstanceId) && IsSafeId(primitive.InstanceId))
        {
            return primitive.InstanceId;
        }

        return FallbackInstanceId(primitiveId, fieldPath);
    }

    private static string FallbackInstanceId(string primitiveId, string? fieldPath) =>
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

    private static bool IsSafeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 120 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/');

    private static string SafeDisplayText(string? value, string? fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback ?? "redacted" : value.Trim();
        if (ContainsUnsafeText(text))
        {
            return "redacted";
        }

        return text.Length <= 160 ? text : text[..160];
    }

    private static bool ContainsUnsafeText(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("RAW_", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("APPROVAL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
            text.Contains('<') ||
            text.Contains('>'));

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

    private static string? SafeMediaToken(
        Pch.Core.PlannerPrimitiveDefinition definition,
        PlannerToolManifest manifest,
        string? mediaToken,
        string? moodToken)
    {
        if (!definition.SupportsMedia)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(mediaToken) &&
            manifest.AllowedMediaTokens.Contains(mediaToken, StringComparer.Ordinal))
        {
            return mediaToken;
        }

        var derived = string.IsNullOrWhiteSpace(moodToken) ? null : $"media:{moodToken}";
        return derived is not null && manifest.AllowedMediaTokens.Contains(derived, StringComparer.Ordinal)
            ? derived
            : manifest.AllowedMediaTokens.FirstOrDefault();
    }

    private static IReadOnlyList<string> SafeRefs(IReadOnlyList<string> refs, string? fallback)
    {
        var safe = refs.Where(IsSafeId).Distinct(StringComparer.Ordinal).Take(12).ToArray();
        return safe.Length > 0 || fallback is null ? safe : [fallback];
    }

    private static IReadOnlyList<Pch.Core.PlannerPrimitiveOption> SafeOptions(
        IReadOnlyList<Pch.Providers.PlannerPrimitives.PlannerPrimitiveOption> options,
        Pch.Core.PlannerPrimitiveDefinition definition,
        PlannerToolManifest manifest) =>
        definition.AnswerSchema.Kind == PlannerAnswerSchemaKinds.Confirmation
            ? []
            :
        options
            .Where(option => IsSafeId(option.OptionId))
            .Take(manifest.MaxOptionCount)
            .Select(option => new Pch.Core.PlannerPrimitiveOption(
                option.OptionId,
                SafeDisplayText(option.Label, option.OptionId),
                SafeDisplayText(option.Summary, null),
                definition.SupportsMood && manifest.AllowedMoodTokens.Contains(option.MoodToken ?? string.Empty, StringComparer.Ordinal) ? option.MoodToken : null,
                definition.SupportsMedia && manifest.AllowedMediaTokens.Contains(option.MediaToken ?? string.Empty, StringComparer.Ordinal) ? option.MediaToken : null,
                [],
                SafeRefs(option.ToolContextRefs, null)))
            .ToArray();

    private static IReadOnlyDictionary<string, string?> ModelAuthoredAnswers(
        PlannerAnswerSchema schema,
        PlannerPrimitiveInvocation primitive,
        string? fieldPath)
    {
        var modelText = SafeDisplayText(
            primitive.DefaultValue ?? primitive.PromptText ?? primitive.Label,
            fieldPath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? primitive.PrimitiveId);
        return schema.Kind switch
        {
            PlannerAnswerSchemaKinds.Text => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = modelText
            },
            PlannerAnswerSchemaKinds.Date => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = FirstIsoDate(modelText) ?? "2027-01-01"
            },
            PlannerAnswerSchemaKinds.DateRange => DateRangeAnswerValues(modelText),
            PlannerAnswerSchemaKinds.Confirmation => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = schema.Options.FirstOrDefault() ?? "confirm"
            },
            PlannerAnswerSchemaKinds.SingleSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["selected"] = primitive.Options.FirstOrDefault()?.OptionId ?? primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault()
                }
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PlannerAnswerSchemaKinds.MultiSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["selected"] = primitive.Options.FirstOrDefault()?.OptionId ?? primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault()
                }
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PlannerAnswerSchemaKinds.RankedChoice => new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ranked"] = primitive.Options.FirstOrDefault()?.OptionId ?? primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault()
                }
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            PlannerAnswerSchemaKinds.NumberRange => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["min"] = "0",
                ["max"] = "0"
            },
            _ => new Dictionary<string, string?>(StringComparer.Ordinal)
        };
    }

    private static string? FirstIsoDate(string value) =>
        IsoDates(value).FirstOrDefault();

    private static IReadOnlyList<string> IsoDates(string value)
    {
        var dates = new List<string>();
        for (var index = 0; index <= value.Length - 10; index++)
        {
            var candidate = value.Substring(index, 10);
            if (DateOnly.TryParseExact(candidate, "yyyy-MM-dd", out _))
            {
                dates.Add(candidate);
            }
        }

        return dates;
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
        var isHarnessBlocked = !isProviderBlocked && (string.Equals(canonical.Source, "harness_blocked", StringComparison.Ordinal) || canonical.Primitives.Count == 0);
        var primitives = isProviderBlocked
            ? [ProviderBlockedPrimitive(providerOutcome)]
            : isHarnessBlocked
                ? [HarnessBlockedPrimitive(canonical.Code)]
            : ProjectPrimitives(canonical);
        var tasks = ProjectTasks(canonical, isProviderBlocked, providerOutcome);
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
            .Where(IsFormFieldPrimitive)
            .ToArray();
        var projected = new List<ValidatedPrimitive>();

        projected.AddRange(canonical.Primitives
            .Where(IsStandaloneMessagePrimitive)
            .Select(primitive => ProjectMessage(primitive, canonical)));

        if (formViews.Length > 0)
        {
            projected.Add(ProjectForm(formViews, canonical));
        }

        projected.AddRange(canonical.Primitives
            .Where(IsChoicePrimitive)
            .Select(primitive => ProjectDeck(primitive, canonical)));

        if (projected.Count == 0)
        {
            var primitive = canonical.Primitives.First(primitive => primitive.PrimitiveId != PlannerPrimitiveIds.TaskDecomposition);
            projected.Add(ProjectMessage(primitive, canonical));
        }

        return projected;
    }

    private static bool IsFormFieldPrimitive(ValidatedPrimitiveView primitive) =>
        primitive.AnswerSchema.Required &&
        primitive.PrimitiveId is not (PlannerPrimitiveIds.CandidateDeck or PlannerPrimitiveIds.ChoiceCard or PlannerPrimitiveIds.TaskDecomposition);

    private static bool IsChoicePrimitive(ValidatedPrimitiveView primitive) =>
        primitive.PrimitiveId is PlannerPrimitiveIds.CandidateDeck or PlannerPrimitiveIds.ChoiceCard;

    private static bool IsStandaloneMessagePrimitive(ValidatedPrimitiveView primitive) =>
        primitive.PrimitiveId is PlannerPrimitiveIds.AssistantMessage or PlannerPrimitiveIds.StatusNotice
            or PlannerPrimitiveIds.ToolSearchRequest or PlannerPrimitiveIds.ToolGapRequest;

    private static ValidatedPrimitive ProjectForm(
        IReadOnlyList<ValidatedPrimitiveView> primitives,
        Pch.Core.ValidatedTurnView canonical)
    {
        var first = primitives.First();
        var title = first.Label ?? "Validated planner form";
        var prompt = first.Prompt ?? "Review the validated model-authored fields.";
        return new(
            InstanceId: $"form-{first.InstanceId}",
            PrimitiveId: "composite_form",
            RendererKey: "form",
            Title: title,
            Prompt: prompt,
            MoodToken: first.MoodToken ?? PlannerMoodTokens.Neutral,
            MediaToken: first.MediaToken ?? first.MoodToken ?? PlannerMoodTokens.Neutral,
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
            AllowedValues: AllowedValuesFor(primitive));
    }

    private static ValidatedPrimitive ProjectMessage(ValidatedPrimitiveView primitive, Pch.Core.ValidatedTurnView canonical) =>
        new(
            primitive.InstanceId,
            primitive.PrimitiveId,
            IsRenderableAcceptedCode(canonical.Code)
                ? "assistant_message"
                : "provider_failure",
            primitive.Label ?? "Live planner update",
            primitive.Prompt ?? "The live planner returned a sanitized update.",
            primitive.MoodToken ?? PlannerMoodTokens.Logistics,
            primitive.MediaToken ?? "logistics_transit",
            canonical.Code,
            [],
            [],
            primitive.EvidenceReferences.Count > 0 ? primitive.EvidenceReferences : canonical.EvidenceReferences,
            IsRenderableAcceptedCode(canonical.Code) ? null : canonical.Code,
            IsRenderableAcceptedCode(canonical.Code) ? null : canonical.Code);

    private static bool IsRenderableAcceptedCode(string code) =>
        code is PlannerPrimitiveValidator.AcceptedCode
            or PlannerPrimitiveValidator.AwaitingUserInputCode
            or PlannerPrimitiveValidator.ToolSearchRequestedCode
            or PlannerPrimitiveValidator.ToolGapReviewRequiredCode;

    private static ValidatedPrimitive ProjectDeck(ValidatedPrimitiveView primitive, Pch.Core.ValidatedTurnView canonical) =>
        new(
            primitive.InstanceId,
            primitive.PrimitiveId,
            "candidate_deck",
            primitive.Label ?? "Choose a validated option",
            primitive.Prompt ?? "Pick one validated option.",
            primitive.MoodToken ?? PlannerMoodTokens.Neutral,
            primitive.MediaToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
            AwaitingUserInput,
            [],
            primitive.Options.Count > 0
                ? primitive.Options.Select(option => Candidate(
                    option.OptionId,
                    option.MoodToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                    option.MediaToken ?? primitive.MediaToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                    option.Label,
                    option.Summary ?? primitive.Prompt ?? "Validated model-authored option.",
                    option.EvidenceReferences.Count > 0 ? option.EvidenceReferences : primitive.EvidenceReferences)).ToArray()
                : primitive.CandidateId is null
                    ? []
                    : [Candidate(
                        primitive.CandidateId,
                        primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                        primitive.MediaToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                        primitive.Label ?? primitive.CandidateId,
                        primitive.Prompt ?? "Validated model-authored option.",
                        primitive.EvidenceReferences)],
            primitive.EvidenceReferences.Count > 0 ? primitive.EvidenceReferences : canonical.EvidenceReferences);

    private static IReadOnlyList<string> AllowedValuesFor(ValidatedPrimitiveView primitive) =>
        primitive.Options.Count > 0
            ? primitive.Options.Select(option => option.OptionId).ToArray()
            : primitive.AnswerSchema.Options;

    private static string FieldId(ValidatedPrimitiveView primitive) =>
        primitive.FieldPath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? primitive.InstanceId;

    private static string LabelFor(string fieldId) =>
        fieldId.Replace('_', ' ');

    private static string FieldRenderer(ValidatedPrimitiveView primitive)
    {
        var rendererKey = primitive.RendererKey.Replace('-', '_');
        return rendererKey switch
        {
            "text_input" or "textarea" or "number_input" or "slider" or "date" or "date_range" or
                "radio_group" or "select" or "multi_select" or "checkbox" => rendererKey,
            _ => primitive.AnswerSchema.Kind switch
            {
                PlannerAnswerSchemaKinds.DateRange => "date_range",
                PlannerAnswerSchemaKinds.SingleSelect => "select",
                PlannerAnswerSchemaKinds.MultiSelect => "multi_select",
                PlannerAnswerSchemaKinds.RankedChoice => "multi_select",
                PlannerAnswerSchemaKinds.Confirmation => primitive.AnswerSchema.Options.Count <= 3 ? "radio_group" : "select",
                _ => "text_input"
            }
        };
    }

    private static bool IsProviderFailure(ValidatedPrimitive primitive) =>
        primitive.RendererKey.Replace('-', '_') == "provider_failure";

    private static EndUserChatState ApplyTurnState(EndUserChatState state, EndUserValidatedTurnView turn)
    {
        var hasFailure = turn.Source == "provider_blocked" || turn.Primitives.Any(IsProviderFailure);
        var missingTaskDecomposition = turn.Tasks.Any(task => task.TaskId == "task-decomposition-missing");
        var blockedReason = turn.Source == "provider_blocked"
            ? turn.ProviderOutcome
            : turn.Primitives.FirstOrDefault(IsProviderFailure)?.BlockedReason
                ?? (missingTaskDecomposition ? PlannerPrimitiveValidator.TaskDecompositionMissingCode : null);
        var errorCode = turn.Source == "provider_blocked"
            ? "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"
            : turn.Primitives.FirstOrDefault(IsProviderFailure)?.ErrorCode
                ?? (missingTaskDecomposition ? "PCH_UI_TASK_DECOMPOSITION_MISSING" : null);

        return state with
        {
            ModeLabel = turn.Source == "provider_blocked" ? "Live planner blocked" : "Live planner attached",
            ModeState = turn.Source == "provider_blocked" ? "live-model-blocked" : "live-model-attached",
            ComposerState = AwaitingUserInput,
            FinalState = hasFailure || missingTaskDecomposition
                ? "provider_blocked"
                : AwaitingUserInput,
            LivePreflightState = turn.ProviderRequestState == "not_attempted" ? state.LivePreflightState : "preflight_passed",
            LiveProposalState = hasFailure || missingTaskDecomposition ? "provider_blocked" : AwaitingUserInput,
            HarnessValidationState = turn.Source == "provider_blocked" ? "not_run" : turn.OutcomeCode,
            LatestTurnSource = "validated_primitive_turn",
            ProviderRequestState = turn.ProviderRequestState,
            ProviderOutcome = turn.ProviderOutcome,
            ProviderHealth = turn.Source == "provider_blocked" ? turn.ProviderOutcome : "provider_request_accepted",
            Tasks = turn.Tasks.Select(ToEndUserTask).ToArray(),
            FormCard = null,
            ChoiceSet = null,
            ApprovalPlate = null,
            Evidence = turn.Evidence,
            PlanningTimeline = turn.Timeline,
            ErrorCode = errorCode,
            BlockedReason = blockedReason
        };
    }

    private static string FieldValue(ValidatedPrimitiveView primitive) =>
        primitive.AnswerSchema.Kind switch
        {
            PlannerAnswerSchemaKinds.DateRange => primitive.Answers.TryGetValue("start", out var start)
                ? $"{start} to {(primitive.Answers.TryGetValue("end", out var end) ? end : start)}".Trim()
                : string.Empty,
            PlannerAnswerSchemaKinds.SingleSelect => primitive.Answers.TryGetValue("selected", out var selected) ? selected ?? string.Empty : string.Empty,
            PlannerAnswerSchemaKinds.MultiSelect => primitive.Answers.TryGetValue("selected", out var multi) ? multi ?? string.Empty : string.Empty,
            PlannerAnswerSchemaKinds.Confirmation => primitive.Answers.TryGetValue("value", out var value) ? value ?? string.Empty : string.Empty,
            _ => primitive.Answers.TryGetValue("value", out var text) ? text ?? string.Empty : string.Empty
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
            "provider_failure",
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

    private static ValidatedPrimitive HarnessBlockedPrimitive(string code) =>
        new(
            "primitive-harness-validation-blocked",
            PlannerPrimitiveIds.StatusNotice,
            "provider_failure",
            "Harness validation blocked",
            "The model-authored primitive did not pass the validated renderer contract.",
            PlannerMoodTokens.Logistics,
            "logistics_transit",
            "blocked",
            [],
            [],
            ["evidence-primitive-harness-block"],
            "PCH_UI_HARNESS_VALIDATION_BLOCKED",
            code);

    private static IReadOnlyList<ValidatedTaskPrimitive> ProjectTasks(
        Pch.Core.ValidatedTurnView canonical,
        bool isProviderBlocked,
        string providerOutcome)
    {
        if (isProviderBlocked)
        {
            return ProviderBlockedTasks(providerOutcome);
        }

        var tasks = canonical.Primitives
            .SelectMany(primitive => primitive.TaskDecomposition)
            .OrderBy(task => task.Order)
            .ToArray();
        if (tasks.Length == 0)
        {
            return MissingTaskDecomposition();
        }

        return tasks
            .Select(task => new ValidatedTaskPrimitive(
                task.TaskId,
                task.Title,
                TaskState(task.State),
                TaskProgress(task.State),
                TaskStatusLabel(task.State),
                [new($"step-{task.TaskId}", task.Title, TaskState(task.State))]))
            .ToArray();
    }

    private static IReadOnlyList<ValidatedTaskPrimitive> ProviderBlockedTasks(string outcome) =>
    [
        new("task-live-intake", "Live provider request", "blocked", 20, "Blocked", [new("step-live-provider", outcome, "blocked")])
    ];

    private static IReadOnlyList<ValidatedTaskPrimitive> MissingTaskDecomposition() =>
    [
        new(
            "task-decomposition-missing",
            "Task decomposition missing",
            "blocked",
            0,
            "Review",
            [new("step-task-decomposition-missing", "Planner did not provide validated task decomposition data.", "blocked")])
    ];

    private static string TaskState(string state) =>
        state switch
        {
            PlannerTaskStates.Active => "active",
            PlannerTaskStates.Complete => "complete",
            PlannerTaskStates.Blocked => "blocked",
            _ => "pending"
        };

    private static int TaskProgress(string state) =>
        state switch
        {
            PlannerTaskStates.Complete => 100,
            PlannerTaskStates.Active => 45,
            PlannerTaskStates.Blocked => 10,
            _ => 20
        };

    private static string TaskStatusLabel(string state) =>
        state switch
        {
            PlannerTaskStates.Complete => "Complete",
            PlannerTaskStates.Active => "Active",
            PlannerTaskStates.Blocked => "Blocked",
            _ => "Pending"
        };

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

    private static PlannerToolManifestMirror ToProviderMirror(PlannerToolManifest manifest) =>
        new(
            manifest.ManifestId,
            manifest.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            manifest.GraphRevision,
            manifest.SessionId,
            manifest.Stage,
            manifest.AllowedPrimitives
                .Select(primitive => new Pch.Providers.PlannerPrimitives.PlannerPrimitiveDefinition(
                    primitive.PrimitiveId,
                    primitive.PrimitiveId,
                    primitive.PrimitiveId))
                .ToArray(),
            manifest.AllowedFieldPaths,
            manifest.AllowedMoodTokens,
            manifest.MaxPrimitiveCount)
        {
            AllowedMediaTokens = manifest.AllowedMediaTokens,
            AllowedToolIds = manifest.AllowedToolIds.Count > 0 ? manifest.AllowedToolIds : ["mock_context_provider"]
        };

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
        var key = string.IsNullOrWhiteSpace(token) ? "mood_placeholder" : token.StartsWith("media:", StringComparison.Ordinal) ? token["media:".Length..] : token;
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

}

public sealed record PlannerModelRun(
    string ProviderRequestState,
    string ProviderOutcome,
    PlannerModelResult? Result);
