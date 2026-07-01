using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pch.Harness;
using Pch.Providers.LivePreflight;
using Pch.Providers.LiveTurns;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Pch.Providers.PlannerPrimitives;
using Pch.UI.Features.EndUserChat;
using Xunit;

namespace Pch.UI.Tests;

public sealed class PlanningSessionServiceTests
{
    [Fact]
    public void PlanningSessionServiceIsResolvableFromDi()
    {
        var services = new ServiceCollection();
        services.AddScoped<FormBuilder>();
        services.AddScoped(_ => LiveChatService());
        services.AddScoped<PlanningSessionService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PlanningSessionService>());
    }

    [Fact]
    public async Task TripLivePathUsesServiceBackedValidatedTurnAndNoDeterministicSeededCards()
    {
        var service = PlanningService();

        var result = await service.StartAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with validated primitives.",
            EndUserModelRoleSelection.InHarnessActionGenerator);

        Assert.NotNull(result.Turn);
        Assert.Equal("1", result.Turn.ManifestVersion);
        Assert.Equal("live_provider_candidate", result.Turn.Source);
        Assert.Equal(PlanningSessionService.AwaitingUserInput, result.Turn.OutcomeCode);
        Assert.NotNull(result.Turn.CanonicalTurn);
        Assert.Equal(PlanningSessionService.AwaitingUserInput, result.Turn.CanonicalTurn.Code);
        Assert.Equal("attempted", result.State.ProviderRequestState);
        Assert.Equal("awaiting_user_input", result.State.FinalState);
        Assert.Null(result.State.ChoiceSet);
        Assert.Null(result.State.FormCard);
        Assert.Contains(result.Turn.Primitives, primitive => primitive.RendererKey == "form"
            && primitive.PrimitiveId == "composite_form"
            && primitive.Fields.Any(field => field.PrimitiveId == "date_range" && field.RendererKey == "date_range"));
        Assert.All(result.Turn.Primitives.SelectMany(primitive => primitive.Candidates), candidate =>
            Assert.Equal("validated_primitive", candidate.Source));
        AssertRawTextAbsent(JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task AnswerDtoGeneratedFromValidatedFormAndAcceptedBySession()
    {
        var runner = new CountingPlannerPrimitiveRunner();
        var service = PlanningService(runner.RunAsync);
        var result = await service.StartAsync(
            "Plan a live primitive form.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var turn = Assert.IsType<EndUserValidatedTurnView>(result.Turn);

        var answer = service.BuildDefaultAnswer(turn);
        var answered = await service.SubmitAnswer(result.State, turn, answer);
        var form = Assert.Single(turn.Primitives, primitive => primitive.RendererKey == "form");

        Assert.Empty(answered.ValidationErrors);
        Assert.NotNull(answered.LastAnswer);
        Assert.Equal(form.InstanceId, answered.LastAnswer.PrimitiveInstanceId);
        Assert.All(form.Fields, field => Assert.True(answered.LastAnswer.FieldValues.ContainsKey(field.FieldId)));
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(
            ["turn-end-user-planner-primitive", "turn-end-user-planner-primitive-followup"],
            runner.TurnIds);
        Assert.Equal("second_turn_attempted", answered.State.ProviderRequestState);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, answered.State.ProviderOutcome);
        Assert.Equal(PlanningSessionService.AwaitingUserInput, answered.Turn?.OutcomeCode);
        Assert.Contains(answered.Turn!.Primitives, primitive => primitive.RendererKey == "form");
        Assert.Contains(answered.State.Turns, turnItem => turnItem.TurnId == "turn-primitive-answer-submitted"
            && turnItem.OutcomeCode == PlanningSessionService.AnswerAccepted);
        Assert.Contains(answered.State.Turns, turnItem => turnItem.TurnId == "turn-live-model-followup"
            && turnItem.OutcomeCode == PlannerPrimitiveRunner.OutcomeAccepted);
        AssertRawTextAbsent(JsonSerializer.Serialize(answered));
    }

    [Fact]
    public async Task PlanningSessionStoreStartReturnsSanitizedValidatedForm()
    {
        var runner = new CountingPlannerPrimitiveRunner();
        var store = new PlanningSessionStore();
        var service = PlanningService(runner.RunAsync);

        var response = await store.StartAsync(
            service,
            new("RAW_USER_PROMPT_SHOULD_NOT_LEAK Plan Japan with primitives.", EndUserModelRoleSelection.InHarnessActionGenerator));

        Assert.Equal("started", response.Status);
        Assert.StartsWith("planning-http-session-", response.SessionId, StringComparison.Ordinal);
        Assert.Equal(1, runner.CallCount);
        Assert.NotNull(response.Turn);
        Assert.Null(response.Turn.CanonicalTurn);
        Assert.Contains(response.Turn.Primitives, primitive => primitive.RendererKey == "form");
        Assert.Equal("attempted", response.State.ProviderRequestState);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, response.State.ProviderOutcome);
        AssertRawTextAbsent(JsonSerializer.Serialize(response));
    }

    [Fact]
    public async Task PlanningSessionStoreAnswerInvokesSecondRunner()
    {
        var runner = new CountingPlannerPrimitiveRunner();
        var store = new PlanningSessionStore();
        var service = PlanningService(runner.RunAsync);
        var started = await store.StartAsync(
            service,
            new("Plan a live primitive form.", EndUserModelRoleSelection.InHarnessActionGenerator));
        var form = Assert.Single(started.Turn!.Primitives, primitive => primitive.RendererKey == "form");

        var answered = await store.AnswerAsync(
            service,
            started.SessionId,
            new(
                form.InstanceId,
                form.Fields.ToDictionary(field => field.FieldId, field => field.Value, StringComparer.Ordinal)));

        Assert.Equal("answered", answered.Status);
        Assert.Equal(2, runner.CallCount);
        Assert.Equal(
            ["turn-end-user-planner-primitive", "turn-end-user-planner-primitive-followup"],
            runner.TurnIds);
        Assert.Equal("second_turn_attempted", answered.State.ProviderRequestState);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, answered.State.ProviderOutcome);
        Assert.NotNull(answered.Turn);
        Assert.Null(answered.Turn.CanonicalTurn);
        AssertRawTextAbsent(JsonSerializer.Serialize(answered));
    }

    [Fact]
    public async Task PlanningSessionStoreUnknownSessionReturnsFixedErrorWithoutProviderCall()
    {
        var runner = new CountingPlannerPrimitiveRunner();
        var store = new PlanningSessionStore();
        var service = PlanningService(runner.RunAsync);

        var response = await store.AnswerAsync(
            service,
            "missing-session",
            new("primitive-trip-basics-form", new Dictionary<string, string>(StringComparer.Ordinal)));

        Assert.Equal("planning_session_unknown", response.Status);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal("PCH_UI_PLANNING_SESSION_UNKNOWN", response.State.ErrorCode);
        Assert.Equal("planning_session_unknown", response.State.BlockedReason);
        AssertRawTextAbsent(JsonSerializer.Serialize(response));
    }

    [Fact]
    public void BrowserHelperUsesHttpTransportForDisconnectedLiveMode()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pch.UI", "ClientApp", "endUserChat.ts"));

        Assert.Contains("/api/planning/session/start", source, StringComparison.Ordinal);
        Assert.Contains("/api/planning/session/${encodeURIComponent(sessionId)}/answer", source, StringComparison.Ordinal);
        Assert.Contains("\"data-browser-transport\": \"http_api\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("no fallback provider request was attempted", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("could not send the live provider request", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidAnswerShowsValidationErrorWithoutAdvancingTurn()
    {
        var service = PlanningService();
        var result = await service.StartAsync(
            "Plan a live primitive form.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var turn = Assert.IsType<EndUserValidatedTurnView>(result.Turn);
        var form = Assert.Single(turn.Primitives, primitive => primitive.RendererKey == "form");
        var invalid = new PrimitiveAnswerDto(
            turn.SessionId,
            turn.TurnId,
            turn.GraphRevision,
            form.InstanceId,
            form.Fields.ToDictionary(field => field.FieldId, _ => string.Empty, StringComparer.Ordinal));

        var answered = await service.SubmitAnswer(result.State, turn, invalid);

        Assert.Equal("answer_validation_failed", answered.State.FinalState);
        Assert.Equal("PCH_UI_PRIMITIVE_ANSWER_INVALID", answered.State.ErrorCode);
        Assert.NotEmpty(answered.ValidationErrors);
        Assert.Equal(PlanningSessionService.AnswerValidationFailed, answered.Turn?.OutcomeCode);
    }

    [Fact]
    public void MoodAndMissingMediaResolveToSafeAssets()
    {
        var culture = PlanningSessionService.ResolveMedia("reflective_culture");
        var missing = PlanningSessionService.ResolveMedia("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST");

        Assert.Equal("mood-reflective-culture", PlanningSessionService.MoodCssClass("reflective_culture"));
        Assert.StartsWith("/media/japan-prompt-studio-pack/", culture.Path, StringComparison.Ordinal);
        Assert.Equal("prompt_studio_generated_local", culture.SourceClass);
        Assert.Equal("backdrop.cultural.craft_district.arts_design", missing.AssetId);
        Assert.Equal("fallback", missing.State);
    }

    [Fact]
    public async Task TaskRailUsesValidatedTurnData()
    {
        var service = PlanningService();
        var result = await service.StartAsync(
            "Plan a live primitive form.",
            EndUserModelRoleSelection.InHarnessActionGenerator);

        Assert.NotNull(result.Turn);
        Assert.Equal(result.Turn.Tasks.Select(task => task.TaskId), result.State.Tasks.Select(task => task.TaskId));
        var form = Assert.Single(result.Turn.Primitives, primitive => primitive.RendererKey == "form");
        Assert.Contains(result.State.Tasks, task => task.TaskId == $"task-{form.InstanceId}"
            && task.Title == form.Title
            && task.State == "needs_user");
    }

    [Fact]
    public async Task RawProviderSchemaAndKeyStringsStayOutOfPrimitiveState()
    {
        var service = PlanningService();
        var result = await service.StartAsync(
            "RAW_USER_PROMPT_SHOULD_NOT_LEAK sk-live-secret RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST",
            EndUserModelRoleSelection.InHarnessActionGenerator);

        AssertRawTextAbsent(JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task PromptVariantsProduceDifferentValidatedPrimitiveContentAndTrace()
    {
        var runner = new DynamicPromptPlannerPrimitiveRunner();
        var store = new PlanningSessionStore();
        var service = PlanningService(runner.RunAsync);

        var osaka = await store.StartAsync(
            service,
            new(
                "Plan a weird food-first Osaka trip with late night ramen, markets, and no temples.",
                EndUserModelRoleSelection.InHarnessActionGenerator));
        var iceland = await store.StartAsync(
            service,
            new(
                "Plan a quiet Iceland hiking trip focused on glaciers, hot springs, and early nights.",
                EndUserModelRoleSelection.InHarnessActionGenerator));

        var osakaJson = JsonSerializer.Serialize(osaka);
        var icelandJson = JsonSerializer.Serialize(iceland);

        Assert.Contains("Osaka food-first plan", osakaJson, StringComparison.Ordinal);
        Assert.Contains("late night ramen, markets, and no temples", osakaJson, StringComparison.Ordinal);
        Assert.Contains("Iceland quiet hiking plan", icelandJson, StringComparison.Ordinal);
        Assert.Contains("glaciers, hot springs, and early nights", icelandJson, StringComparison.Ordinal);
        Assert.NotEqual(
            osaka.Turn!.Primitives.Select(primitive => primitive.InstanceId),
            iceland.Turn!.Primitives.Select(primitive => primitive.InstanceId));
        Assert.NotEqual(osaka.Trace.Single().PrimitiveHash, iceland.Trace.Single().PrimitiveHash);
        Assert.DoesNotContain("2027-04-01", osakaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("balanced", osakaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("comfortable", osakaJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Japan", osakaJson, StringComparison.Ordinal);
        AssertRawTextAbsent(osakaJson);
        AssertRawTextAbsent(icelandJson);
    }

    [Fact]
    public async Task SubmittedAnswerValuesReachSecondProviderRequestAndSessionTrace()
    {
        var runner = new CapturingPlannerPrimitiveRunner();
        var store = new PlanningSessionStore();
        var service = PlanningService(runner.RunAsync);
        var started = await store.StartAsync(
            service,
            new(
                "Plan a weird food-first Osaka trip.",
                EndUserModelRoleSelection.InHarnessActionGenerator));
        var form = Assert.Single(started.Turn!.Primitives, primitive => primitive.RendererKey == "form");
        var values = form.Fields.ToDictionary(
            field => field.FieldId,
            _ => "osaka ramen markets no temples",
            StringComparer.Ordinal);

        var answered = await store.AnswerAsync(service, started.SessionId, new(form.InstanceId, values));

        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("osaka ramen markets no temples", runner.Requests[1].RuntimePrompt, StringComparison.Ordinal);
        Assert.Contains(form.Fields[0].FieldId, runner.Requests[1].RuntimePrompt, StringComparison.Ordinal);
        Assert.Equal("answered", answered.Status);
        Assert.Equal(2, answered.Trace.Count);
        Assert.Contains(form.Fields[0].FieldId, answered.Trace.Last().AnswerIds);
        Assert.Equal(values, answered.LastAnswer!.FieldValues);
        AssertRawTextAbsent(JsonSerializer.Serialize(answered));
    }

    [Fact]
    public async Task AcceptedAssistantMessageFollowupDoesNotRenderAsProviderFailure()
    {
        var callCount = 0;
        Task<PlannerModelResult> runner(
            PlannerModelRequest request,
            PlannerModelOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            callCount++;
            return callCount == 1
                ? AcceptedPlannerPrimitiveRun(request, options, cancellationToken)
                : Task.FromResult(ModelResult(
                    request,
                    [
                        Primitive(
                            "assistant_message",
                            "assistant_message",
                            "msg-followup",
                            "assistant-message",
                            null,
                            "lively_food",
                            "Exploring food and activities",
                            "The follow-up reflects the submitted answer values.")
                    ],
                    "request-planner-primitive-assistant-message"));
        }

        var store = new PlanningSessionStore();
        var service = PlanningService(runner);
        var started = await store.StartAsync(
            service,
            new("Plan a weird food-first Osaka trip.", EndUserModelRoleSelection.InHarnessActionGenerator));
        var form = Assert.Single(started.Turn!.Primitives, primitive => primitive.RendererKey == "form");
        var values = form.Fields.ToDictionary(
            field => field.FieldId,
            _ => "osaka ramen markets no temples",
            StringComparer.Ordinal);

        var answered = await store.AnswerAsync(service, started.SessionId, new(form.InstanceId, values));

        Assert.Equal("second_turn_attempted", answered.State.ProviderRequestState);
        Assert.Equal("planner_model_accepted", answered.State.ProviderOutcome);
        Assert.Equal("awaiting_user_input", answered.State.FinalState);
        var primitive = Assert.Single(answered.Turn!.Primitives);
        Assert.Equal("assistant_message", primitive.RendererKey);
        Assert.Null(primitive.ErrorCode);
        AssertRawTextAbsent(JsonSerializer.Serialize(answered));
    }

    [Fact]
    public async Task UnsafeModelAuthoredLabelsAreRedactedBeforeApiSerialization()
    {
        var store = new PlanningSessionStore();
        var service = PlanningService(UnsafePlannerPrimitiveRun);

        var response = await store.StartAsync(
            service,
            new("Plan a safe primitive.", EndUserModelRoleSelection.InHarnessActionGenerator));
        var serialized = JsonSerializer.Serialize(response);

        Assert.Contains("redacted", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", serialized, StringComparison.OrdinalIgnoreCase);
        AssertRawTextAbsent(serialized);
    }

    [Fact]
    public void LivePrimitivePlanningServiceDoesNotDependOnStaticLiveDefaults()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "Pch.UI",
            "Features",
            "EndUserChat",
            "PlanningSessionService.cs"));

        Assert.DoesNotContain("FixedLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FixedPrompt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultAnswers", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LivePrimitiveTasks", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskFromRef", source, StringComparison.Ordinal);
        Assert.DoesNotContain("primitive-trip-basics-form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SyntheticTripFactory.CreateSession", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateDeckRenderersSubmitPrimitiveAnswerDtoShape()
    {
        var primitiveRenderer = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "Pch.UI",
            "Features",
            "EndUserChat",
            "PrimitiveRenderer.razor"));
        var browserHelper = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "src",
            "Pch.UI",
            "ClientApp",
            "endUserChat.ts"));

        Assert.Contains("new PrimitiveAnswerDto(", primitiveRenderer, StringComparison.Ordinal);
        Assert.Contains("[\"candidate_id\"] = candidateId", primitiveRenderer, StringComparison.Ordinal);
        Assert.Contains("data-answer-choice", browserHelper, StringComparison.Ordinal);
        Assert.Contains("fieldValues.candidate_id = selectedCandidateId", browserHelper, StringComparison.Ordinal);
        Assert.Contains("submitPrimitiveAnswerViaHttp(answerSubmit, selectedChoice)", browserHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlPrimitiveRendererComponentsExistAndUseNativeControls()
    {
        var featureRoot = Path.Combine(RepoRoot(), "src", "Pch.UI", "Features", "EndUserChat");
        var clientSource = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Pch.UI", "ClientApp", "endUserChat.ts"));

        Assert.Contains("<select", File.ReadAllText(Path.Combine(featureRoot, "SelectPrimitive.razor")), StringComparison.Ordinal);
        Assert.Contains("type=\"radio\"", File.ReadAllText(Path.Combine(featureRoot, "RadioGroupPrimitive.razor")), StringComparison.Ordinal);
        Assert.Contains("type=\"date\"", File.ReadAllText(Path.Combine(featureRoot, "DateRangePrimitive.razor")), StringComparison.Ordinal);
        Assert.Contains("type=\"range\"", File.ReadAllText(Path.Combine(featureRoot, "SliderPrimitive.razor")), StringComparison.Ordinal);
        Assert.Contains("data-dom-renderer=\"candidate_deck\"", File.ReadAllText(Path.Combine(featureRoot, "CandidateDeckPrimitive.razor")), StringComparison.Ordinal);
        Assert.Contains("data-development-status-dock=\"trip\"", File.ReadAllText(Path.Combine(featureRoot, "DevelopmentStatusDock.razor")), StringComparison.Ordinal);
        Assert.Contains("data-task-source", File.ReadAllText(Path.Combine(featureRoot, "TaskDecompositionRail.razor")), StringComparison.Ordinal);
        Assert.Contains("data-dom-renderer=\"select\"", clientSource, StringComparison.Ordinal);
        Assert.Contains("data-dom-renderer=\"radio_group\"", clientSource, StringComparison.Ordinal);
        Assert.Contains("data-dom-renderer=\"date_range\"", clientSource, StringComparison.Ordinal);
        Assert.Contains("data-dom-renderer=\"slider\"", clientSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptedSessionClearsStaleProviderBlockedState()
    {
        var service = PlanningService();
        var blocked = service.CreateInitialState() with
        {
            FinalState = "provider_blocked",
            ErrorCode = "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            BlockedReason = "planner_model_malformed_json",
            Tasks =
            [
                new("task-live-intake", "Live provider blocked", "blocked", 20, "Blocked", [new("step-provider-blocked", "Live provider blocked", "blocked")], true)
            ]
        };
        var result = await service.StartAsync("Plan a live primitive form.", EndUserModelRoleSelection.InHarnessActionGenerator);
        var answer = service.BuildDefaultAnswer(result.Turn!);
        var answered = await service.SubmitAnswer(blocked, result.Turn!, answer);

        Assert.Equal("awaiting_user_input", answered.State.FinalState);
        Assert.Null(answered.State.ErrorCode);
        Assert.Null(answered.State.BlockedReason);
        Assert.DoesNotContain(answered.State.Tasks, task => task.Title.Contains("provider blocked", StringComparison.OrdinalIgnoreCase));
    }

    private static PlanningSessionService PlanningService(PlannerPrimitiveModelRunner? runner = null) =>
        new(LiveChatService(), new FormBuilder(), LiveEnvironment, runner ?? AcceptedPlannerPrimitiveRun);

    private static Task<PlannerModelResult> UnsafePlannerPrimitiveRun(
        PlannerModelRequest request,
        PlannerModelOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ModelResult(
            request,
            [
                Primitive(
                    "text_input",
                    "text_input",
                    "primitive-unsafe-label",
                    "text-input",
                    "/mission/purpose",
                    null,
                    "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST",
                    "<script>RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST</script>")
            ],
            "request-planner-primitive-unsafe"));
    }

    private static Task<PlannerModelResult> AcceptedPlannerPrimitiveRun(
        PlannerModelRequest request,
        PlannerModelOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ModelResult(
            request,
            [
                Primitive(
                    "text_input",
                    "text_input",
                    "primitive-destination-country",
                    "text-input",
                    "/mission/destination_country",
                    null,
                    "Destination",
                    "Confirm destination."),
                Primitive(
                    "date_range",
                    "date_range",
                    "primitive-trip-dates",
                    "date-range",
                    "/mission/start_date",
                    null,
                    "Dates",
                    "Confirm travel dates.")
            ],
            "request-planner-primitive-safe");

        return Task.FromResult(result);
    }

    private static PlannerModelResult ModelResult(
        PlannerModelRequest request,
        IReadOnlyList<PlannerPrimitiveInvocation> primitives,
        string requestId) =>
        new(
            request.Manifest.ManifestId,
            request.Manifest.ManifestVersion,
            request.Manifest.GraphRevision,
            request.Manifest.SessionId,
            PlannerModelOutputKind.CompositeForm,
            primitives,
            [new("task-planner-fixture", primitives.Select(primitive => primitive.InstanceId).ToArray(), "Fixture task", "Fixture task summary.")],
            WasRepaired: false,
            HasUnsafeValue: false,
            HasPromptSpecificContent: true,
            Duration: TimeSpan.FromMilliseconds(25),
            ResponseContentLength: 256,
            Provider: "mock",
            Model: "mock-planner-primitive",
            RequestId: requestId);

    private static PlannerPrimitiveInvocation Primitive(
        string primitiveId,
        string primitiveKind,
        string instanceId,
        string rendererKey,
        string? fieldPath,
        string? moodToken,
        string? label,
        string? promptText) =>
        new(
            primitiveId,
            primitiveKind,
            instanceId,
            rendererKey,
            fieldPath,
            moodToken,
            moodToken,
            [],
            [],
            ["evidence-planner-primitive"],
            [],
            [],
            label,
            promptText,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed class DynamicPromptPlannerPrimitiveRunner
    {
        public Task<PlannerModelResult> RunAsync(
            PlannerModelRequest request,
            PlannerModelOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt = request.RuntimePrompt ?? string.Empty;
            var primitive = prompt.Contains("Iceland", StringComparison.OrdinalIgnoreCase)
                ? Primitive(
                    "textarea",
                    "textarea",
                    "primitive-iceland-quiet-hiking",
                    "textarea",
                    "/mission/purpose",
                    "soft_nature",
                    "Iceland quiet hiking plan",
                    "Focus on glaciers, hot springs, and early nights.")
                : Primitive(
                    "text_input",
                    "text_input",
                    "primitive-osaka-food-first",
                    "text-input",
                    "/mission/purpose",
                    "lively_food",
                    "Osaka food-first plan",
                    "Prioritize late night ramen, markets, and no temples.");

            return Task.FromResult(ModelResult(request, [primitive], $"request-{primitive.InstanceId}"));
        }
    }

    private sealed class CountingPlannerPrimitiveRunner
    {
        private readonly List<string> _turnIds = [];

        public int CallCount => _turnIds.Count;

        public IReadOnlyList<string> TurnIds => _turnIds;

        public Task<PlannerModelResult> RunAsync(
            PlannerModelRequest request,
            PlannerModelOptions options,
            CancellationToken cancellationToken)
        {
            _turnIds.Add(request.TurnId);
            return AcceptedPlannerPrimitiveRun(request, options, cancellationToken);
        }
    }

    private sealed class CapturingPlannerPrimitiveRunner
    {
        private readonly List<PlannerModelRequest> _requests = [];
        private readonly PlannerPrimitiveModelRunner _inner;

        public IReadOnlyList<PlannerModelRequest> Requests => _requests;

        public CapturingPlannerPrimitiveRunner(PlannerPrimitiveModelRunner? inner = null)
        {
            _inner = inner ?? AcceptedPlannerPrimitiveRun;
        }

        public Task<PlannerModelResult> RunAsync(
            PlannerModelRequest request,
            PlannerModelOptions options,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return _inner(request, options, cancellationToken);
        }
    }

    private static EndUserChatService LiveChatService()
    {
        var proposal = new ProviderMissionProposalMirror(
            "proposal-live-safe",
            [
                new("/mission/purpose", "vacation", "user", ["evidence-live-purpose"]),
                new("/mission/destination_country", "Japan", "user", ["evidence-live-destination"]),
                new("/mission/start_date", "2026-10-05", "user", ["evidence-live-dates"]),
                new("/mission/end_date", "2026-10-12", "user", ["evidence-live-dates"])
            ],
            [],
            []);
        var gateway = new EndUserLiveProposalGateway(proposal, LiveTurnRunner.OutcomeAccepted, null);
        return new EndUserChatService(
            new GoldenTurnTraceRunner(),
            new ModelRoleStatusEvaluator(new Pch.Providers.Mock.MockModelRoleStatusSource()),
            new EndUserLiveModelTurnService(
                LiveEnvironment,
                new LivePreflightEvaluator(new LivePreflightRunner(new PreflightCompletionClient(), new PreflightCreditClient())),
                gateway));
    }

    private static IReadOnlyDictionary<string, string?> LiveEnvironment() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PCH_LIVE_MODEL_ENABLED"] = "true",
            ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true",
            ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b"
        };

    private static void AssertRawTextAbsent(string serialized)
    {
        Assert.DoesNotContain("RAW_USER_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("json_schema", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "src", "Pch.UI")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class PreflightCompletionClient : IModelCompletionClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            var content = JsonSerializer.Serialize(new
            {
                packetId = "packet-end-user-live-preflight",
                roles = new[]
                {
                    new
                    {
                        role = "in_harness_action_generator",
                        probeId = "probe-in-harness-action-generator",
                        modelId = "qwen/qwen3-14b",
                        outputKind = "structured_output_ready"
                    }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            return Task.FromResult(new ModelCompletionResponse(
                "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-preflight-safe"));
        }
    }

    private sealed class PreflightCreditClient : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(1m, 0m, 1m, IsExhausted: false));
    }
}
