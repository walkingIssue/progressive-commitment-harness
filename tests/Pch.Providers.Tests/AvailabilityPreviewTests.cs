using Pch.Providers.AvailabilityPreview;
using Pch.Providers.Errors;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class AvailabilityPreviewTests
{
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string RawFareReference = "RAW_PROVIDER_QUOTE_REFERENCE_SHOULD_NOT_PERSIST";
    private const string PaymentData = "PAYMENT_DATA_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string CandidateDisplayValue = "CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST";
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawException = "RAW_EXCEPTION_MESSAGE_SHOULD_NOT_PERSIST";
    private const string GenericSentinel = "GENERIC_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawProviderPayload,
        RawFareReference,
        PaymentData,
        Credential,
        ApprovalToken,
        CandidateDisplayValue,
        RawPrompt,
        RawException,
        GenericSentinel
    ];

    [Fact]
    public async Task MockAdapterReturnsDeterministicQuoteReadyForAllPreviewCategories()
    {
        var adapter = new MockAvailabilityPreviewAdapter();

        var result = await adapter.PreviewAsync(CreatePacket());

        Assert.Equal("packet-availability", result.PacketId);
        Assert.Equal(AvailabilityPreviewResultKind.QuoteReady, result.Kind);
        Assert.Equal(MockAvailabilityPreviewAdapter.ProviderName, result.Provider);
        Assert.Equal(MockAvailabilityPreviewAdapter.ModelName, result.Model);
        Assert.Equal(5, result.Candidates.Count);
        Assert.Contains(result.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Flight);
        Assert.Contains(result.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Lodging);
        Assert.Contains(result.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Activity);
        Assert.Contains(result.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Dining);
        Assert.Contains(result.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Transit);
        Assert.All(result.Candidates, candidate =>
        {
            Assert.Equal(AvailabilityPreviewCandidateStatus.QuoteReady, candidate.Status);
            Assert.NotNull(candidate.QuoteAmount);
            Assert.Equal("USD", candidate.Currency);
            Assert.NotNull(candidate.ProviderQuoteReference);
        });
    }

    [Fact]
    public async Task QuoteReadyRowsPersistOnlyTrustedIdsCategoriesCountsAndMetadata()
    {
        var evaluator = new AvailabilityPreviewEvaluator(new MockAvailabilityPreviewAdapter());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("quote-ready", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeQuoteReady, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(5, row.CandidateCount);
        Assert.Equal(5, row.QuoteReadyCount);
        Assert.Equal(0, row.UnavailableCount);
        Assert.Equal(MockAvailabilityPreviewAdapter.ProviderName, row.Provider);
        Assert.Equal(MockAvailabilityPreviewAdapter.ModelName, row.Model);
        Assert.NotNull(row.ResponseContentLength);
        Assert.Contains(row.Candidates, candidate => candidate is
        {
            SlotId: "slot-flight",
            CandidateId: "candidate-flight",
            Category: AvailabilityPreviewCategory.Flight,
            Status: AvailabilityPreviewCandidateStatus.QuoteReady
        });
        Assert.Contains(row.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Lodging);
        Assert.Contains(row.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Activity);
        Assert.Contains(row.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Dining);
        Assert.Contains(row.Candidates, candidate => candidate.Category == AvailabilityPreviewCategory.Transit);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        var serialized = SanitizedEvalArtifactAssert.Serialize(row);
        Assert.DoesNotContain("USD", serialized);
    }

    [Fact]
    public async Task UnavailableRowsUseTrustedCandidateRowsWithoutProviderMetadata()
    {
        var evaluator = new AvailabilityPreviewEvaluator(new MockAvailabilityPreviewAdapter(
            MockAvailabilityPreviewBehavior.Unavailable));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("unavailable", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeUnavailable, row.OutcomeCode);
        Assert.Equal(5, row.CandidateCount);
        Assert.Equal(0, row.QuoteReadyCount);
        Assert.Equal(5, row.UnavailableCount);
        Assert.All(row.Candidates, candidate => Assert.Equal(AvailabilityPreviewCandidateStatus.Unavailable, candidate.Status));
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData(MockAvailabilityPreviewBehavior.PacketMismatch, "availability_preview_packet_mismatch", null)]
    [InlineData(MockAvailabilityPreviewBehavior.MalformedResult, "availability_preview_malformed_result", null)]
    [InlineData(MockAvailabilityPreviewBehavior.UnsupportedResult, "availability_preview_unsupported_result", null)]
    [InlineData(MockAvailabilityPreviewBehavior.UnsupportedCategory, "availability_preview_unsupported_category", null)]
    [InlineData(MockAvailabilityPreviewBehavior.CandidateMismatch, "availability_preview_candidate_mismatch", null)]
    [InlineData(MockAvailabilityPreviewBehavior.ProviderTimeout, "availability_preview_timeout", "timeout")]
    [InlineData(MockAvailabilityPreviewBehavior.ProviderUnavailable, "availability_preview_provider_unavailable", "provider_error")]
    public async Task BlockedMockBehaviorsUseFixedCodesWithoutRawProviderValues(
        MockAvailabilityPreviewBehavior behavior,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new AvailabilityPreviewEvaluator(new MockAvailabilityPreviewAdapter(behavior));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase($"blocked-{behavior}", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Equal(0, row.QuoteReadyCount);
        Assert.Equal(0, row.UnavailableCount);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
        Assert.DoesNotContain("RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST", SanitizedEvalArtifactAssert.Serialize(row));
    }

    [Fact]
    public async Task MissingResultCandidateBlocksWithoutPersistingPartialMetadata()
    {
        var packet = CreatePacket();
        var result = CreateResult(packet) with
        {
            Candidates = CreateResult(packet).Candidates.Take(4).ToArray(),
            Provider = Credential,
            Model = CandidateDisplayValue,
            RequestId = ApprovalToken
        };
        var evaluator = new AvailabilityPreviewEvaluator(new StaticAvailabilityPreviewAdapter(result));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("missing-result-candidate", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeCandidateMismatch, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task DuplicateResultCandidateBlocksWithoutPersistingProviderValues()
    {
        var packet = CreatePacket();
        var first = CreateResult(packet).Candidates[0];
        var result = CreateResult(packet) with
        {
            Candidates = [first, first],
            Provider = Credential,
            Model = CandidateDisplayValue,
            RequestId = ApprovalToken
        };
        var evaluator = new AvailabilityPreviewEvaluator(new StaticAvailabilityPreviewAdapter(result));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("duplicate-result-candidate", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeCandidateMismatch, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task WrongResultStatusBlocksAsMalformedWithoutQuoteReferences()
    {
        var packet = CreatePacket();
        var result = CreateResult(packet) with
        {
            Candidates = CreateResult(packet).Candidates
                .Select(candidate => candidate with { Status = AvailabilityPreviewCandidateStatus.Unavailable })
                .ToArray(),
            Provider = Credential,
            Model = CandidateDisplayValue,
            RequestId = ApprovalToken
        };
        var evaluator = new AvailabilityPreviewEvaluator(new StaticAvailabilityPreviewAdapter(result));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("wrong-status", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeMalformedResult, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task NullResultAndNullResultCandidatesAreMalformed()
    {
        var nullResultRow = Assert.Single(await new AvailabilityPreviewEvaluator(new NullAvailabilityPreviewAdapter())
            .EvaluateAsync([new AvailabilityPreviewEvalCase("null-result", CreatePacket())]));
        var nullCandidatesRow = Assert.Single(await new AvailabilityPreviewEvaluator(new StaticAvailabilityPreviewAdapter(
                CreateResult(CreatePacket()) with { Candidates = null!, Provider = Credential }))
            .EvaluateAsync([new AvailabilityPreviewEvalCase("null-candidates", CreatePacket())]));

        AssertMalformedRejectedRow(nullResultRow, AvailabilityPreviewEvaluator.OutcomeMalformedResult);
        AssertMalformedRejectedRow(nullCandidatesRow, AvailabilityPreviewEvaluator.OutcomeMalformedResult);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullResultRow, SensitiveSentinels);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(nullCandidatesRow, SensitiveSentinels);
    }

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public async Task MalformedPacketsBlockBeforeAdapterInvocation(AvailabilityPreviewPacket packet)
    {
        var adapter = new CountingAvailabilityPreviewAdapter();
        var evaluator = new AvailabilityPreviewEvaluator(adapter);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("malformed-packet", packet)]));

        AssertMalformedRejectedRow(row, AvailabilityPreviewEvaluator.OutcomeMalformedPacket);
        Assert.Equal(0, adapter.CallCount);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SourceExceptionsUseFixedCodesWithoutRawExceptionText()
    {
        var evaluator = new AvailabilityPreviewEvaluator(new ThrowingAvailabilityPreviewAdapter(
            new InvalidOperationException($"{RawException} {RawProviderPayload} {Credential} {ApprovalToken} {RawFareReference}")));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new AvailabilityPreviewEvalCase("exception", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(AvailabilityPreviewEvaluator.OutcomeError, row.OutcomeCode);
        Assert.Equal("availability_preview_error", row.ErrorCode);
        Assert.Empty(row.Candidates);
        Assert.Null(row.Provider);
        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    public static TheoryData<AvailabilityPreviewPacket> MalformedPackets() =>
        new()
        {
            CreatePacket() with { Candidates = [] },
            CreatePacket() with { Candidates = null! },
            CreatePacket() with { Candidates = [null!] },
            CreatePacket() with { Candidates = [new AvailabilityPreviewCandidate("", "candidate", AvailabilityPreviewCategory.Flight)] },
            CreatePacket() with { Candidates = [new AvailabilityPreviewCandidate("slot", "   ", AvailabilityPreviewCategory.Flight)] },
            CreatePacket() with { Candidates = [new AvailabilityPreviewCandidate("slot", "candidate", (AvailabilityPreviewCategory)999)] },
            CreatePacket() with
            {
                Candidates =
                [
                    new AvailabilityPreviewCandidate("slot-flight", "candidate-flight", AvailabilityPreviewCategory.Flight),
                    new AvailabilityPreviewCandidate("slot-flight", "candidate-flight", AvailabilityPreviewCategory.Flight)
                ]
            }
        };

    private static AvailabilityPreviewPacket CreatePacket() =>
        new(
            "packet-availability",
            [
                new AvailabilityPreviewCandidate("slot-flight", "candidate-flight", AvailabilityPreviewCategory.Flight),
                new AvailabilityPreviewCandidate("slot-lodging", "candidate-lodging", AvailabilityPreviewCategory.Lodging),
                new AvailabilityPreviewCandidate("slot-activity", "candidate-activity", AvailabilityPreviewCategory.Activity),
                new AvailabilityPreviewCandidate("slot-dining", "candidate-dining", AvailabilityPreviewCategory.Dining),
                new AvailabilityPreviewCandidate("slot-transit", "candidate-transit", AvailabilityPreviewCategory.Transit)
            ],
            "en-US",
            $"{RawPrompt} {RawProviderPayload} {PaymentData} {Credential} {ApprovalToken} {CandidateDisplayValue} {GenericSentinel}");

    private static AvailabilityPreviewResult CreateResult(AvailabilityPreviewPacket packet) =>
        new(
            packet.PacketId,
            AvailabilityPreviewResultKind.QuoteReady,
            packet.Candidates
                .Select(candidate => new AvailabilityPreviewCandidateResult(
                    candidate.SlotId,
                    candidate.CandidateId,
                    candidate.Category,
                    AvailabilityPreviewCandidateStatus.QuoteReady,
                    QuoteAmount: 100,
                    Currency: "USD",
                    ExpiresAt: DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
                    ProviderQuoteReference: RawFareReference))
                .ToArray(),
            123,
            "provider-safe-unless-rejected",
            "model-safe-unless-rejected",
            "request-safe-unless-rejected");

    private static void AssertMalformedRejectedRow(
        SanitizedAvailabilityPreviewEvalRow row,
        string expectedOutcome)
    {
        Assert.False(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Equal(0, row.QuoteReadyCount);
        Assert.Equal(0, row.UnavailableCount);
        Assert.Null(row.ResponseContentLength);
        Assert.Null(row.Provider);
        Assert.Null(row.Model);
        Assert.Null(row.RequestId);
    }

    private sealed class StaticAvailabilityPreviewAdapter(AvailabilityPreviewResult result) : IAvailabilityPreviewAdapter
    {
        public Task<AvailabilityPreviewResult> PreviewAsync(
            AvailabilityPreviewPacket packet,
            AvailabilityPreviewOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class NullAvailabilityPreviewAdapter : IAvailabilityPreviewAdapter
    {
        public Task<AvailabilityPreviewResult> PreviewAsync(
            AvailabilityPreviewPacket packet,
            AvailabilityPreviewOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AvailabilityPreviewResult>(null!);
    }

    private sealed class CountingAvailabilityPreviewAdapter : IAvailabilityPreviewAdapter
    {
        public int CallCount { get; private set; }

        public Task<AvailabilityPreviewResult> PreviewAsync(
            AvailabilityPreviewPacket packet,
            AvailabilityPreviewOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CreateResult(packet));
        }
    }

    private sealed class ThrowingAvailabilityPreviewAdapter(Exception exception) : IAvailabilityPreviewAdapter
    {
        public Task<AvailabilityPreviewResult> PreviewAsync(
            AvailabilityPreviewPacket packet,
            AvailabilityPreviewOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AvailabilityPreviewResult>(exception);
    }
}
