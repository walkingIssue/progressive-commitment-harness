using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LivePreflight;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class LiveMissionProposalTests
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
    public async Task AcceptedProposalPersistsOnlySafeMetadata()
    {
        var client = new StaticCompletionClient(CreateContent("packet-live-proposal", "session-live"));
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase("mission-proposal-ready", CreatePacket())],
            CreateOptions()));

        Assert.True(row.Passed);
        Assert.Equal("mission-proposal-ready", row.Name);
        Assert.Equal("packet-live-proposal", row.PacketId);
        Assert.Equal("session-live", row.SessionId);
        Assert.Equal(LiveMissionProposalRunner.OutcomeAccepted, row.OutcomeCode);
        Assert.Equal(LiveModelRole.StrongPlanner, row.Role);
        Assert.Equal("mission_proposal", row.OutputKind);
        Assert.Equal(LiveMissionKind.Vacation, row.MissionKind);
        Assert.Contains("/mission/purpose", row.FieldPaths);
        Assert.Contains(LiveMissionCommitmentKind.Travel, row.CommitmentKinds);
        Assert.Contains(LiveMissionPendingReason.NeedsDateConfirmation, row.PendingReasonCodes);
        Assert.Equal(2, row.FieldCount);
        Assert.Equal(1, row.CommitmentCount);
        Assert.Equal(1, row.PendingConfirmationCount);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.Equal("request-safe", row.RequestId);
        Assert.Equal("live_mission_proposal", client.LastRequest?.JsonSchemaName);
        Assert.DoesNotContain(RawPrompt, client.LastRequest!.Messages.Last().Content, StringComparison.Ordinal);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("disabled", "live_mission_proposal_disabled", null)]
    [InlineData("key_missing", "live_mission_proposal_key_missing", null)]
    [InlineData("schema_unsupported", "live_mission_proposal_schema_invalid", "schema_unsupported")]
    [InlineData("fallback_disabled", "live_mission_proposal_fallback_disabled", "fallback_disabled")]
    public async Task GuardRowsBlockBeforeProviderCall(
        string guard,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var client = new CountingCompletionClient(CreateContent("packet-live-proposal", "session-live"));
        var options = guard switch
        {
            "disabled" => CreateOptions(enabled: false),
            "key_missing" => CreateOptions(apiKeyAvailable: false),
            "schema_unsupported" => CreateOptions(structuredOutputSupported: false),
            _ => CreateOptions(allowPaidProviderFallback: true)
        };
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket(requiresFallback: guard == "fallback_disabled"))],
            options));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task CreditExhaustedBlocksWithoutCompletionCall()
    {
        var client = new CountingCompletionClient(CreateContent("packet-live-proposal", "session-live"));
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            client,
            new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveMissionProposalRunner.OutcomeCreditExhausted, "credit_exhausted");
        Assert.Equal(0, client.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("empty", "live_mission_proposal_empty_content", "empty_content")]
    [InlineData("malformed", "live_mission_proposal_malformed_json", "malformed_json")]
    [InlineData("timeout", "live_mission_proposal_timeout", "timeout")]
    [InlineData("provider", "live_mission_proposal_provider_unavailable", "provider_error")]
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
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(client, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("not-json", "live_mission_proposal_malformed_json", "malformed_json")]
    [InlineData("{\"packetId\":\"packet-live-proposal\"}", "live_mission_proposal_schema_invalid", "malformed_schema")]
    [InlineData("{\"packetId\":\"RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST\",\"sessionId\":\"session-live\",\"role\":\"strong_planner\",\"outputKind\":\"mission_proposal\",\"missionKind\":\"vacation\",\"fields\":[],\"commitments\":[],\"pendingConfirmations\":[]}", "live_mission_proposal_packet_mismatch", null)]
    [InlineData("{\"packetId\":\"packet-live-proposal\",\"sessionId\":\"session-live\",\"role\":\"strong_planner\",\"outputKind\":\"pay_now\",\"missionKind\":\"vacation\",\"fields\":[],\"commitments\":[],\"pendingConfirmations\":[]}", "live_mission_proposal_unsupported_value", "unsupported_output_kind")]
    [InlineData("{\"packetId\":\"packet-live-proposal\",\"sessionId\":\"session-live\",\"role\":\"strong_planner\",\"outputKind\":\"mission_proposal\",\"missionKind\":\"space_colony\",\"fields\":[],\"commitments\":[],\"pendingConfirmations\":[]}", "live_mission_proposal_schema_invalid", "malformed_schema")]
    public async Task MalformedOrUnsupportedContentProducesSanitizedRows(
        string content,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("role")]
    public async Task PacketSessionAndRoleMismatchDoNotEchoResultValues(string mismatch)
    {
        var content = mismatch == "session"
            ? CreateContent("packet-live-proposal", $"{RawProviderPayload}-session")
            : CreateContent("packet-live-proposal", "session-live", role: "in_harness_action_generator");
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveMissionProposalRunner.OutcomePacketMismatch);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData("bad_field_path")]
    [InlineData("bad_authority")]
    [InlineData("bad_commitment_kind")]
    [InlineData("bad_priority")]
    [InlineData("bad_pending")]
    public async Task UnsupportedProposalValuesProduceFixedRows(string unsupported)
    {
        var content = unsupported switch
        {
            "bad_field_path" => CreateContent("packet-live-proposal", "session-live", fieldPath: "/profile/name"),
            "bad_authority" => CreateContent("packet-live-proposal", "session-live", authoritySource: "provider_payload"),
            "bad_commitment_kind" => CreateContent("packet-live-proposal", "session-live", commitmentKind: "pay_now"),
            "bad_priority" => CreateContent("packet-live-proposal", "session-live", priority: "urgent_secret"),
            _ => CreateContent("packet-live-proposal", "session-live", reasonCode: "raw_prompt_review")
        };
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new StaticCompletionClient(content),
            new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        AssertRejected(row, LiveMissionProposalRunner.OutcomeUnsupportedValue, "unsupported_proposal_value");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsafeRuntimeValuesAreRejectedAndNotSerialized()
    {
        var runner = new LiveMissionProposalRunner(
            new StaticCompletionClient(CreateContent(
                "packet-live-proposal",
                "session-live",
                fieldValue: RawPrompt,
                title: CandidateDisplay,
                location: ApprovalToken,
                evidenceId: SecretSentinel)),
            new StaticCreditClient());

        var result = await runner.RunAsync(CreatePacket(), CreateOptions());
        var row = Assert.Single(await new LiveMissionProposalEvaluator(runner).EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions()));

        Assert.True(result.HasUnsafeValue);
        AssertRejected(row, LiveMissionProposalRunner.OutcomeUnsafeValueRedacted, "unsafe_value_redacted");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(result, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task TimeoutsAndCallerCancellationKeepFixedSemantics()
    {
        var timeoutRow = Assert.Single(await new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient())).EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(timeout: TimeSpan.FromMilliseconds(1))));

        AssertRejected(timeoutRow, LiveMissionProposalRunner.OutcomeTimeout, "timeout");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(timeoutRow, SensitiveSentinels);

        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new DelayedCompletionClient(),
            new StaticCreditClient()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            evaluator.EvaluateAsync(
                [new LiveMissionProposalEvalCase("cancelled", CreatePacket())],
                CreateOptions(timeout: TimeSpan.FromSeconds(30)),
                cts.Token));
    }

    [Fact]
    public async Task TimeoutDuringCreditGuardMapsToFixedTimeout()
    {
        var client = new CountingCompletionClient(CreateContent("packet-live-proposal", "session-live"));
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(client, new DelayedCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            CreateOptions(timeout: TimeSpan.FromMilliseconds(1))));

        AssertRejected(row, LiveMissionProposalRunner.OutcomeTimeout, "timeout");
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task MalformedApiInputsReturnFixedSanitizedRows()
    {
        var evaluator = new LiveMissionProposalEvaluator(new LiveMissionProposalRunner(
            new CountingCompletionClient(CreateContent("packet-live-proposal", "session-live")),
            new StaticCreditClient()));

        var nullCasesRow = Assert.Single(await evaluator.EvaluateAsync(null!, CreateOptions()));
        var nullEvalCaseRow = Assert.Single(await evaluator.EvaluateAsync([null], CreateOptions()));
        var nullPacketRow = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", null!)],
            CreateOptions()));
        var nullOptionsRow = Assert.Single(await evaluator.EvaluateAsync(
            [new LiveMissionProposalEvalCase($"{RawPrompt}-{Credential}", CreatePacket())],
            null!));

        foreach (var row in new[] { nullCasesRow, nullEvalCaseRow, nullPacketRow, nullOptionsRow })
        {
            AssertRejected(row, LiveMissionProposalRunner.OutcomeSchemaInvalid);
            SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        }
    }

    [Fact]
    public void OptionsLoadSafeEnvControlsWithoutSecrets()
    {
        var options = LiveMissionProposalOptions.FromEnvironment(new Dictionary<string, string?>
        {
            ["PCH_LIVE_MISSION_PROPOSAL_ENABLED"] = "true",
            ["OPENROUTER_API_KEY"] = ApiKey,
            ["PCH_LIVE_MODEL_PROVIDER"] = "openai",
            ["PCH_LIVE_MODEL_SKIP_CREDIT_GUARD"] = "true",
            ["PCH_LIVE_MODEL_TIMEOUT_SECONDS"] = "13",
            ["PCH_LIVE_IN_HARNESS_MODEL"] = "qwen/qwen3-14b",
            ["PCH_LIVE_STRONG_PLANNER_MODEL"] = "openai/strong-planner"
        });

        Assert.True(options.Enabled);
        Assert.True(options.ApiKeyAvailable);
        Assert.False(options.CreditGuardEnabled);
        Assert.Equal(LivePreflightProviderKind.OpenAi, options.ProviderKind);
        Assert.Equal(TimeSpan.FromSeconds(13), options.Timeout);
        Assert.Equal("openai/strong-planner", options.StrongPlannerModelId);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(options, [ApiKey]);
    }

    private static LiveMissionProposalPacket CreatePacket(bool requiresFallback = false) =>
        new(
            "packet-live-proposal",
            "session-live",
            LiveModelRole.StrongPlanner,
            "en-US",
            ["mission_proposal"],
            requiresFallback,
            $"{RawPrompt} {RawProviderPayload} {CandidateDisplay} {ApprovalToken} {HoldReference} {Credential} {SecretSentinel}");

    private static LiveMissionProposalOptions CreateOptions(
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

    private static string CreateContent(
        string packetId,
        string sessionId,
        string role = "strong_planner",
        string outputKind = "mission_proposal",
        string missionKind = "vacation",
        string fieldPath = "/mission/purpose",
        string fieldValue = "safe vacation",
        string authoritySource = "model_inference_pending_confirmation",
        string commitmentKind = "travel",
        string title = "Safe travel plan",
        string? location = "Safe airport",
        string priority = "normal",
        string reasonCode = "needs_date_confirmation",
        string evidenceId = "evidence-safe") =>
        JsonSerializer.Serialize(new
        {
            packetId,
            sessionId,
            role,
            outputKind,
            missionKind,
            fields = new[]
            {
                new
                {
                    fieldPath,
                    value = fieldValue,
                    authoritySource,
                    evidenceIds = new[] { evidenceId }
                },
                new
                {
                    fieldPath = "/mission/destination_country",
                    value = "Japan",
                    authoritySource = "model_inference_pending_confirmation",
                    evidenceIds = new[] { "evidence-destination" }
                }
            },
            commitments = new[]
            {
                new
                {
                    commitmentId = "commitment-travel",
                    commitmentKind,
                    title,
                    startsAt = "2026-10-01T09:00:00Z",
                    endsAt = "2026-10-01T12:00:00Z",
                    location,
                    isIrreversible = false,
                    requiresSpend = true,
                    priority,
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
                    reasonCode,
                    authoritySource = "model_inference_pending_confirmation",
                    evidenceIds = new[] { "evidence-pending" }
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedLiveMissionProposalEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(LiveMissionProposalRunner.RejectedRowName, row.Name);
        Assert.Equal(LiveMissionProposalRunner.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        if (expectedErrorCode is not null)
        {
            Assert.Equal(expectedErrorCode, row.ErrorCode);
        }

        Assert.Null(row.SessionId);
        Assert.Null(row.Role);
        Assert.Null(row.OutputKind);
        Assert.Null(row.MissionKind);
        Assert.Empty(row.FieldPaths);
        Assert.Empty(row.CommitmentKinds);
        Assert.Empty(row.PendingReasonCodes);
        Assert.Equal(0, row.FieldCount);
        Assert.Equal(0, row.CommitmentCount);
        Assert.Equal(0, row.PendingConfirmationCount);
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

    private sealed class DelayedCreditClient : IProviderCreditClient
    {
        public async Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable delayed credits");
        }
    }
}
