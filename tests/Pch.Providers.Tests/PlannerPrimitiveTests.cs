using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;
using Pch.Providers.PlannerPrimitives;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class PlannerPrimitiveTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string RawCompletion = "RAW_COMPLETION_SHOULD_NOT_PERSIST";
    private const string ApiKey = "sk-api-key-should-not-persist";
    private const string Credential = "CREDENTIAL_SHOULD_NOT_PERSIST";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string HoldReference = "HOLD_REFERENCE_SHOULD_NOT_PERSIST";
    private const string BookingReference = "BOOKING_REFERENCE_SHOULD_NOT_PERSIST";
    private const string PaymentReference = "PAYMENT_REFERENCE_SHOULD_NOT_PERSIST";
    private const string CandidateDisplay = "CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST";
    private const string RawException = "RAW_EXCEPTION_TEXT_SHOULD_NOT_PERSIST";
    private const string SecretSentinel = "SECRET_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        RawCompletion,
        ApiKey,
        Credential,
        ApprovalToken,
        HoldReference,
        BookingReference,
        PaymentReference,
        CandidateDisplay,
        RawException,
        SecretSentinel
    ];

    [Fact]
    public async Task AcceptedCompositeFormPersistsOnlyPrimitiveMetadata()
    {
        var client = new StaticCompletionClient(CreateContent("composite_form"));
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("primitive-form", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal("primitive-form", row.Name);
        Assert.Equal("planner-run", row.RunId);
        Assert.Equal("planner-turn-01", row.TurnId);
        Assert.Equal("manifest-intake", row.ManifestId);
        Assert.Equal("v1", row.ManifestVersion);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Null(row.FailureClassCode);
        Assert.Equal(PlannerModelOutputKind.CompositeForm, row.OutputKind);
        Assert.Equal(2, row.PrimitiveCount);
        Assert.Contains("assistant_message", row.PrimitiveIds);
        Assert.Contains("text_input", row.PrimitiveIds);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.NotNull(row.DurationMilliseconds);
        Assert.NotNull(row.DurationBucket);
        Assert.Equal("planner_primitive_output", client.LastRequest?.JsonSchemaName);
        Assert.DoesNotContain(RawPrompt, client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(client.LastRequest.Messages, SensitiveSentinels);
    }

    [Theory]
    [InlineData("tool_search_request", "planner_model_tool_search_requested", PlannerModelOutputKind.ToolSearchRequest)]
    [InlineData("tool_gap_request", "planner_model_tool_gap_requested", PlannerModelOutputKind.ToolGapRequest)]
    public async Task ToolSearchAndGapOutputsAreAcceptedAsNonMutatingRows(
        string outputKind,
        string expectedOutcome,
        PlannerModelOutputKind expectedKind)
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent(outputKind)),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("tool-request", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedKind, row.OutputKind);
        Assert.Equal(1, row.PrimitiveCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task MalformedJsonThenRepairAcceptedUsesFixedRepairOutcome()
    {
        var client = new SequentialCompletionClient("not-json", CreateContent("composite_form"));
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("repair-form", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.True(row.WasRepaired);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeRepairedJson, row.OutcomeCode);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("repairAttempt", client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsupportedPrimitiveBlocksWithoutProviderMetadata()
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent("composite_form", primitiveId: "arbitrary_html")),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeUnsupportedPrimitive, "unsupported_primitive");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("key_missing", "planner_model_key_missing", "provider_key_missing")]
    [InlineData("credit", "planner_model_credit_exhausted", "provider_credit_exhausted")]
    [InlineData("timeout", "planner_model_timeout", "provider_timeout")]
    [InlineData("empty", "planner_model_empty_content", "provider_empty_content")]
    [InlineData("schema", "planner_model_schema_invalid", "provider_schema_invalid")]
    [InlineData("unavailable", "planner_model_provider_unavailable", "provider_http_5xx")]
    public async Task ProviderFailuresMapToFixedOutcomes(
        string failure,
        string expectedOutcome,
        string expectedFailureClass)
    {
        IModelCompletionClient client = failure switch
        {
            "key_missing" => new CountingCompletionClient(CreateContent("composite_form")),
            "credit" => new CountingCompletionClient(CreateContent("composite_form")),
            "timeout" => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", "OpenRouter request timed out.")),
            "empty" => new ThrowingCompletionClient(new ProviderEmptyResponseException("openrouter", $"{RawProviderPayload} empty")),
            "schema" => new StaticCompletionClient("{\"manifestId\":\"manifest-intake\"}"),
            _ => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", $"{RawException} unavailable", 500))
        };
        var creditClient = failure == "credit"
            ? new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))
            : new StaticCreditClient();
        var options = failure == "key_missing"
            ? CreateOptions(apiKeyAvailable: false)
            : CreateOptions();
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, creditClient));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            options));

        AssertRejected(row, expectedOutcome, expectedFailureClass);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task MalformedJsonAfterRepairMapsToFixedMalformedOutcome()
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new SequentialCompletionClient("not-json", "still-not-json"),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeMalformedJson, "provider_malformed_json");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsafeRuntimeValuesAreRejectedAndJsonIgnored()
    {
        var runner = new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent(
                "composite_form",
                label: CandidateDisplay,
                promptText: RawProviderPayload)),
            new StaticCreditClient());

        var result = await runner.RunAsync(CreateRequest(), CreateOptions());
        var row = Assert.Single(await new PlannerPrimitiveEvaluator(runner).EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            CreateOptions()));

        Assert.True(result.HasUnsafeValue);
        AssertRejected(row, PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(result, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public void OptionsLoadSafeEnvControlsWithoutSecrets()
    {
        var options = PlannerModelOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_PLANNER_PRIMITIVE_ENABLED"] = "true",
            ["OPENROUTER_API_KEY"] = ApiKey,
            ["PCH_LIVE_MODEL_PROVIDER"] = "openai",
            ["PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"] = "true",
            ["PCH_LIVE_MODEL_TIMEOUT_SECONDS"] = "17",
            ["PCH_PLANNER_PRIMITIVE_MODEL"] = "gpt-4.1-mini"
        });

        Assert.True(options.Enabled);
        Assert.True(options.ApiKeyAvailable);
        Assert.False(options.CreditGuardEnabled);
        Assert.Equal(TimeSpan.FromSeconds(17), options.Timeout);
        Assert.Equal("gpt-4.1-mini", options.Model);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(options, [ApiKey]);
    }

    private static PlannerModelRequest CreateRequest() =>
        new(
            "planner-run",
            "planner-turn-01",
            new PlannerToolManifestMirror(
                "manifest-intake",
                "v1",
                "graph-01",
                "session-01",
                "mission_intake",
                [
                    new PlannerPrimitiveDefinition("assistant_message", "assistant_message", "assistant_message"),
                    new PlannerPrimitiveDefinition("text_input", "text_input", "text_input"),
                    new PlannerPrimitiveDefinition("tool_search_request", "tool_search_request", "tool_search_request"),
                    new PlannerPrimitiveDefinition("tool_gap_request", "tool_gap_request", "tool_gap_request")
                ],
                ["/mission/purpose", "/mission/date_window"],
                ["neutral", "calm_morning", "logistics"],
                4),
            "en-US",
            $"{RawPrompt} {Credential}",
            "prompt-digest-safe");

    private static PlannerModelOptions CreateOptions(bool apiKeyAvailable = true) =>
        new(
            Enabled: true,
            ApiKeyAvailable: apiKeyAvailable,
            CreditGuardEnabled: true,
            Timeout: TimeSpan.FromSeconds(30),
            Provider: "openrouter",
            Model: "qwen/qwen3-14b");

    private static string CreateContent(
        string outputKind,
        string primitiveId = "assistant_message",
        string label = "Safe label",
        string promptText = "Safe question") =>
        JsonSerializer.Serialize(new
        {
            manifestId = "manifest-intake",
            manifestVersion = "v1",
            graphRevision = "graph-01",
            sessionId = "session-01",
            outputKind,
            primitives = outputKind switch
            {
                "tool_search_request" => new[]
                {
                    new
                    {
                        primitiveId = "tool_search_request",
                        primitiveKind = "tool_search_request",
                        instanceId = "primitive-search",
                        rendererKey = "tool_search_request",
                        fieldPath = string.Empty,
                        moodToken = "neutral",
                        candidateIds = Array.Empty<string>(),
                        label,
                        promptText
                    }
                },
                "tool_gap_request" => new[]
                {
                    new
                    {
                        primitiveId = "tool_gap_request",
                        primitiveKind = "tool_gap_request",
                        instanceId = "primitive-gap",
                        rendererKey = "tool_gap_request",
                        fieldPath = string.Empty,
                        moodToken = "neutral",
                        candidateIds = Array.Empty<string>(),
                        label,
                        promptText
                    }
                },
                _ => new[]
                {
                    new
                    {
                        primitiveId,
                        primitiveKind = primitiveId,
                        instanceId = "primitive-message",
                        rendererKey = primitiveId,
                        fieldPath = string.Empty,
                        moodToken = "neutral",
                        candidateIds = Array.Empty<string>(),
                        label,
                        promptText
                    },
                    new
                    {
                        primitiveId = "text_input",
                        primitiveKind = "text_input",
                        instanceId = "primitive-purpose",
                        rendererKey = "text_input",
                        fieldPath = "/mission/purpose",
                        moodToken = "calm_morning",
                        candidateIds = Array.Empty<string>(),
                        label = "Safe purpose",
                        promptText = "Safe purpose question"
                    }
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedPlannerModelLogRow row,
        string expectedOutcome,
        string expectedFailureClass)
    {
        Assert.False(row.Passed);
        Assert.Equal(PlannerPrimitiveRunner.RejectedRowName, row.Name);
        Assert.Equal(PlannerPrimitiveRunner.RejectedRunId, row.RunId);
        Assert.Equal(PlannerPrimitiveRunner.RejectedTurnId, row.TurnId);
        Assert.Equal(PlannerPrimitiveRunner.RejectedManifestId, row.ManifestId);
        Assert.Equal(PlannerPrimitiveRunner.RejectedManifestVersion, row.ManifestVersion);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedFailureClass, row.FailureClassCode);
        Assert.Null(row.OutputKind);
        Assert.Empty(row.PrimitiveIds);
        Assert.Equal(0, row.PrimitiveCount);
        Assert.False(row.WasRepaired);
        Assert.Null(row.DurationMilliseconds);
        Assert.Null(row.DurationBucket);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class StaticCompletionClient(string content) : IModelCompletionClient
    {
        public ModelCompletionRequest? LastRequest { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelCompletionResponse(
                request.Model ?? "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-safe"));
        }
    }

    private sealed class SequentialCompletionClient(params string[] contents) : IModelCompletionClient
    {
        public int CallCount { get; private set; }
        public ModelCompletionRequest? LastRequest { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var index = Math.Min(CallCount, contents.Length - 1);
            CallCount++;
            return Task.FromResult(new ModelCompletionResponse(
                request.Model ?? "qwen/qwen3-14b",
                contents[index],
                "openrouter",
                RequestId: "request-safe"));
        }
    }

    private sealed class CountingCompletionClient(string content) : IModelCompletionClient
    {
        public int CallCount { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ModelCompletionResponse(
                request.Model ?? "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-safe"));
        }
    }

    private sealed class ThrowingCompletionClient(Exception exception) : IModelCompletionClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelCompletionResponse>(exception);
    }

    private sealed class StaticCreditClient(ProviderCreditStatus? status = null) : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status ?? new ProviderCreditStatus(40, 1, 39, IsExhausted: false));
    }
}
