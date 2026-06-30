using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class LivePreflightTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string RawCompletion = "RAW_COMPLETION_SHOULD_NOT_PERSIST";
    private const string ApiKey = "sk-api-key-should-not-persist";
    private const string Credential = "CREDENTIAL_SHOULD_NOT_PERSIST";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string HoldReference = "HOLD_REFERENCE_SHOULD_NOT_PERSIST";
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
        CandidateDisplay,
        RawException,
        SecretSentinel
    ];

    [Fact]
    public async Task AcceptedStructuredOutputRowsPersistOnlySafeRoleMetadata()
    {
        var client = new StaticCompletionClient(CreateContent("packet-preflight"));
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase("preflight-ready", CreatePacket())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal("preflight-ready", row.Name);
        Assert.Equal("packet-preflight", row.PacketId);
        Assert.Equal(LivePreflightRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Equal(2, row.RoleCount);
        Assert.Equal(2, row.AcceptedRoleCount);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.Contains(row.Roles, role => role is
        {
            Role: LiveModelRole.InHarnessActionGenerator,
            ProbeId: "probe-in-harness",
            ModelId: "qwen/qwen3-14b",
            ProviderKind: LivePreflightProviderKind.OpenRouter,
            OutputKind: "structured_output_ready"
        });
        Assert.Contains(row.Roles, role => role.Role == LiveModelRole.StrongPlanner);
        Assert.Equal("live_preflight_probe", client.LastRequest?.JsonSchemaName);
        Assert.Contains("in_harness_action_generator", client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);
        Assert.Contains("strong_planner", client.LastRequest.Messages.Last().Content, StringComparison.Ordinal);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(client.LastRequest.Messages, SensitiveSentinels);
    }

    [Theory]
    [InlineData("disabled", "live_preflight_disabled", null)]
    [InlineData("key_missing", "live_preflight_key_missing", null)]
    [InlineData("schema_unsupported", "live_preflight_schema_unsupported", null)]
    [InlineData("fallback_disabled", "live_preflight_fallback_disabled", "fallback_disabled")]
    public async Task GuardedConfigRowsBlockBeforeProviderCall(
        string guard,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var client = new CountingCompletionClient(CreateContent("packet-preflight"));
        var options = guard switch
        {
            "disabled" => CreateOptions(enabled: false),
            "key_missing" => CreateOptions(apiKeyAvailable: false),
            "schema_unsupported" => CreateOptions(structuredOutputSupported: false),
            _ => CreateOptions(allowPaidProviderFallback: true)
        };
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            options));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CreditExhaustedBlocksWithoutCompletionCall()
    {
        var client = new CountingCompletionClient(CreateContent("packet-preflight"));
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(
            client,
            new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LivePreflightRunner.OutcomeCreditExhausted, "credit_exhausted");
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("empty", "live_preflight_empty_content", "empty_content")]
    [InlineData("malformed", "live_preflight_malformed_json", "malformed_schema")]
    [InlineData("timeout", "live_preflight_timeout", "timeout")]
    [InlineData("provider", "live_preflight_provider_unavailable", "provider_error")]
    public async Task ProviderFailuresMapToFixedRows(
        string failure,
        string expectedOutcome,
        string expectedErrorCode)
    {
        IModelCompletionClient client = failure switch
        {
            "empty" => new ThrowingCompletionClient(new ProviderEmptyResponseException("openrouter", $"{RawProviderPayload} empty")),
            "malformed" => new ThrowingCompletionClient(new ProviderMalformedResponseException("openrouter", $"{RawCompletion} malformed")),
            "timeout" => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", "OpenRouter request timed out.")),
            _ => new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", $"{RawException} {ApiKey}"))
        };
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("{\"packetId\":\"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST\",\"roles\":[]}", "live_preflight_packet_mismatch", null)]
    [InlineData("not-json", "live_preflight_malformed_json", "malformed_schema")]
    [InlineData("{\"packetId\":\"packet-preflight\",\"roles\":[]}", "live_preflight_malformed_json", "malformed_schema")]
    [InlineData("{\"packetId\":\"packet-preflight\",\"roles\":[{\"role\":\"in_harness_action_generator\",\"probeId\":\"probe-in-harness\",\"modelId\":\"wrong-model\",\"outputKind\":\"structured_output_ready\"}]}", "live_preflight_malformed_json", "malformed_schema")]
    public async Task MalformedOrMismatchedContentProducesSanitizedRows(
        string content,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(
                [new LivePreflightEvalCase("cancelled", CreatePacket())],
                CreateOptions(timeout: TimeSpan.FromSeconds(30)),
                cts.Token));
    }

    [Fact]
    public async Task RunnerTimeoutMapsToFixedTimeout()
    {
        var evaluator = new LivePreflightEvaluator(new LivePreflightRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LivePreflightEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(timeout: TimeSpan.FromMilliseconds(1))));

        AssertRejected(row, LivePreflightRunner.OutcomeTimeout, "timeout");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public void OptionsLoadSafeEnvControlsWithoutSecrets()
    {
        var options = LivePreflightOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_LIVE_MODEL_ENABLED"] = "true",
            ["OPENROUTER_API_KEY"] = ApiKey,
            ["PCH_LIVE_MODEL_PROVIDER"] = "grok-xai",
            ["PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"] = "true",
            ["PCH_LIVE_MODEL_TIMEOUT_SECONDS"] = "11",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b",
            ["PCH_LIVE_STRONG_PLANNER_MODEL"] = "openai/strong-planner"
        });

        Assert.True(options.Enabled);
        Assert.True(options.ApiKeyAvailable);
        Assert.False(options.CreditGuardEnabled);
        Assert.Equal(LivePreflightProviderKind.GrokXAi, options.ProviderKind);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Timeout);
        Assert.Equal("qwen/qwen3-14b", options.InHarnessModelId);
        Assert.Equal("openai/strong-planner", options.StrongPlannerModelId);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(options, [ApiKey]);
    }

    private static LivePreflightPacket CreatePacket(bool requiresFallback = false) =>
        new(
            "packet-preflight",
            [
                new LivePreflightRoleProbe(LiveModelRole.InHarnessActionGenerator, "probe-in-harness", requiresFallback),
                new LivePreflightRoleProbe(LiveModelRole.StrongPlanner, "probe-strong-planner")
            ],
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {CandidateDisplay} {ApprovalToken} {HoldReference} {Credential} {SecretSentinel}");

    private static LivePreflightOptions CreateOptions(
        bool enabled = true,
        bool apiKeyAvailable = true,
        bool structuredOutputSupported = true,
        bool allowPaidProviderFallback = false,
        TimeSpan? timeout = null) =>
        new(
            Enabled: enabled,
            ApiKeyAvailable: apiKeyAvailable,
            CreditGuardEnabled: true,
            StructuredOutputSupported: structuredOutputSupported,
            AllowPaidProviderFallback: allowPaidProviderFallback,
            Timeout: timeout,
            ProviderKind: LivePreflightProviderKind.OpenRouter,
            Provider: "openrouter",
            InHarnessModelId: "qwen/qwen3-14b",
            StrongPlannerModelId: "qwen/qwen3-14b");

    private static string CreateContent(string packetId) =>
        JsonSerializer.Serialize(new
        {
            packetId,
            roles = new[]
            {
                new
                {
                    role = "in_harness_action_generator",
                    probeId = "probe-in-harness",
                    modelId = "qwen/qwen3-14b",
                    outputKind = "structured_output_ready"
                },
                new
                {
                    role = "strong_planner",
                    probeId = "probe-strong-planner",
                    modelId = "qwen/qwen3-14b",
                    outputKind = "structured_output_ready"
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedLivePreflightEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(LivePreflightRunner.RejectedRowName, row.Name);
        Assert.Equal(LivePreflightRunner.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Empty(row.Roles);
        Assert.Equal(0, row.RoleCount);
        Assert.Equal(0, row.AcceptedRoleCount);
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

    private sealed class StaticCreditClient(ProviderCreditStatus? status = null) : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status ?? new ProviderCreditStatus(40, 1, 39, IsExhausted: false));
    }
}
