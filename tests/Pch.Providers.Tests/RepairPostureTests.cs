using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.Mock;
using Pch.Providers.ModelCompletion;
using Pch.Providers.RepairPosture;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class RepairPostureTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string CandidateDisplay = "CANDIDATE_DISPLAY_TEXT_SHOULD_NOT_PERSIST";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string HoldReference = "HOLD_REFERENCE_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string RawException = "RAW_EXCEPTION_TEXT_SHOULD_NOT_PERSIST";
    private const string SecretSentinel = "SECRET_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        CandidateDisplay,
        ApprovalToken,
        HoldReference,
        Credential,
        RawException,
        SecretSentinel
    ];

    [Fact]
    public async Task MockSourceSuggestsDeterministicRepairModesFromSanitizedNodeMetadata()
    {
        var result = await new MockRepairPostureSource().SuggestAsync(CreatePacket());

        Assert.Equal("packet-repair", result.PacketId);
        Assert.Equal(MockRepairPostureSource.ProviderName, result.Provider);
        Assert.Contains(result.Suggestions, suggestion => suggestion is { NodeId: "node-keep", Mode: RepairMode.Keep });
        Assert.Contains(result.Suggestions, suggestion => suggestion is { NodeId: "node-day", Mode: RepairMode.ReplanDay });
        Assert.Contains(result.Suggestions, suggestion => suggestion is { NodeId: "node-candidate", Mode: RepairMode.ReselectCandidate });
        Assert.Contains(result.Suggestions, suggestion => suggestion is { NodeId: "node-user", Mode: RepairMode.AskUser });
        Assert.Contains(result.Suggestions, suggestion => suggestion is { NodeId: "node-hold", Mode: RepairMode.BlockedReview });
    }

    [Fact]
    public async Task AcceptedRowsPersistOnlyEnumsCountsTrustedNodeIdsAndProviderMetadata()
    {
        var evaluator = new RepairPostureEvaluator(new MockRepairPostureSource());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase("repair-ready", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.Equal("repair-ready", row.Name);
        Assert.Equal("packet-repair", row.PacketId);
        Assert.Equal(RepairPostureEvaluator.OutcomeAccepted, row.OutcomeCode);
        Assert.Equal(5, row.NodeCount);
        Assert.Equal(5, row.SuggestionCount);
        Assert.Equal(MockRepairPostureSource.ProviderName, row.Provider);
        Assert.Equal(MockRepairPostureSource.ModelName, row.Model);
        Assert.Contains(row.Suggestions, suggestion => suggestion is
        {
            NodeId: "node-candidate",
            NodeKind: RepairPostureNodeKind.SelectedCandidate,
            Mode: RepairMode.ReselectCandidate,
            ReasonCode: RepairReasonCode.CandidateInvalidated
        });

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData(MockRepairPostureBehavior.PacketMismatch, "repair_posture_packet_mismatch", null)]
    [InlineData(MockRepairPostureBehavior.NodeMismatch, "repair_posture_node_mismatch", null)]
    [InlineData(MockRepairPostureBehavior.UnsupportedMode, "repair_posture_unsupported_mode", null)]
    [InlineData(MockRepairPostureBehavior.MalformedResult, "repair_posture_malformed_result", null)]
    [InlineData(MockRepairPostureBehavior.ProviderTimeout, "repair_posture_timeout", "timeout")]
    [InlineData(MockRepairPostureBehavior.ProviderUnavailable, "repair_posture_provider_unavailable", "provider_error")]
    public async Task BlockedRowsUseFixedCodesAndOmitProviderMetadata(
        MockRepairPostureBehavior behavior,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new RepairPostureEvaluator(new MockRepairPostureSource(behavior));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase($"{RawPrompt}-{Credential}", CreatePacket())]));

        AssertRejected(row, expectedOutcome, expectedErrorCode);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
        Assert.DoesNotContain("RAW_NODE_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
    }

    [Fact]
    public async Task MalformedPacketBlocksBeforeSourceInvocation()
    {
        var source = new CountingRepairPostureSource();
        var evaluator = new RepairPostureEvaluator(source);
        var packet = CreatePacket() with
        {
            PacketId = $"{RawPrompt}-{RawProviderPayload}-{SecretSentinel}",
            Nodes = []
        };

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase($"{CandidateDisplay}-{Credential}", packet)]));

        AssertRejected(row, RepairPostureEvaluator.OutcomeMalformedPacket);
        Assert.Equal(0, source.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SourceExceptionsUseFixedCodesWithoutRawExceptionText()
    {
        var evaluator = new RepairPostureEvaluator(new ThrowingRepairPostureSource(
            new InvalidOperationException($"{RawException} {RawProviderPayload} {Credential} {ApprovalToken} {HoldReference}")));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase($"{CandidateDisplay}-{Credential}", CreatePacket())]));

        AssertRejected(row, RepairPostureEvaluator.OutcomeError, "repair_posture_error");
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task LiveSourceDisabledAndMissingKeyAreGuardedBeforeCompletion()
    {
        var completion = new CountingCompletionClient(CreateCompletionContent("packet-repair"));
        var evaluator = new RepairPostureEvaluator(new ModelCompletionRepairPostureSource(completion, new StaticCreditClient()));

        var disabled = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase("disabled", CreatePacket())],
            new RepairPostureOptions(new RepairPostureLiveOptions(Enabled: false))));
        var missingKey = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase("missing-key", CreatePacket())],
            new RepairPostureOptions(new RepairPostureLiveOptions(Enabled: true, ApiKeyAvailable: false))));

        AssertRejected(disabled, RepairPostureEvaluator.OutcomeLiveDisabled);
        AssertRejected(missingKey, RepairPostureEvaluator.OutcomeKeyMissing);
        Assert.Equal(0, completion.CallCount);
    }

    [Fact]
    public async Task LiveSourceParsesStructuredCompletionAndSendsOnlySanitizedPrompt()
    {
        var completion = new CountingCompletionClient(CreateCompletionContent("packet-repair"));
        var evaluator = new RepairPostureEvaluator(new ModelCompletionRepairPostureSource(completion, new StaticCreditClient()));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new RepairPostureEvalCase("live-shaped", CreatePacket())],
            new RepairPostureOptions(new RepairPostureLiveOptions(Enabled: true, ApiKeyAvailable: true, CreditGuardEnabled: true))));

        Assert.True(row.Passed);
        Assert.Equal(RepairMode.AskUser, Assert.Single(row.Suggestions).Mode);
        Assert.Equal("openrouter", row.Provider);
        Assert.Equal("qwen/qwen3-14b", row.Model);
        Assert.NotNull(completion.LastRequest);
        Assert.Equal("repair_posture_suggestions", completion.LastRequest.JsonSchemaName);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(completion.LastRequest.Messages, SensitiveSentinels);
    }

    [Fact]
    public async Task LiveCreditExhaustionAndMalformedOutputUseFixedRejectedRows()
    {
        var creditRow = Assert.Single(await new RepairPostureEvaluator(new ModelCompletionRepairPostureSource(
                new CountingCompletionClient(CreateCompletionContent("packet-repair")),
                new StaticCreditClient(new ProviderCreditStatus(40, 40, 0, IsExhausted: true))))
            .EvaluateAsync(
                [new RepairPostureEvalCase("credit", CreatePacket())],
                new RepairPostureOptions(new RepairPostureLiveOptions(Enabled: true, ApiKeyAvailable: true))));
        var malformedRow = Assert.Single(await new RepairPostureEvaluator(new ModelCompletionRepairPostureSource(
                new CountingCompletionClient("not-json"),
                new StaticCreditClient()))
            .EvaluateAsync(
                [new RepairPostureEvalCase("malformed", CreatePacket())],
                new RepairPostureOptions(new RepairPostureLiveOptions(Enabled: true, ApiKeyAvailable: true))));

        AssertRejected(creditRow, RepairPostureEvaluator.OutcomeCreditExhausted, "credit_exhausted");
        AssertRejected(malformedRow, RepairPostureEvaluator.OutcomeMalformedResult, "malformed_schema");
    }

    [Fact]
    public void LiveProviderDescriptorsAreDisabledAndGuardedByDefault()
    {
        var descriptors = RepairPostureLiveProviders.Defaults;

        Assert.Contains(descriptors, descriptor => descriptor.ProviderKind == RepairPostureProviderKind.OpenRouter);
        Assert.Contains(descriptors, descriptor => descriptor.ProviderKind == RepairPostureProviderKind.OpenAi);
        Assert.Contains(descriptors, descriptor => descriptor.ProviderKind == RepairPostureProviderKind.GrokXAi);
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(descriptor.EnabledByDefault);
            Assert.True(descriptor.RequiresApiKey);
            Assert.Contains("timeout", descriptor.GuardPolicy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no-fallback", descriptor.GuardPolicy, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static RepairPosturePacket CreatePacket() =>
        new(
            "packet-repair",
            [
                new RepairPostureNode("node-keep", RepairPostureNodeKind.MissionFact, RepairPostureNodeStatus.Preserved, 0, false, false, ["evidence-1"]),
                new RepairPostureNode("node-day", RepairPostureNodeKind.Day, RepairPostureNodeStatus.Affected, 3, false, false, ["evidence-2"]),
                new RepairPostureNode("node-candidate", RepairPostureNodeKind.SelectedCandidate, RepairPostureNodeStatus.Changed, 2, false, false, ["evidence-3"]),
                new RepairPostureNode("node-user", RepairPostureNodeKind.Slot, RepairPostureNodeStatus.NeedsUser, 1, true, false, ["evidence-4"]),
                new RepairPostureNode("node-hold", RepairPostureNodeKind.MockHold, RepairPostureNodeStatus.Blocked, 2, false, true, ["evidence-5"])
            ],
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {CandidateDisplay} {ApprovalToken} {HoldReference} {Credential} {SecretSentinel}");

    private static string CreateCompletionContent(string packetId) =>
        JsonSerializer.Serialize(new
        {
            packetId,
            suggestions = new[]
            {
                new
                {
                    nodeId = "node-user",
                    mode = "ask_user",
                    reasonCode = "needs_user_confirmation",
                    affectedNodeCount = 1
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertRejected(
        SanitizedRepairPostureEvalRow row,
        string expectedOutcome,
        string? expectedErrorCode = null)
    {
        Assert.False(row.Passed);
        Assert.Equal(RepairPostureEvaluator.RejectedRowName, row.Name);
        Assert.Equal(RepairPostureEvaluator.RejectedRowPacketId, row.PacketId);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Empty(row.Suggestions);
        Assert.Equal(0, row.NodeCount);
        Assert.Equal(0, row.SuggestionCount);
        Assert.Equal(0, row.TotalAffectedNodeCount);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class CountingRepairPostureSource : IRepairPostureSource
    {
        public int CallCount { get; private set; }

        public Task<RepairPostureResult> SuggestAsync(
            RepairPosturePacket packet,
            RepairPostureOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return new MockRepairPostureSource().SuggestAsync(packet, options, cancellationToken);
        }
    }

    private sealed class ThrowingRepairPostureSource(Exception exception) : IRepairPostureSource
    {
        public Task<RepairPostureResult> SuggestAsync(
            RepairPosturePacket packet,
            RepairPostureOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<RepairPostureResult>(exception);
    }

    private sealed class CountingCompletionClient(string content) : IModelCompletionClient
    {
        public int CallCount { get; private set; }

        public ModelCompletionRequest? LastRequest { get; private set; }

        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new ModelCompletionResponse(
                request.Model ?? "qwen/qwen3-14b",
                content,
                "openrouter",
                RequestId: "request-safe"));
        }
    }

    private sealed class StaticCreditClient(ProviderCreditStatus? status = null) : IProviderCreditClient
    {
        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status ?? new ProviderCreditStatus(40, 1, 39, IsExhausted: false));
    }
}
