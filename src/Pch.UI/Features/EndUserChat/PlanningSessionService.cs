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
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [field.FieldId] = value
                    },
                    [],
                    canonical.EvidenceReferences.Count > 0 ? canonical.EvidenceReferences : primitive.EvidenceIds);
            })
            .ToArray();
    }

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
            InstanceId: SafePrimitiveInstanceId(primitive, definition.PrimitiveId, fieldPath),
            PrimitiveId: primitive.PrimitiveId,
            SchemaVersion: manifest.SchemaVersion,
            RendererKey: definition.RendererKey,
            Label: SafeDisplayText(primitive.Label, definition.PrimitiveId),
            Prompt: SafeDisplayText(primitive.PromptText, "Validated planner primitive"),
            FieldPath: fieldPath,
            SlotId: null,
            CandidateId: candidateId,
            TaskId: null,
            MoodToken: safeMoodToken,
            MediaToken: definition.SupportsMedia && !string.IsNullOrWhiteSpace(safeMoodToken) ? $"media:{safeMoodToken}" : null,
            AnswerSchema: definition.AnswerSchema,
            Answers: ModelAuthoredAnswers(definition.AnswerSchema, primitive, fieldPath),
            EvidenceReferences: ["evidence-planner-primitive"],
            DependencyReferences: []);
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

    private static bool IsSafeId(string value) =>
        value.Length <= 120 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/');

    private static string SafeDisplayText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (text.Contains("RAW_", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
            text.Contains('<') ||
            text.Contains('>'))
        {
            return "redacted";
        }

        return text.Length <= 160 ? text : text[..160];
    }

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

    private static IReadOnlyDictionary<string, string?> ModelAuthoredAnswers(
        PlannerAnswerSchema schema,
        PlannerPrimitiveInvocation primitive,
        string? fieldPath)
    {
        var modelText = SafeDisplayText(
            primitive.PromptText ?? primitive.Label,
            fieldPath?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? primitive.PrimitiveId);
        return schema.Kind switch
        {
            PlannerAnswerSchemaKinds.Text => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = modelText
            },
            PlannerAnswerSchemaKinds.DateRange => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["start"] = $"{modelText}-start",
                ["end"] = $"{modelText}-end"
            },
            PlannerAnswerSchemaKinds.Confirmation => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["value"] = schema.Options.FirstOrDefault() ?? "confirm"
            },
            PlannerAnswerSchemaKinds.SingleSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["selected"] = primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault() ?? "selected"
            },
            PlannerAnswerSchemaKinds.MultiSelect => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["selected"] = primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault() ?? "selected"
            },
            PlannerAnswerSchemaKinds.RankedChoice => new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ranked"] = primitive.CandidateIds.FirstOrDefault() ?? schema.Options.FirstOrDefault() ?? "ranked"
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
        var tasks = ProjectTasks(primitives, isProviderBlocked, providerOutcome);
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
            primitive.Label ?? "Choose a validated option",
            primitive.Prompt ?? "Pick one validated option.",
            primitive.MoodToken ?? PlannerMoodTokens.Neutral,
            primitive.MediaToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
            AwaitingUserInput,
            [],
            primitive.CandidateId is null
                ? []
                : [Candidate(
                    primitive.CandidateId,
                    primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                    primitive.MediaToken ?? primitive.MoodToken ?? PlannerMoodTokens.Neutral,
                    primitive.Label ?? primitive.CandidateId,
                    primitive.Prompt ?? "Validated model-authored option.",
                    primitive.EvidenceReferences)],
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
            ModeLabel = turn.Source == "provider_blocked" ? "Live planner blocked" : "Live planner attached",
            ModeState = turn.Source == "provider_blocked" ? "live-model-blocked" : "live-model-attached",
            ComposerState = AwaitingUserInput,
            FinalState = turn.Source == "provider_blocked" || turn.Primitives.Any(primitive => primitive.RendererKey == "provider-failure")
                ? "provider_blocked"
                : AwaitingUserInput,
            LivePreflightState = turn.ProviderRequestState == "not_attempted" ? state.LivePreflightState : "preflight_passed",
            LiveProposalState = turn.Source == "provider_blocked" ? "provider_blocked" : AwaitingUserInput,
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

    private static IReadOnlyList<ValidatedTaskPrimitive> ProjectTasks(
        IReadOnlyList<ValidatedPrimitive> primitives,
        bool isProviderBlocked,
        string providerOutcome)
    {
        if (isProviderBlocked)
        {
            return ProviderBlockedTasks(providerOutcome);
        }

        return primitives
            .Select(primitive => new ValidatedTaskPrimitive(
                $"task-{primitive.InstanceId}",
                primitive.Title,
                primitive.State == AwaitingUserInput ? "needs_user" : primitive.State,
                primitive.State == AwaitingUserInput ? 45 : 20,
                primitive.State == AwaitingUserInput ? "Answer" : "Review",
                [new($"step-{primitive.InstanceId}", primitive.Prompt, primitive.State == AwaitingUserInput ? "needs_user" : primitive.State)]))
            .ToArray();
    }

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

}

public sealed record PlannerModelRun(
    string ProviderRequestState,
    string ProviderOutcome,
    PlannerModelResult? Result);
