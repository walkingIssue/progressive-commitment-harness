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
            && primitive.Fields.Any(field => field.PrimitiveId == "date_range"));
        Assert.All(result.Turn.Primitives.SelectMany(primitive => primitive.Candidates), candidate =>
            Assert.Equal("validated_primitive", candidate.Source));
        AssertRawTextAbsent(JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task AnswerDtoGeneratedFromValidatedFormAndAcceptedBySession()
    {
        var service = PlanningService();
        var result = await service.StartAsync(
            "Plan a live primitive form.",
            EndUserModelRoleSelection.InHarnessActionGenerator);
        var turn = Assert.IsType<EndUserValidatedTurnView>(result.Turn);

        var answer = service.BuildDefaultAnswer(turn);
        var answered = service.SubmitAnswer(result.State, turn, answer);

        Assert.Empty(answered.ValidationErrors);
        Assert.NotNull(answered.LastAnswer);
        Assert.Equal("primitive-trip-basics-form", answered.LastAnswer.PrimitiveInstanceId);
        Assert.Equal("second_turn_attempted", answered.State.ProviderRequestState);
        Assert.Equal("second_validated_turn_rendered", answered.Turn?.OutcomeCode);
        Assert.Contains(answered.Turn!.Primitives, primitive => primitive.RendererKey == "candidate-deck");
        Assert.Contains(answered.State.Turns, turnItem => turnItem.TurnId == "turn-primitive-answer-submitted"
            && turnItem.OutcomeCode == PlanningSessionService.AnswerAccepted);
        AssertRawTextAbsent(JsonSerializer.Serialize(answered));
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

        var answered = service.SubmitAnswer(result.State, turn, invalid);

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
        Assert.Contains(result.State.Tasks, task => task.TaskId == "task-live-intake"
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

    private static PlanningSessionService PlanningService() =>
        new(LiveChatService(), new FormBuilder(), LiveEnvironment, AcceptedPlannerPrimitiveRun);

    private static Task<PlannerModelResult> AcceptedPlannerPrimitiveRun(
        PlannerModelRequest request,
        PlannerModelOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new PlannerModelResult(
            request.Manifest.ManifestId,
            request.Manifest.ManifestVersion,
            request.Manifest.GraphRevision,
            request.Manifest.SessionId,
            PlannerModelOutputKind.CompositeForm,
            [
                new(
                    "text_input",
                    "text_input",
                    "primitive-destination-country",
                    "text-input",
                    "/mission/destination_country",
                    null,
                    [],
                    "Destination",
                    "Confirm destination."),
                new(
                    "date_range",
                    "date_range",
                    "primitive-trip-dates",
                    "date-range",
                    "/mission/start_date",
                    null,
                    [],
                    "Dates",
                    "Confirm travel dates.")
            ],
            WasRepaired: false,
            HasUnsafeValue: false,
            Duration: TimeSpan.FromMilliseconds(25),
            ResponseContentLength: 256,
            Provider: "mock",
            Model: "mock-planner-primitive",
            RequestId: "request-planner-primitive-safe");

        return Task.FromResult(result);
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
