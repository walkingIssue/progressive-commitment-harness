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
        Assert.Equal(3, row.PrimitiveCount);
        Assert.Equal(1, row.TaskCount);
        Assert.Equal(2, row.OptionCount);
        Assert.Contains("assistant_message", row.PrimitiveIds);
        Assert.Contains("choice_card", row.PrimitiveIds);
        Assert.Contains("task_decomposition", row.PrimitiveIds);
        Assert.Contains("choice_card", row.PrimitiveKinds);
        Assert.Contains("task_decomposition", row.PrimitiveKinds);
        Assert.Contains("task-osaka-shape", row.TaskIds);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.NotNull(row.DurationMilliseconds);
        Assert.NotNull(row.DurationBucket);
        Assert.Equal("planner_primitive_output", client.LastRequest?.JsonSchemaName);
        Assert.Contains("Plan an Osaka ramen market trip", client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task AcceptedDynamicOutputCarriesRuntimeOptionsTasksAndTextInMemoryOnly()
    {
        var runner = new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent("composite_form")),
            new StaticCreditClient());

        var result = await runner.RunAsync(CreateRequest(), CreateOptions());
        var purpose = Assert.Single(result.Primitives, primitive => primitive.PrimitiveId == "choice_card");

        Assert.Contains("Osaka", purpose.Label, StringComparison.Ordinal);
        Assert.Contains("ramen", purpose.PromptText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, purpose.Options.Count);
        Assert.Contains(purpose.Options, option => option.OptionId == "late_ramen");
        Assert.Contains(result.Primitives, primitive => primitive.PrimitiveId == "task_decomposition");
        Assert.Single(result.Tasks);
        Assert.Equal("pending", result.Tasks[0].State);
        Assert.Equal(0, result.Tasks[0].Order);
        Assert.Contains("Osaka", result.Tasks[0].Title, StringComparison.Ordinal);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(result, SensitiveSentinels);
    }

    [Fact]
    public async Task AcceptedCompositeFormCanRunThroughOpenAiClientShape()
    {
        var client = new StaticCompletionClient(
            CreateContent("composite_form"),
            provider: "openai",
            model: "gpt-4.1-mini",
            requestId: "request-openai-safe");
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient(
            new ProviderCreditStatus(null, null, null, IsExhausted: false))));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("primitive-form-openai", CreateRequest())],
            CreateOptions(provider: "openai", model: "gpt-4.1-mini")));

        Assert.True(row.Passed);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Equal("openai", row.Provider);
        Assert.Equal("gpt-4.1-mini", row.Model);
        Assert.Equal("request-openai-safe", row.RequestId);
        Assert.Equal("gpt-4.1-mini", client.LastRequest?.Model);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task PromptBuilderIncludesRuntimePromptAnswersAndContextButRowsDoNotPersistThem()
    {
        var request = CreateRequest() with
        {
            RuntimePrompt = $"{RawPrompt} Plan an Osaka ramen trip.",
            SubmittedAnswers =
            [
                new PlannerSubmittedAnswer("answer-01", "/mission/purpose", "late ramen and no temples", "primitive-purpose")
            ],
            ContextToolResults =
            [
                new PlannerContextToolResult(
                    "mock_context_provider",
                    "context-osaka-food",
                    "dining",
                    "mock_context_provider",
                    ["evidence-safe"],
                    Title: "Osaka food note",
                    Summary: $"{RawProviderPayload} should redact")
            ]
        };
        var client = new StaticCompletionClient(CreateContent("composite_form"));
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", request)],
            CreateOptions()));

        Assert.True(row.Passed);
        var providerBody = client.LastRequest!.Messages.Last().Content;
        Assert.Contains(RawPrompt, providerBody, StringComparison.Ordinal);
        Assert.Contains("late ramen and no temples", providerBody, StringComparison.Ordinal);
        Assert.Contains("context-osaka-food", providerBody, StringComparison.Ordinal);
        Assert.Contains("context_redacted", providerBody, StringComparison.Ordinal);
        foreach (var primitiveId in PlannerPrimitiveToolCatalog.RequiredPrimitiveIds)
        {
            Assert.Contains(primitiveId, providerBody, StringComparison.Ordinal);
        }

        Assert.Contains("destination confirmation must use radio_group or select", providerBody, StringComparison.Ordinal);
        Assert.Contains("exact dates must use date or date_range", providerBody, StringComparison.Ordinal);
        Assert.Contains("pace should use select or radio_group", providerBody, StringComparison.Ordinal);
        Assert.Contains("slider and number_input must not emit options", providerBody, StringComparison.Ordinal);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
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
        Assert.Equal(1, row.TaskCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task GenericStaticOutputIsRejectedAsSchemaInvalid()
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateGenericContent()),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("generic-form", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task PromptABOutputsDifferInStructuralDimensions()
    {
        var osakaRunner = new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent("composite_form")),
            new StaticCreditClient());
        var icelandRunner = new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateIcelandContent()),
            new StaticCreditClient());
        var osaka = await osakaRunner.RunAsync(CreateRequest(), CreateOptions());
        var iceland = await icelandRunner.RunAsync(CreateIcelandRequest(), CreateOptions());

        Assert.NotEqual(
            osaka.Primitives.SelectMany(primitive => primitive.Options.Select(option => option.OptionId)).Order().ToArray(),
            iceland.Primitives.SelectMany(primitive => primitive.Options.Select(option => option.OptionId)).Order().ToArray());
        Assert.NotEqual(
            osaka.Primitives.Select(primitive => primitive.PrimitiveId).Order().ToArray(),
            iceland.Primitives.Select(primitive => primitive.PrimitiveId).Order().ToArray());
        Assert.NotEqual(
            osaka.Tasks.Select(task => task.TaskId).Order().ToArray(),
            iceland.Tasks.Select(task => task.TaskId).Order().ToArray());
    }

    [Theory]
    [InlineData("/mission/destination_country", "text_input")]
    [InlineData("/mission/pace", "text_input")]
    [InlineData("/mission/date_window", "text_input")]
    public async Task SemanticTextInputMisuseIsRejected(string fieldPath, string primitiveId)
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent(
                "composite_form",
                dynamicPrimitiveId: primitiveId,
                fieldPath: fieldPath)),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("semantic-misuse", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomePrimitiveRendererMismatch, "primitive_renderer_mismatch");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CompositeFormWithoutTaskDecompositionIsRejected()
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent("composite_form", includeTaskDecomposition: false)),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("missing-task-decomposition", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeTaskDecompositionMissing, "task_decomposition_missing");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SliderWithUntrustedBudgetFieldPathIsRejectedBeforeAcceptedResult()
    {
        var content = CreateBudgetSliderContent(
            fieldPath: "/constraints/budget_amount",
            includeRangeOptions: true,
            defaultValue: null);
        var runner = new PlannerPrimitiveRunner(new StaticCompletionClient(content), new StaticCreditClient());

        var ex = await Assert.ThrowsAsync<PlannerModelGuardException>(() =>
            runner.RunAsync(CreateRequest(), CreateOptions()));
        var row = Assert.Single(await new PlannerPrimitiveEvaluator(runner).EvaluateAsync(
            [new PlannerModelEvalCase("slider-budget-invalid-field", CreateRequest())],
            CreateOptions()));

        Assert.Equal(PlannerPrimitiveRunner.OutcomeFieldPathNotAllowed, ex.OutcomeCode);
        AssertRejected(row, PlannerPrimitiveRunner.OutcomeFieldPathNotAllowed, "field_path_not_allowed");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SliderWithRangeOptionsIsRejectedAsAnswerSchemaInvalid()
    {
        var content = CreateBudgetSliderContent(
            fieldPath: "/constraints/budget",
            includeRangeOptions: true,
            defaultValue: null);
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("slider-budget-invalid-schema", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeAnswerSchemaInvalid, "answer_schema_invalid");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SliderWithNumericDefaultAndAllowedFieldPathIsAccepted()
    {
        var content = CreateBudgetSliderContent(
            fieldPath: "/constraints/budget",
            includeRangeOptions: false,
            defaultValue: "2500");
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("slider-budget-valid", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Contains("slider", row.PrimitiveKinds);
        Assert.Contains("task_decomposition", row.PrimitiveKinds);
        Assert.NotEmpty(row.TaskIds);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task RunnerBlocksCompositeFormWithoutTaskDecompositionBeforeAcceptedResult()
    {
        var runner = new PlannerPrimitiveRunner(
            new StaticCompletionClient(CreateContent("composite_form", includeTaskDecomposition: false)),
            new StaticCreditClient());

        var ex = await Assert.ThrowsAsync<PlannerModelGuardException>(() =>
            runner.RunAsync(CreateRequest(), CreateOptions()));

        Assert.Equal(PlannerPrimitiveRunner.OutcomeTaskDecompositionMissing, ex.OutcomeCode);
        Assert.Equal("task_decomposition_missing", ex.FailureClassCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(
            new { ex.OutcomeCode, ex.FailureClassCode },
            SensitiveSentinels);
    }

    [Theory]
    [InlineData("field", "planner_model_field_path_not_allowed", "field_path_not_allowed")]
    [InlineData("tool", "planner_model_tool_not_allowed", "tool_not_allowed")]
    public async Task InvalidFieldPathAndToolRefsUseSpecificFixedOutcomes(
        string failure,
        string expectedOutcome,
        string expectedFailureClass)
    {
        var content = failure == "field"
            ? CreateContent("composite_form", fieldPath: "/mission/untrusted")
            : CreateContent("composite_form", toolContextRefs: ["untrusted_live_search"]);
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("invalid-dynamic-form", CreateRequest())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedFailureClass);
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

    [Theory]
    [InlineData("Here is the JSON:\n```json\n{0}\n```")]
    [InlineData("Provider note before JSON.\n{0}\nProvider note after JSON.")]
    public async Task WrappedJsonProviderContentIsAcceptedWithoutPersistingRawText(string wrapper)
    {
        var content = string.Format(wrapper, CreateContent("composite_form"));
        var client = new StaticCompletionClient(content);
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("wrapped-json", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Contains("task_decomposition", row.PrimitiveKinds);
        Assert.NotEmpty(row.TaskIds);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SemanticRepairAcceptedUsesFixedRepairOutcome()
    {
        var client = new SequentialCompletionClient(
            CreateContent("composite_form", includeTaskDecomposition: false),
            CreateContent("composite_form"));
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase("semantic-repair-form", CreateRequest())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.True(row.WasRepaired);
        Assert.Equal(PlannerPrimitiveRunner.OutcomeRepairedJson, row.OutcomeCode);
        Assert.Equal(2, client.CallCount);
        Assert.Contains("task_decomposition_missing", client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);
        Assert.Contains("task_decomposition", row.PrimitiveKinds);
        Assert.NotEmpty(row.TaskIds);
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
    [InlineData("rate", "planner_model_rate_limited", "provider_rate_limited")]
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
            "rate" => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", $"{RawException} rate", 429)),
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
    public async Task RunnerTimeoutDuringCompletionMapsToFixedTimeoutOutcome()
    {
        var evaluator = new PlannerPrimitiveEvaluator(new PlannerPrimitiveRunner(
            new CancellableCompletionClient(),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            CreateOptions() with { Timeout = TimeSpan.FromMilliseconds(1) }));

        AssertRejected(row, PlannerPrimitiveRunner.OutcomeTimeout, "provider_timeout");
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

        var ex = await Assert.ThrowsAsync<PlannerModelGuardException>(() =>
            runner.RunAsync(CreateRequest(), CreateOptions()));
        var row = Assert.Single(await new PlannerPrimitiveEvaluator(runner).EvaluateAsync(
            [new PlannerModelEvalCase($"{RawPrompt}-{Credential}", CreateRequest())],
            CreateOptions()));

        Assert.Equal(PlannerPrimitiveRunner.OutcomeUnsafeText, ex.OutcomeCode);
        AssertRejected(row, PlannerPrimitiveRunner.OutcomeUnsafeText, "unsafe_text");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(
            new { ex.OutcomeCode, ex.FailureClassCode },
            SensitiveSentinels);
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

    [Fact]
    public void OptionsUseOpenAiDefaultModelWhenProviderIsOpenAi()
    {
        var options = PlannerModelOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_PLANNER_PRIMITIVE_ENABLED"] = "true",
            ["OPENAI_API_KEY"] = ApiKey,
            ["PCH_LIVE_MODEL_PROVIDER"] = "openai"
        });

        Assert.Equal("openai", options.Provider);
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
                PlannerPrimitiveToolCatalog.CreateRequiredDefinitions(),
                [
                    "/mission/purpose",
                    "/mission/destination_country",
                    "/mission/date_window",
                    "/mission/start_date",
                    "/mission/end_date",
                    "/mission/pace",
                    "/mission/preferences",
                    "/constraints/pace",
                    "/constraints/budget"
                ],
                ["neutral", "calm_morning", "logistics", "lively_food"],
                8)
            {
                AllowedMediaTokens = ["neutral", "calm_morning", "logistics", "lively_food"],
                AllowedToolIds = ["mock_context_provider"]
            },
            "en-US",
            "Plan an Osaka ramen market trip with lively food options.",
            "prompt-digest-safe");

    private static PlannerModelRequest CreateIcelandRequest() =>
        CreateRequest() with
        {
            RuntimePrompt = "Plan a quiet Iceland hiking trip focused on glaciers and hot springs."
        };

    private static PlannerModelOptions CreateOptions(
        bool apiKeyAvailable = true,
        string provider = "openrouter",
        string model = "qwen/qwen3-14b") =>
        new(
            Enabled: true,
            ApiKeyAvailable: apiKeyAvailable,
            CreditGuardEnabled: true,
            Timeout: TimeSpan.FromSeconds(30),
            Provider: provider,
            Model: model);

    private static string CreateContent(
        string outputKind,
        string primitiveId = "assistant_message",
        string dynamicPrimitiveId = "choice_card",
        string label = "Osaka ramen planning",
        string promptText = "Shape the Osaka ramen and market plan.",
        string fieldPath = "/mission/preferences",
        IReadOnlyList<string>? toolContextRefs = null,
        bool includeTaskDecomposition = true) =>
        JsonSerializer.Serialize(new
        {
            manifestId = "manifest-intake",
            manifestVersion = "v1",
            graphRevision = "graph-01",
            sessionId = "session-01",
            outputKind,
            primitives = outputKind switch
            {
                "tool_search_request" => new object[]
                {
                    new
                    {
                        primitiveId = "tool_search_request",
                        primitiveKind = "tool_search_request",
                        instanceId = "primitive-search",
                        rendererKey = "tool_search_request",
                        fieldPath = string.Empty,
                        moodToken = "neutral",
                        mediaToken = "neutral",
                        candidateIds = Array.Empty<string>(),
                        taskRefs = new[] { "task-osaka-search" },
                        evidenceRefs = Array.Empty<string>(),
                        toolContextRefs = Array.Empty<string>(),
                        options = Array.Empty<object>(),
                        label,
                        promptText,
                        helpText = "Use a tool before claiming live facts.",
                        defaultValue = (string?)null,
                        rendererHints = new Dictionary<string, string> { ["layout"] = "notice", ["variant"] = "tool_search" }
                    }
                },
                "tool_gap_request" => new object[]
                {
                    new
                    {
                        primitiveId = "tool_gap_request",
                        primitiveKind = "tool_gap_request",
                        instanceId = "primitive-gap",
                        rendererKey = "tool_gap_request",
                        fieldPath = string.Empty,
                        moodToken = "neutral",
                        mediaToken = "neutral",
                        candidateIds = Array.Empty<string>(),
                        taskRefs = new[] { "task-osaka-gap" },
                        evidenceRefs = Array.Empty<string>(),
                        toolContextRefs = Array.Empty<string>(),
                        options = Array.Empty<object>(),
                        label,
                        promptText,
                        helpText = "Ask for context rather than inventing it.",
                        defaultValue = (string?)null,
                        rendererHints = new Dictionary<string, string> { ["layout"] = "notice", ["variant"] = "tool_gap" }
                    }
                },
                _ => CompositePrimitives(primitiveId, dynamicPrimitiveId, label, promptText, fieldPath, toolContextRefs, includeTaskDecomposition)
            },
            tasks = new[]
            {
                new
                {
                    taskId = outputKind == "tool_gap_request" ? "task-osaka-gap" : outputKind == "tool_search_request" ? "task-osaka-search" : "task-osaka-shape",
                    primitiveRefs = outputKind == "composite_form"
                        ? includeTaskDecomposition
                            ? new[] { "primitive-osaka-choice", "primitive-task-decomposition" }
                            : new[] { "primitive-osaka-choice" }
                        : outputKind == "tool_search_request"
                            ? new[] { "primitive-search" }
                            : new[] { "primitive-gap" },
                    title = "Shape the Osaka food-first plan",
                    summary = "Collect ramen, market, and constraint details before itinerary choices.",
                    state = "pending",
                    order = 0
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static object[] CompositePrimitives(
        string primitiveId,
        string dynamicPrimitiveId,
        string label,
        string promptText,
        string fieldPath,
        IReadOnlyList<string>? toolContextRefs,
        bool includeTaskDecomposition)
    {
        var primitives = new List<object>
        {
            new
            {
                primitiveId,
                primitiveKind = primitiveId,
                instanceId = "primitive-message",
                rendererKey = primitiveId,
                fieldPath = string.Empty,
                moodToken = "lively_food",
                mediaToken = "lively_food",
                candidateIds = Array.Empty<string>(),
                taskRefs = new[] { "task-osaka-shape" },
                evidenceRefs = Array.Empty<string>(),
                toolContextRefs = toolContextRefs ?? [],
                options = Array.Empty<object>(),
                label,
                promptText,
                helpText = "Keep the planning question specific to Osaka.",
                defaultValue = (string?)null,
                rendererHints = new Dictionary<string, string> { ["layout"] = "message", ["variant"] = "assistant" }
            },
            new
            {
                primitiveId = dynamicPrimitiveId,
                primitiveKind = dynamicPrimitiveId,
                instanceId = "primitive-osaka-choice",
                rendererKey = dynamicPrimitiveId,
                fieldPath,
                moodToken = "lively_food",
                mediaToken = "lively_food",
                candidateIds = Array.Empty<string>(),
                taskRefs = new[] { "task-osaka-shape" },
                evidenceRefs = Array.Empty<string>(),
                toolContextRefs = toolContextRefs ?? [],
                options = new object[]
                {
                    new
                    {
                        optionId = "late_ramen",
                        moodToken = "lively_food",
                        mediaToken = "lively_food",
                        toolContextRefs = Array.Empty<string>(),
                        label = "Late ramen",
                        summary = "Prioritize Osaka late-night ramen lanes."
                    },
                    new
                    {
                        optionId = "markets",
                        moodToken = "lively_food",
                        mediaToken = "lively_food",
                        toolContextRefs = Array.Empty<string>(),
                        label = "Market snacks",
                        summary = "Keep Osaka markets in the plan."
                    }
                },
                label = "Osaka food purpose",
                promptText = "Choose how the Osaka ramen and market plan should feel.",
                helpText = "Pick the food-first planning emphasis.",
                defaultValue = (string?)"late_ramen",
                rendererHints = new Dictionary<string, string> { ["layout"] = "choice", ["variant"] = "cards" }
            }
        };

        if (includeTaskDecomposition)
        {
            primitives.Add(new
            {
                primitiveId = "task_decomposition",
                primitiveKind = "task_decomposition",
                instanceId = "primitive-task-decomposition",
                rendererKey = "task_decomposition",
                fieldPath = string.Empty,
                moodToken = "logistics",
                mediaToken = "logistics",
                candidateIds = Array.Empty<string>(),
                taskRefs = new[] { "task-osaka-shape" },
                evidenceRefs = Array.Empty<string>(),
                toolContextRefs = toolContextRefs ?? [],
                options = Array.Empty<object>(),
                label = "Osaka planning tasks",
                promptText = "Break the Osaka food-first turn into concrete next steps.",
                helpText = "Use these tasks to drive the next planner turn.",
                defaultValue = (string?)null,
                rendererHints = new Dictionary<string, string> { ["layout"] = "tasks", ["variant"] = "compact" }
            });
        }

        return primitives.ToArray();
    }

    private static string CreateBudgetSliderContent(
        string fieldPath,
        bool includeRangeOptions,
        string? defaultValue) =>
        JsonSerializer.Serialize(new
        {
            manifestId = "manifest-intake",
            manifestVersion = "v1",
            graphRevision = "graph-01",
            sessionId = "session-01",
            outputKind = "composite_form",
            primitives = new object[]
            {
                new
                {
                    primitiveId = "select",
                    primitiveKind = "select",
                    instanceId = "select-destination-country",
                    rendererKey = "select",
                    fieldPath = "/mission/destination_country",
                    moodToken = "logistics",
                    mediaToken = "logistics",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-destination-selection" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = new object[]
                    {
                        Option("country_italy", "Italy"),
                        Option("country_portugal", "Portugal"),
                        Option("country_japan", "Japan")
                    },
                    label = "Osaka destination country",
                    promptText = "Confirm the Osaka destination country.",
                    helpText = "Choose the Osaka country before dates and budget.",
                    defaultValue = (string?)"country_japan",
                    rendererHints = new Dictionary<string, string> { ["layout"] = "select", ["variant"] = "compact" }
                },
                new
                {
                    primitiveId = "slider",
                    primitiveKind = "slider",
                    instanceId = "slider-budget",
                    rendererKey = "slider",
                    fieldPath,
                    moodToken = "logistics",
                    mediaToken = "logistics",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-budget-setting" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = includeRangeOptions
                        ? new object[]
                        {
                            Option("min_budget", "Minimum budget"),
                            Option("max_budget", "Maximum budget")
                        }
                        : Array.Empty<object>(),
                    label = "Osaka budget",
                    promptText = "Set a numeric Osaka planning budget.",
                    helpText = "Use a numeric Osaka budget value.",
                    defaultValue,
                    rendererHints = new Dictionary<string, string> { ["layout"] = "slider", ["variant"] = "budget" }
                },
                new
                {
                    primitiveId = "task_decomposition",
                    primitiveKind = "task_decomposition",
                    instanceId = "task-decomposition-main",
                    rendererKey = "task_decomposition",
                    fieldPath = string.Empty,
                    moodToken = "logistics",
                    mediaToken = "logistics",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-destination-selection", "task-budget-setting" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = Array.Empty<object>(),
                    label = "Osaka planning tasks",
                    promptText = "Break the Osaka destination and budget decisions into tasks.",
                    helpText = "Use these Osaka tasks for the planning rail.",
                    defaultValue = (string?)null,
                    rendererHints = new Dictionary<string, string> { ["layout"] = "tasks", ["variant"] = "compact" }
                }
            },
            tasks = new[]
            {
                new
                {
                    taskId = "task-destination-selection",
                    primitiveRefs = new[] { "select-destination-country", "task-decomposition-main" },
                    title = "Confirm Osaka destination",
                    summary = "Choose the Osaka destination country.",
                    state = "pending",
                    order = 0
                },
                new
                {
                    taskId = "task-budget-setting",
                    primitiveRefs = new[] { "slider-budget", "task-decomposition-main" },
                    title = "Set Osaka budget",
                    summary = "Collect numeric Osaka budget posture.",
                    state = "pending",
                    order = 1
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static object Option(string optionId, string label) =>
        new
        {
            optionId,
            moodToken = "logistics",
            mediaToken = "logistics",
            toolContextRefs = Array.Empty<string>(),
            label,
            summary = label
        };

    private static string CreateIcelandContent() =>
        JsonSerializer.Serialize(new
        {
            manifestId = "manifest-intake",
            manifestVersion = "v1",
            graphRevision = "graph-01",
            sessionId = "session-01",
            outputKind = "composite_form",
            primitives = new object[]
            {
                new
                {
                    primitiveId = "select",
                    primitiveKind = "select",
                    instanceId = "primitive-iceland-style",
                    rendererKey = "select",
                    fieldPath = "/mission/purpose",
                    moodToken = "calm_morning",
                    mediaToken = "calm_morning",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-iceland-shape" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = new object[]
                    {
                        new
                        {
                            optionId = "glacier_hikes",
                            moodToken = "calm_morning",
                            mediaToken = "calm_morning",
                            toolContextRefs = Array.Empty<string>(),
                            label = "Glacier hikes",
                            summary = "Center Iceland glacier walking and daylight pacing."
                        },
                        new
                        {
                            optionId = "hot_springs",
                            moodToken = "calm_morning",
                            mediaToken = "calm_morning",
                            toolContextRefs = Array.Empty<string>(),
                            label = "Hot springs",
                            summary = "Keep Iceland hot springs as restorative anchors."
                        }
                    },
                    label = "Iceland hiking style",
                    promptText = "Should Iceland lean more toward glaciers or hot springs?",
                    helpText = "Choose the quiet outdoors emphasis.",
                    defaultValue = (string?)"glacier_hikes",
                    rendererHints = new Dictionary<string, string> { ["layout"] = "select", ["variant"] = "compact" }
                },
                new
                {
                    primitiveId = "task_decomposition",
                    primitiveKind = "task_decomposition",
                    instanceId = "primitive-iceland-task-decomposition",
                    rendererKey = "task_decomposition",
                    fieldPath = string.Empty,
                    moodToken = "logistics",
                    mediaToken = "logistics",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-iceland-shape" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = Array.Empty<object>(),
                    label = "Iceland planning tasks",
                    promptText = "Break the Iceland hiking turn into concrete planning steps.",
                    helpText = "Use the tasks to guide glacier and hot spring planning.",
                    defaultValue = (string?)null,
                    rendererHints = new Dictionary<string, string> { ["layout"] = "tasks", ["variant"] = "compact" }
                }
            },
            tasks = new[]
            {
                new
                {
                    taskId = "task-iceland-shape",
                    primitiveRefs = new[] { "primitive-iceland-style", "primitive-iceland-task-decomposition" },
                    title = "Shape the Iceland hiking plan",
                    summary = "Choose glacier, hot spring, and quiet-night priorities.",
                    state = "pending",
                    order = 0
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string CreateGenericContent() =>
        JsonSerializer.Serialize(new
        {
            manifestId = "manifest-intake",
            manifestVersion = "v1",
            graphRevision = "graph-01",
            sessionId = "session-01",
            outputKind = "composite_form",
            primitives = new object[]
            {
                new
                {
                    primitiveId = "text_input",
                    primitiveKind = "text_input",
                    instanceId = "primitive-purpose",
                    rendererKey = "text_input",
                    fieldPath = "/mission/purpose",
                    moodToken = "neutral",
                    mediaToken = "neutral",
                    candidateIds = Array.Empty<string>(),
                    taskRefs = new[] { "task-generic" },
                    evidenceRefs = Array.Empty<string>(),
                    toolContextRefs = Array.Empty<string>(),
                    options = Array.Empty<object>(),
                    label = "Trip basics",
                    promptText = "Tell us the basics.",
                    helpText = "Add a few details.",
                    defaultValue = (string?)"trip-planning",
                    rendererHints = new Dictionary<string, string> { ["layout"] = "form_field" }
                }
            },
            tasks = new[]
            {
                new
                {
                    taskId = "task-generic",
                    primitiveRefs = new[] { "primitive-purpose" },
                    title = "Answer live planner form",
                    summary = "Generate planning options.",
                    state = "pending",
                    order = 0
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
        Assert.Empty(row.PrimitiveKinds);
        Assert.Empty(row.TaskIds);
        Assert.Equal(0, row.PrimitiveCount);
        Assert.Equal(0, row.TaskCount);
        Assert.Equal(0, row.OptionCount);
        Assert.False(row.WasRepaired);
        Assert.Null(row.DurationMilliseconds);
        Assert.Null(row.DurationBucket);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class StaticCompletionClient(
        string content,
        string provider = "openrouter",
        string model = "qwen/qwen3-14b",
        string requestId = "request-safe") : IModelCompletionClient
    {
        public ModelCompletionRequest? LastRequest { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelCompletionResponse(
                request.Model ?? model,
                content,
                provider,
                RequestId: requestId));
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

    private sealed class CancellableCompletionClient : IModelCompletionClient
    {
        public async Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ModelCompletionResponse(
                request.Model ?? "qwen/qwen3-14b",
                CreateContent("composite_form"),
                "openrouter",
                RequestId: "request-safe");
        }
    }

    private sealed class StaticCreditClient(ProviderCreditStatus? status = null) : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status ?? new ProviderCreditStatus(40, 1, 39, IsExhausted: false));
    }
}
