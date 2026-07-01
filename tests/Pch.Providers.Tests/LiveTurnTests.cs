using System.Net.Http;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LiveTurns;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class LiveTurnTests
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

    [Theory]
    [InlineData("mission_proposal", LiveTurnOutputKind.MissionProposal)]
    [InlineData("pending_confirmation_question", LiveTurnOutputKind.PendingConfirmationQuestion)]
    [InlineData("choice_set", LiveTurnOutputKind.ChoiceSet)]
    [InlineData("summary_fallback_notice", LiveTurnOutputKind.SummaryFallbackNotice)]
    public async Task AcceptedOutputKindsProduceSanitizedLogRows(
        string outputKind,
        LiveTurnOutputKind expectedOutputKind)
    {
        var client = new StaticCompletionClient(CreateContent(outputKind));
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase("turn-ready", CreatePacket())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal("turn-ready", row.Name);
        Assert.Equal("run-live", row.RunId);
        Assert.Equal("turn-01", row.TurnId);
        Assert.Equal("packet-live-turn", row.PacketId);
        Assert.Equal(LiveTurnRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Null(row.FailureClass);
        Assert.Null(row.FailureClassCode);
        Assert.Equal(expectedOutputKind, row.OutputKind);
        Assert.Equal(LiveModelRole.StrongPlanner, row.Role);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.NotNull(row.DurationMilliseconds);
        Assert.NotNull(row.DurationBucket);
        Assert.Equal("live_turn_output", client.LastRequest?.JsonSchemaName);
        Assert.DoesNotContain(RawPrompt, client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);

        if (expectedOutputKind == LiveTurnOutputKind.ChoiceSet)
        {
            Assert.Equal(2, row.CandidateCount);
            Assert.Contains("candidate-dining", row.CandidateIds);
            Assert.Contains(LiveTurnCandidateCategory.Dining, row.CandidateCategories);
        }
        else
        {
            Assert.Empty(row.CandidateIds);
            Assert.Empty(row.CandidateCategories);
        }

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(client.LastRequest.Messages, SensitiveSentinels);
    }

    [Theory]
    [InlineData("disabled", "live_turn_disabled", ProviderFailureClass.ProviderDisabled)]
    [InlineData("key_missing", "live_turn_key_missing", ProviderFailureClass.ProviderKeyMissing)]
    [InlineData("schema_unsupported", "live_turn_provider_schema_invalid", ProviderFailureClass.ProviderSchemaInvalid)]
    [InlineData("fallback_disabled", "live_turn_fallback_disabled", ProviderFailureClass.ProviderFallbackDisabled)]
    public async Task GuardFailuresDoNotCallProvider(
        string guard,
        string expectedOutcome,
        ProviderFailureClass expectedFailureClass)
    {
        var client = new CountingCompletionClient(CreateContent("summary_fallback_notice"));
        var options = guard switch
        {
            "disabled" => CreateOptions(enabled: false),
            "key_missing" => CreateOptions(apiKeyAvailable: false),
            "schema_unsupported" => CreateOptions(structuredOutputSupported: false),
            _ => CreateOptions(allowPaidProviderFallback: true)
        };
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket(requiresFallback: guard == "fallback_disabled"))],
            options));

        AssertRejected(row, expectedOutcome, expectedFailureClass);
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CreditExhaustedBlocksWithoutCompletionCall()
    {
        var client = new CountingCompletionClient(CreateContent("summary_fallback_notice"));
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            client,
            new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveTurnRunner.OutcomeCreditExhausted, ProviderFailureClass.ProviderCreditExhausted);
        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData(400, "live_turn_provider_http_4xx", ProviderFailureClass.ProviderHttp4xx)]
    [InlineData(429, "live_turn_provider_rate_limited", ProviderFailureClass.ProviderRateLimited)]
    [InlineData(500, "live_turn_provider_http_5xx", ProviderFailureClass.ProviderHttp5xx)]
    [InlineData(503, "live_turn_provider_upstream_model_unavailable", ProviderFailureClass.ProviderUpstreamModelUnavailable)]
    public async Task HttpProviderFailuresClassifySpecifically(
        int statusCode,
        string expectedOutcome,
        ProviderFailureClass expectedFailureClass)
    {
        var message = statusCode == 503 ? "upstream model unavailable" : $"{RawException} status";
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            new ThrowingCompletionClient(new ProviderUnavailableException("openrouter", message, statusCode)),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedFailureClass);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("timeout", "live_turn_provider_timeout", ProviderFailureClass.ProviderTimeout)]
    [InlineData("empty", "live_turn_provider_empty_content", ProviderFailureClass.ProviderEmptyContent)]
    [InlineData("malformed", "live_turn_provider_malformed_json", ProviderFailureClass.ProviderMalformedJson)]
    [InlineData("schema", "live_turn_provider_schema_invalid", ProviderFailureClass.ProviderSchemaInvalid)]
    [InlineData("network", "live_turn_provider_network_error", ProviderFailureClass.ProviderNetworkError)]
    [InlineData("unknown", "live_turn_provider_unknown_error", ProviderFailureClass.ProviderUnknownError)]
    public async Task ProviderFailureClassesUseFixedRows(
        string failure,
        string expectedOutcome,
        ProviderFailureClass expectedFailureClass)
    {
        Exception exception = failure switch
        {
            "timeout" => new ProviderUnavailableException("openrouter", "OpenRouter request timed out."),
            "empty" => new ProviderEmptyResponseException("openrouter", $"{RawProviderPayload} empty"),
            "malformed" => new ProviderMalformedResponseException("openrouter", $"{RawCompletion} malformed"),
            "schema" => new ProviderMalformedResponseException("openrouter", "provider schema invalid"),
            "network" => new ProviderUnavailableException("openrouter", "network", innerException: new HttpRequestException(RawException)),
            _ => new InvalidOperationException($"{RawException} {ApiKey}")
        };
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            new ThrowingCompletionClient(exception),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedFailureClass);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("not-json", "live_turn_provider_malformed_json")]
    [InlineData("{\"runId\":\"run-live\"}", "live_turn_provider_schema_invalid")]
    [InlineData("{\"runId\":\"run-live\",\"turnId\":\"turn-01\",\"packetId\":\"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST\",\"sessionId\":\"session-live\",\"role\":\"strong_planner\",\"outputKind\":\"summary_fallback_notice\",\"missionProposal\":null,\"pendingQuestion\":null,\"choiceSet\":null,\"summaryNotice\":{\"noticeKind\":\"summary\",\"summaryText\":\"safe\"}}", "live_turn_packet_mismatch")]
    [InlineData("{\"runId\":\"run-live\",\"turnId\":\"turn-01\",\"packetId\":\"packet-live-turn\",\"sessionId\":\"session-live\",\"role\":\"strong_planner\",\"outputKind\":\"pay_now\",\"missionProposal\":null,\"pendingQuestion\":null,\"choiceSet\":null,\"summaryNotice\":{\"noticeKind\":\"summary\",\"summaryText\":\"safe\"}}", "live_turn_unsupported_value")]
    public async Task MalformedAndMismatchedContentIsSanitized(string content, string expectedOutcome)
    {
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        Assert.False(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(LiveTurnRunner.RejectedRowName, row.Name);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("unknown_candidate")]
    [InlineData("category_mismatch")]
    [InlineData("duplicate_candidate")]
    public async Task ChoiceSetMustReferenceTrustedPacketCandidates(string mismatch)
    {
        var content = mismatch switch
        {
            "unknown_candidate" => CreateContent("choice_set", candidateId: "candidate-unknown"),
            "category_mismatch" => CreateContent("choice_set", category: "activity"),
            _ => CreateContent("choice_set", duplicateCandidate: true)
        };
        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveTurnRunner.OutcomePacketMismatch, ProviderFailureClass.ProviderSchemaInvalid);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsafeRuntimeTextIsJsonIgnoredAndRejected()
    {
        var runner = new LiveTurnRunner(
            new StaticCompletionClient(CreateContent(
                "choice_set",
                label: CandidateDisplay,
                rationale: RawProviderPayload,
                framingText: ApprovalToken)),
            new StaticCreditClient());

        var result = await runner.RunAsync(CreatePacket(), CreateOptions());
        var row = Assert.Single(await new LiveTurnEvaluator(runner).EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        Assert.True(result.HasUnsafeValue);
        AssertRejected(row, LiveTurnRunner.OutcomeUnsafeValueRedacted, ProviderFailureClass.ProviderSchemaInvalid);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(result, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellationKeepSemantics()
    {
        var timeoutRow = Assert.Single(await new LiveTurnEvaluator(new LiveTurnRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient())).EvaluateAsync(
            [new LiveTurnEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(timeout: TimeSpan.FromMilliseconds(1))));

        AssertRejected(timeoutRow, LiveTurnRunner.OutcomeProviderTimeout, ProviderFailureClass.ProviderTimeout);

        var evaluator = new LiveTurnEvaluator(new LiveTurnRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(
                [new LiveTurnEvalCase("cancelled", CreatePacket())],
                CreateOptions(timeout: TimeSpan.FromSeconds(30)),
                cts.Token));
    }

    [Fact]
    public void OptionsLoadSafeEnvControlsWithoutSecrets()
    {
        var options = LiveTurnOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_LIVE_TURN_ENABLED"] = "true",
            ["OPENROUTER_API_KEY"] = ApiKey,
            ["PCH_LIVE_MODEL_PROVIDER"] = "grok-xai",
            ["PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"] = "true",
            ["PCH_LIVE_MODEL_TIMEOUT_SECONDS"] = "19",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b",
            ["PCH_LIVE_STRONG_PLANNER_MODEL"] = "openai/strong-planner"
        });

        Assert.True(options.Enabled);
        Assert.True(options.ApiKeyAvailable);
        Assert.False(options.CreditGuardEnabled);
        Assert.Equal(TimeSpan.FromSeconds(19), options.Timeout);
        Assert.Equal("openai/strong-planner", options.StrongPlannerModelId);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(options, [ApiKey]);
    }

    private static LiveTurnPacket CreatePacket(bool requiresFallback = false) =>
        new(
            "run-live",
            "turn-01",
            "packet-live-turn",
            "session-live",
            LiveModelRole.StrongPlanner,
            "en-US",
            [
                LiveTurnOutputKind.MissionProposal,
                LiveTurnOutputKind.PendingConfirmationQuestion,
                LiveTurnOutputKind.ChoiceSet,
                LiveTurnOutputKind.SummaryFallbackNotice
            ],
            [
                new LiveTurnTrustedCandidate("candidate-dining", "slot-dinner", LiveTurnCandidateCategory.Dining),
                new LiveTurnTrustedCandidate("candidate-activity", "slot-morning", LiveTurnCandidateCategory.Activity)
            ],
            requiresFallback,
            $"{RawPrompt} {RawProviderPayload} {CandidateDisplay} {ApprovalToken} {HoldReference} {Credential} {SecretSentinel}");

    private static LiveTurnOptions CreateOptions(
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
            Provider: "openrouter",
            InHarnessModelId: "qwen/qwen3-14b",
            StrongPlannerModelId: "qwen/qwen3-14b");

    private static string CreateContent(
        string outputKind,
        string candidateId = "candidate-dining",
        string category = "dining",
        bool duplicateCandidate = false,
        string label = "Safe option",
        string rationale = "Safe rationale",
        string framingText = "Safe framing") =>
        JsonSerializer.Serialize(new
        {
            runId = "run-live",
            turnId = "turn-01",
            packetId = "packet-live-turn",
            sessionId = "session-live",
            role = "strong_planner",
            outputKind,
            missionProposal = outputKind == "mission_proposal"
                ? new
                {
                    missionKind = "vacation",
                    fields = new[]
                    {
                        new
                        {
                            fieldPath = "/mission/purpose",
                            value = "safe vacation",
                            authoritySource = "model_inference_pending_confirmation",
                            evidenceIds = new[] { "evidence-purpose" }
                        }
                    },
                    commitments = new[]
                    {
                        new
                        {
                            commitmentId = "commitment-travel",
                            commitmentKind = "travel",
                            title = "Safe travel plan",
                            startsAt = "2026-10-01T09:00:00Z",
                            endsAt = "2026-10-01T12:00:00Z",
                            location = "Safe airport",
                            isIrreversible = false,
                            requiresSpend = true,
                            priority = "normal",
                            authoritySource = "model_inference_pending_confirmation",
                            evidenceIds = new[] { "evidence-commitment" }
                        }
                    },
                    pendingConfirmations = new[]
                    {
                        new
                        {
                            confirmationId = "confirm-date",
                            fieldPath = "/mission/date_window",
                            reasonCode = "needs_date_confirmation",
                            authoritySource = "model_inference_pending_confirmation",
                            evidenceIds = new[] { "evidence-date" }
                        }
                    }
                }
                : null,
            pendingQuestion = outputKind == "pending_confirmation_question"
                ? new
                {
                    questionId = "question-date",
                    fieldPath = "/mission/date_window",
                    reasonCode = "needs_date_confirmation",
                    promptText = "Safe question"
                }
                : null,
            choiceSet = outputKind == "choice_set"
                ? new
                {
                    choiceSetId = "choice-dinner",
                    uiMood = "lively_food",
                    framingText,
                    options = duplicateCandidate
                        ? new[]
                        {
                            new { candidateId = "candidate-dining", slotId = "slot-dinner", category = "dining", label, rationale },
                            new { candidateId = "candidate-dining", slotId = "slot-dinner", category = "dining", label, rationale }
                        }
                        : new[]
                        {
                            new { candidateId, slotId = "slot-dinner", category, label, rationale },
                            new { candidateId = "candidate-activity", slotId = "slot-morning", category = "activity", label = "Safe activity", rationale = "Safe activity rationale" }
                        }
                }
                : null,
            summaryNotice = outputKind == "summary_fallback_notice"
                ? new
                {
                    noticeKind = "summary",
                    summaryText = "Safe summary"
                }
                : null
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedLiveTurnLogRow row,
        string expectedOutcome,
        ProviderFailureClass expectedFailureClass)
    {
        Assert.False(row.Passed);
        Assert.Equal(LiveTurnRunner.RejectedRowName, row.Name);
        Assert.Equal(LiveTurnRunner.RejectedRunId, row.RunId);
        Assert.Equal(LiveTurnRunner.RejectedTurnId, row.TurnId);
        Assert.Equal(LiveTurnRunner.RejectedPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedFailureClass, row.FailureClass);
        Assert.Equal(ProviderFailureClassifier.CodeFor(expectedFailureClass), row.FailureClassCode);
        Assert.Null(row.OutputKind);
        Assert.Null(row.Role);
        Assert.Empty(row.CandidateIds);
        Assert.Empty(row.CandidateCategories);
        Assert.Equal(0, row.CandidateCount);
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
