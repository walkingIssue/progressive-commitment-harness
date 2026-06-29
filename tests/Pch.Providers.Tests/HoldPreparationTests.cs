using System.Text.Json;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.HoldPreparation;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class HoldPreparationTests
{
    [Fact]
    public async Task PreviewDoesNotRequireApprovalToken()
    {
        const string contextSentinel = "RAW_PAYLOAD_CANDIDATE_TITLE_PAYMENT_SENTINEL_SHOULD_NOT_PERSIST";
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("preview", CreatePacket(HoldPreparationOperation.Preview, contextDigest: contextSentinel))]));

        Assert.True(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomePreviewReady, row.OutcomeCode);
        Assert.Equal(2, row.CandidateCount);
        Assert.Equal(MockHoldPreparationAdapter.ProviderName, row.Provider);
        Assert.Equal("mock-hold-preparation-deterministic", row.Model);
        Assert.Equal("mock-hold-prep-packet-hold", row.RequestId);
        Assert.Contains(row.Candidates, candidate => candidate is { SlotId: "slot-dining", CandidateId: "candidate-dining", Category: CandidateCategory.Dining });
        Assert.Contains(row.Candidates, candidate => candidate is { SlotId: "slot-activity", CandidateId: "candidate-activity", Category: CandidateCategory.Activity });

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(contextSentinel, serialized);
        Assert.DoesNotContain("mock-hold-slot", serialized);
    }

    [Fact]
    public async Task HoldSuccessRequiresMatchingApprovalTokenWithoutPersistingToken()
    {
        const string approvalToken = "APPROVAL_TOKEN_SENTINEL_SHOULD_NOT_PERSIST";
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("hold", CreatePacket(HoldPreparationOperation.Hold, approvalToken))],
            new HoldPreparationOptions(RequiredApprovalToken: approvalToken)));

        Assert.True(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeHoldPrepared, row.OutcomeCode);
        Assert.Equal(2, row.CandidateCount);
        Assert.Equal(MockHoldPreparationAdapter.ProviderName, row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(approvalToken, serialized);
        Assert.DoesNotContain("mock-hold-slot", serialized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingApprovalTokenBlocksHoldBeforeSuccess(string? approvalToken)
    {
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("missing-approval", CreatePacket(HoldPreparationOperation.Hold, approvalToken))]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeMissingApproval, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    [Fact]
    public async Task MismatchedApprovalTokenBlocksHoldWithoutPersistingToken()
    {
        const string approvalToken = "MISMATCHED_APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("approval-mismatch", CreatePacket(HoldPreparationOperation.Hold, approvalToken))],
            new HoldPreparationOptions(RequiredApprovalToken: "expected-token")));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeApprovalMismatch, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(approvalToken, serialized);
    }

    [Fact]
    public async Task EvaluatorRejectsForgedHoldPreparedWhenApprovalTokenMissing()
    {
        const string holdReference = "RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST";
        var packet = CreatePacket(HoldPreparationOperation.Hold, approvalToken: null, contextDigest: "RAW_CONTEXT_SHOULD_NOT_PERSIST");
        var evaluator = new HoldPreparationEvaluator(new StaticHoldPreparationAdapter(CreateForgedHoldPreparedResult(packet, holdReference)));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("forged-missing-approval", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeMissingApproval, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(holdReference, serialized);
        Assert.DoesNotContain("RAW_CONTEXT_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("provider-should-not-persist", serialized);
        Assert.DoesNotContain("request-should-not-persist", serialized);
    }

    [Fact]
    public async Task EvaluatorRejectsForgedHoldPreparedWhenApprovalTokenMismatches()
    {
        const string approvalToken = "MALICIOUS_APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
        const string holdReference = "RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST";
        var packet = CreatePacket(HoldPreparationOperation.Hold, approvalToken);
        var evaluator = new HoldPreparationEvaluator(new StaticHoldPreparationAdapter(CreateForgedHoldPreparedResult(packet, holdReference)));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("forged-mismatched-approval", packet)],
            new HoldPreparationOptions(RequiredApprovalToken: "expected-approval-token")));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeApprovalMismatch, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Null(row.Provider);
        Assert.Null(row.RequestId);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(approvalToken, serialized);
        Assert.DoesNotContain(holdReference, serialized);
        Assert.DoesNotContain("provider-should-not-persist", serialized);
        Assert.DoesNotContain("request-should-not-persist", serialized);
    }

    [Fact]
    public async Task PacketMismatchUsesFixedOutcomeWithoutRawResultPacketId()
    {
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter(MockHoldPreparationBehavior.PacketMismatch));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("packet-mismatch", CreatePacket(HoldPreparationOperation.Preview))]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomePacketIdMismatch, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", serialized);
    }

    [Fact]
    public async Task ProviderUnavailableUsesFixedErrorCodeWithoutRawExceptionText()
    {
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter(MockHoldPreparationBehavior.ProviderUnavailable));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("provider-error", CreatePacket(HoldPreparationOperation.Preview))]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeError, row.OutcomeCode);
        Assert.Equal("provider_error", row.ErrorCode);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("Mock hold preparation provider unavailable", serialized);
    }

    [Fact]
    public async Task UnsupportedResultUsesFixedOutcomeWithoutProviderMetadata()
    {
        var evaluator = new HoldPreparationEvaluator(new MockHoldPreparationAdapter(MockHoldPreparationBehavior.UnsupportedResult));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("unsupported", CreatePacket(HoldPreparationOperation.Preview))]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeMalformedResult, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);
        Assert.Null(row.RequestId);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("mock-hold-prep", serialized);
    }

    [Fact]
    public async Task UnsupportedCandidateStatusUsesFixedOutcomeWithoutProviderMetadata()
    {
        var packet = CreatePacket(HoldPreparationOperation.Preview);
        var evaluator = new HoldPreparationEvaluator(new StaticHoldPreparationAdapter(new HoldPreparationResult(
            packet.PacketId,
            HoldPreparationResultKind.PreviewReady,
            [
                new HoldPreparationCandidateResult(
                    "slot-dining",
                    "candidate-dining",
                    CandidateCategory.Dining,
                    HoldPreparationCandidateStatus.Unsupported,
                    HoldReferenceId: "RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST"),
                new HoldPreparationCandidateResult(
                    "slot-activity",
                    "candidate-activity",
                    CandidateCategory.Activity,
                    HoldPreparationCandidateStatus.PreviewAvailable)
            ],
            123,
            "provider-should-not-persist",
            "model-should-not-persist",
            "request-should-not-persist")));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new HoldPreparationEvalCase("bad-status", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(HoldPreparationEvaluator.OutcomeMalformedResult, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("provider-should-not-persist", serialized);
    }

    private static HoldPreparationPacket CreatePacket(
        HoldPreparationOperation operation,
        string? approvalToken = null,
        string? contextDigest = null) =>
        new(
            "packet-hold",
            operation,
            [
                new SelectedItineraryCandidate("slot-dining", "candidate-dining", CandidateCategory.Dining),
                new SelectedItineraryCandidate("slot-activity", "candidate-activity", CandidateCategory.Activity)
            ],
            "en-US",
            approvalToken,
            contextDigest);

    private static HoldPreparationResult CreateForgedHoldPreparedResult(
        HoldPreparationPacket packet,
        string holdReference) =>
        new(
            packet.PacketId,
            HoldPreparationResultKind.HoldPrepared,
            [
                new HoldPreparationCandidateResult(
                    "slot-dining",
                    "candidate-dining",
                    CandidateCategory.Dining,
                    HoldPreparationCandidateStatus.HoldPrepared,
                    holdReference),
                new HoldPreparationCandidateResult(
                    "slot-activity",
                    "candidate-activity",
                    CandidateCategory.Activity,
                    HoldPreparationCandidateStatus.HoldPrepared,
                    holdReference)
            ],
            456,
            "provider-should-not-persist",
            "model-should-not-persist",
            "request-should-not-persist");

    private sealed class StaticHoldPreparationAdapter(HoldPreparationResult result) : IHoldPreparationAdapter
    {
        public Task<HoldPreparationResult> PrepareAsync(
            HoldPreparationPacket packet,
            HoldPreparationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
