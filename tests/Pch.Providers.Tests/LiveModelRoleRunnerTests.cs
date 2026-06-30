using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class LiveModelRoleRunnerTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string RawError = "RAW_EXCEPTION_MESSAGE_SHOULD_NOT_PERSIST";
    private const string SecretSentinel = "SECRET_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        Credential,
        RawError,
        SecretSentinel
    ];

    [Fact]
    public async Task SuccessfulStructuredOutputUsesConfiguredRoleModelAndSanitizedRow()
    {
        var client = new StaticCompletionClient(CreateContent("packet-live", "harness_action"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase("live-success", CreatePacket())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal("live-success", row.Name);
        Assert.Equal("packet-live", row.PacketId);
        Assert.Equal(LiveModelRoleRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(LiveModelRole.InHarnessActionGenerator, row.Role);
        Assert.Equal("qwen/qwen3-14b", row.ModelId);
        Assert.Equal("harness_action", row.OutputKind);
        Assert.Equal(LiveModelUiMood.Logistics, row.UiMood);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.NotNull(row.ResponseContentLength);
        Assert.Equal("qwen/qwen3-14b", client.LastRequest?.Model);
        Assert.Equal("live_model_role_output", client.LastRequest?.JsonSchemaName);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task RegistryMapsStrongPlannerRoleToConfiguredModelId()
    {
        var client = new StaticCompletionClient(CreateContent("packet-live", "mission_plan"));
        var options = CreateOptions(new LiveModelRunnerOptions(
            LiveModeEnabled: true,
            ApiKeyAvailable: true,
            StrongPlannerModelId: "openai/gpt-strong-planner-test"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase("strong-planner", CreatePacket(
                role: LiveModelRole.StrongPlanner,
                allowedOutputKinds: ["mission_plan"]))],
            options));

        Assert.True(row.Passed);
        Assert.Equal(LiveModelRole.StrongPlanner, row.Role);
        Assert.Equal("openai/gpt-strong-planner-test", row.ModelId);
        Assert.Equal("openai/gpt-strong-planner-test", client.LastRequest?.Model);
    }

    [Theory]
    [InlineData(false, true, "live_model_disabled", null)]
    [InlineData(true, false, "live_model_key_missing", null)]
    public async Task ConfigBlockedRowsUseFixedIdentifiersWithoutCallingProvider(
        bool liveModeEnabled,
        bool apiKeyAvailable,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var client = new CountingCompletionClient(CreateContent("packet-live", "harness_action"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket(packetId: $"{RawPrompt}-{SecretSentinel}"))],
            CreateOptions(new LiveModelRunnerOptions(
                LiveModeEnabled: liveModeEnabled,
                ApiKeyAvailable: apiKeyAvailable))));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task FallbackDisabledBlocksWithoutProviderCall()
    {
        var client = new CountingCompletionClient(CreateContent("packet-live", "harness_action"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket(requiresFallback: true))],
            CreateOptions()));

        AssertRejected(row, LiveModelRoleRunner.OutcomeFallbackDisabled);
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CreditExhaustedBlocksWithoutCompletionCall()
    {
        var client = new CountingCompletionClient(CreateContent("packet-live", "harness_action"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            client,
            new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveModelRoleRunner.OutcomeCreditExhausted, "credit_exhausted");
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("timeout", "live_model_timeout", "timeout")]
    [InlineData("empty", "live_model_empty_content", "empty_content")]
    [InlineData("malformed", "live_model_malformed_schema", "malformed_schema")]
    [InlineData("provider", "live_model_provider_unavailable", "provider_error")]
    public async Task ProviderFailuresUseFixedRowsAndNoRawText(
        string failure,
        string expectedOutcome,
        string expectedErrorCode)
    {
        var client = failure switch
        {
            "timeout" => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", "OpenRouter request timed out.")),
            "empty" => new ThrowingCompletionClient(new ProviderEmptyResponseException("openrouter", $"{RawProviderPayload} empty")),
            "malformed" => new ThrowingCompletionClient(new ProviderMalformedResponseException("openrouter", $"{RawProviderPayload} malformed")),
            _ => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", $"{RawError} {Credential}"))
        };
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("not-json", "live_model_malformed_schema")]
    [InlineData("{\"packetId\":\"packet-live\",\"outputKind\":\"harness_action\"}", "live_model_malformed_schema")]
    [InlineData("{\"packetId\":\"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST\",\"outputKind\":\"harness_action\",\"arguments\":{},\"summary\":\"ok\"}", "live_model_packet_mismatch")]
    [InlineData("{\"packetId\":\"packet-live\",\"outputKind\":\"pay_now\",\"arguments\":{},\"summary\":\"ok\"}", "live_model_unsupported_output")]
    [InlineData("", "live_model_empty_content")]
    public async Task MalformedProviderContentMapsToSanitizedRows(
        string content,
        string expectedOutcome)
    {
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedOutcome switch
        {
            "live_model_malformed_schema" => "malformed_schema",
            "live_model_empty_content" => "empty_content",
            _ => null
        });
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsupportedUiMoodMapsToSafeEnumWithoutPersistingRawMoodText()
    {
        const string rawMood = $"{RawProviderPayload}-mood-prose";
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            new StaticCompletionClient(CreateContent("packet-live", "harness_action", rawMood)),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase("live-success", CreatePacket())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal(LiveModelUiMood.Unspecified, row.UiMood);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, [.. SensitiveSentinels, rawMood]);
    }

    [Fact]
    public async Task TimeoutDuringCreditGuardMapsToFixedTimeoutRow()
    {
        var client = new CountingCompletionClient(CreateContent("packet-live", "harness_action"));
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(client, new DelayedCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(new LiveModelRunnerOptions(
                LiveModeEnabled: true,
                ApiKeyAvailable: true,
                Timeout: TimeSpan.FromMilliseconds(1)))));

        AssertRejected(row, LiveModelRoleRunner.OutcomeTimeout, "timeout");
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task TimeoutDuringCompletionMapsToFixedTimeoutRow()
    {
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(new LiveModelRunnerOptions(
                LiveModeEnabled: true,
                ApiKeyAvailable: true,
                Timeout: TimeSpan.FromMilliseconds(1)))));

        AssertRejected(row, LiveModelRoleRunner.OutcomeTimeout, "timeout");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutProviderUnavailableMapping()
    {
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(
                [new LiveModelRunEvalCase("cancelled", CreatePacket())],
                CreateOptions(new LiveModelRunnerOptions(
                    LiveModeEnabled: true,
                    ApiKeyAvailable: true,
                    Timeout: TimeSpan.FromSeconds(10))),
                cts.Token));
    }

    [Fact]
    public async Task RuntimeArgumentsAreJsonIgnoredAndEvalRowsDoNotPersistRawArgumentValues()
    {
        var content = JsonSerializer.Serialize(new
        {
            packetId = "packet-live",
            outputKind = "harness_action",
            uiMood = "logistics",
            arguments = new { raw = RawProviderPayload, credential = Credential, sentinel = SecretSentinel },
            summary = "safe structured output"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var runner = new LiveModelRoleRunner(new StaticCompletionClient(content), new StaticCreditClient());

        var result = await runner.RunAsync(CreatePacket(), CreateOptions());
        var row = Assert.Single(await new LiveModelRunEvaluator(runner).EvaluateAsync(
            [new LiveModelRunEvalCase("live-success", CreatePacket())],
            CreateOptions()));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Arguments);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(result, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task MalformedApiInputsReturnFixedSanitizedRows()
    {
        var evaluator = new LiveModelRunEvaluator(new LiveModelRoleRunner(
            new CountingCompletionClient(CreateContent("packet-live", "harness_action")),
            new StaticCreditClient()));

        var nullCasesRow = Assert.Single(await evaluator.EvaluateAsync(null!, CreateOptions()));
        var nullEvalCaseRow = Assert.Single(await evaluator.EvaluateAsync([null], CreateOptions()));
        var nullPacketRow = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", null!)],
            CreateOptions()));
        var nullOptionsRow = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            null!));
        var nullRunnerOptionsRow = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveModelRunEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            new LiveModelRunOptions(null!)));

        foreach (var row in new[] { nullCasesRow, nullEvalCaseRow, nullPacketRow, nullOptionsRow, nullRunnerOptionsRow })
        {
            AssertRejected(row, LiveModelRoleRunner.OutcomeMalformedSchema);
            SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        }
    }

    [Fact]
    public void OptionsLoadSafeEnvControlsWithoutSecrets()
    {
        var options = LiveModelRunnerOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_LIVE_MODEL_ENABLED"] = "true",
            ["PCH_LIVE_MODEL_KEY_AVAILABLE"] = "true",
            ["PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"] = "true",
            ["PCH_LIVE_MODEL_FALLBACK_POLICY"] = "allow_same_provider",
            ["PCH_LIVE_MODEL_TIMEOUT_SECONDS"] = "17",
            ["PCH_LIVE_MODEL_PROVIDER"] = "openrouter",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b",
            ["PCH_LIVE_STRONG_PLANNER_MODEL"] = "openai/strong-planner-test"
        });

        Assert.True(options.LiveModeEnabled);
        Assert.True(options.ApiKeyAvailable);
        Assert.False(options.CreditGuardEnabled);
        Assert.Equal(LiveModelFallbackPolicy.AllowSameProvider, options.FallbackPolicy);
        Assert.Equal(TimeSpan.FromSeconds(17), options.Timeout);
        Assert.Equal("qwen/qwen3-14b", options.InHarnessModelId);
        Assert.Equal("openai/strong-planner-test", options.StrongPlannerModelId);
    }

    private static LiveModelRunPacket CreatePacket(
        string packetId = "packet-live",
        LiveModelRole role = LiveModelRole.InHarnessActionGenerator,
        IReadOnlyList<string>? allowedOutputKinds = null,
        bool requiresFallback = false) =>
        new(
            packetId,
            role,
            $"{RawPrompt} {Credential}",
            allowedOutputKinds ?? ["harness_action"],
            "en-US",
            requiresFallback,
            $"{RawProviderPayload} {SecretSentinel}");

    private static LiveModelRunOptions CreateOptions(LiveModelRunnerOptions? options = null) =>
        new(options ?? new LiveModelRunnerOptions(LiveModeEnabled: true, ApiKeyAvailable: true));

    private static string CreateContent(
        string packetId,
        string outputKind,
        string uiMood = "logistics") =>
        JsonSerializer.Serialize(new
        {
            packetId,
            outputKind,
            uiMood,
            arguments = new { accepted = true },
            summary = "safe structured output"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedLiveModelRunEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(LiveModelRoleRunner.RejectedRowName, row.Name);
        Assert.Equal(LiveModelRoleRunner.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Null(row.OutputKind);
        Assert.Null(row.UiMood);
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
                request.Model ?? "test-model",
                content,
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
                request.Model ?? "test-model",
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

    private sealed class DelayedCompletionClient : IModelCompletionClient
    {
        public async Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable delayed completion");
        }
    }

    private sealed class StaticCreditClient(
        ProviderCreditStatus? status = null) : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status ?? new ProviderCreditStatus(40, 1, 39, IsExhausted: false));
    }

    private sealed class DelayedCreditClient : IProviderCreditClient
    {
        public async Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable delayed credits");
        }
    }
}
