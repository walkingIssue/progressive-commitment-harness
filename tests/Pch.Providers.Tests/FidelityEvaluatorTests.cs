using Pch.Providers.Fidelity;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class FidelityEvaluatorTests
{
    private const string RawPrompt = "RAW_PROMPT_TEXT_SHOULD_NOT_PERSIST";
    private const string RawProviderPayload = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_PERSIST";
    private const string ApprovalToken = "APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
    private const string HoldReference = "RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST";
    private const string CandidateDisplayValue = "CANDIDATE_DISPLAY_VALUE_SHOULD_NOT_PERSIST";
    private const string Credential = "sk-credential-sentinel-should-not-persist";
    private const string RawException = "RAW_EXCEPTION_MESSAGE_SHOULD_NOT_PERSIST";
    private const string GenericSentinel = "GENERIC_SENTINEL_SHOULD_NOT_PERSIST";

    private static readonly string[] SensitiveSentinels =
    [
        RawPrompt,
        RawProviderPayload,
        ApprovalToken,
        HoldReference,
        CandidateDisplayValue,
        Credential,
        RawException,
        GenericSentinel
    ];

    [Fact]
    public async Task SchemaValidSourcesProduceSanitizedComparisonRows()
    {
        var evaluator = new FidelityEvaluator(CreateMockSources());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("stage-6-bakeoff", CreatePacket())]));

        Assert.True(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeAgreed, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(3, row.Sources.Count);
        Assert.Equal(4, row.Candidates.Count);
        Assert.Equal(4, row.CandidateCount);
        Assert.Equal(4, row.AgreementCount);
        Assert.Equal(0, row.DisagreementCount);
        Assert.All(row.Candidates, candidate => Assert.True(candidate.AllSourcesAgree));
        Assert.Contains(row.Candidates, candidate => candidate is
        {
            CandidateId: "candidate-dining",
            Category: FidelityCandidateCategory.Dining,
            SmallModelDecision: FidelityCandidateDecision.Include,
            StrongModelDecision: FidelityCandidateDecision.Include,
            HarnessOnlyDecision: FidelityCandidateDecision.Include
        });
        Assert.All(row.Sources, source =>
        {
            Assert.Equal(4, source.CandidateCount);
            Assert.Equal(MockFidelityEvalSource.ProviderName, source.Provider);
            Assert.Equal(MockFidelityEvalSource.ModelName, source.Model);
            Assert.NotNull(source.ResponseContentLength);
            Assert.NotNull(source.RequestId);
        });

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task DisagreementRowsCompareSourcesWithoutPersistingRawPayloads()
    {
        var evaluator = new FidelityEvaluator(CreateMockSources(
            strongBehavior: MockFidelityEvalBehavior.SchemaValidDisagreement));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("stage-6-disagreement", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeDisagreement, row.OutcomeCode);
        Assert.True(row.DisagreementCount > 0);
        Assert.NotEmpty(row.Candidates);
        Assert.NotEmpty(row.Sources);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Theory]
    [InlineData(MockFidelityEvalBehavior.SchemaInvalid, "fidelity_eval_schema_invalid", null)]
    [InlineData(MockFidelityEvalBehavior.UnsupportedClaim, "fidelity_eval_unsupported_claim", null)]
    [InlineData(MockFidelityEvalBehavior.MissingCandidateId, "fidelity_eval_missing_candidate_id", null)]
    [InlineData(MockFidelityEvalBehavior.FallbackRequired, "fidelity_eval_fallback_required", null)]
    [InlineData(MockFidelityEvalBehavior.Timeout, "fidelity_eval_source_error", "timeout")]
    [InlineData(MockFidelityEvalBehavior.ProviderError, "fidelity_eval_source_error", "provider_error")]
    public async Task MockBlockedBehaviorsUseFixedCodesAndNoRawArtifactValues(
        MockFidelityEvalBehavior behavior,
        string expectedOutcome,
        string? expectedErrorCode)
    {
        var evaluator = new FidelityEvaluator(CreateMockSources(smallBehavior: behavior));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase($"blocked-{behavior}", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Equal(expectedErrorCode, row.ErrorCode);
        Assert.Empty(row.Sources);
        Assert.Empty(row.Candidates);
        Assert.Equal(0, row.CandidateCount);
        Assert.Equal(0, row.AgreementCount);
        Assert.Equal(0, row.DisagreementCount);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnknownProviderCandidateIdsDoNotPersistRawProviderMetadata()
    {
        var packet = CreatePacket();
        var evaluator = new FidelityEvaluator(
            [
                new StaticFidelityEvalSource(
                    FidelityEvalSourceKind.SmallModel,
                    CreateResult(packet, FidelityEvalSourceKind.SmallModel) with
                    {
                        Candidates =
                        [
                            new FidelityCandidateVerdict(
                                RawProviderPayload,
                                FidelityCandidateDecision.Include)
                        ],
                        Provider = Credential,
                        Model = CandidateDisplayValue,
                        RequestId = ApprovalToken
                    }),
                new MockFidelityEvalSource(FidelityEvalSourceKind.StrongModel),
                new MockFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly)
            ]);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("unknown-candidate-id", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeMissingCandidateId, row.OutcomeCode);
        Assert.Empty(row.Sources);
        Assert.Empty(row.Candidates);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task PacketMismatchDoesNotPersistRawProviderPacketId()
    {
        var packet = CreatePacket();
        var evaluator = new FidelityEvaluator(
            [
                new StaticFidelityEvalSource(
                    FidelityEvalSourceKind.SmallModel,
                    CreateResult(packet, FidelityEvalSourceKind.SmallModel) with
                    {
                        PacketId = RawProviderPayload,
                        Provider = Credential,
                        Model = CandidateDisplayValue,
                        RequestId = ApprovalToken
                    }),
                new MockFidelityEvalSource(FidelityEvalSourceKind.StrongModel),
                new MockFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly)
            ]);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("packet-mismatch", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomePacketIdMismatch, row.OutcomeCode);
        Assert.Empty(row.Sources);
        Assert.Empty(row.Candidates);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task UnsupportedClaimDoesNotPersistRawClaimText()
    {
        var packet = CreatePacket();
        var evaluator = new FidelityEvaluator(
            [
                new StaticFidelityEvalSource(
                    FidelityEvalSourceKind.SmallModel,
                    CreateResult(packet, FidelityEvalSourceKind.SmallModel) with
                    {
                        ClaimCodes = [GenericSentinel],
                        Provider = Credential,
                        Model = CandidateDisplayValue,
                        RequestId = ApprovalToken
                    }),
                new MockFidelityEvalSource(FidelityEvalSourceKind.StrongModel),
                new MockFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly)
            ]);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("unsupported-claim", packet)]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeUnsupportedClaim, row.OutcomeCode);
        Assert.Empty(row.Sources);
        Assert.Empty(row.Candidates);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task SourceExceptionsUseFixedErrorCodesWithoutRawExceptionText()
    {
        var exception = new InvalidOperationException(
            $"{RawException} {RawPrompt} {RawProviderPayload} {ApprovalToken} {HoldReference} {CandidateDisplayValue} {Credential} {GenericSentinel}");
        var evaluator = new FidelityEvaluator(
            [
                new ThrowingFidelityEvalSource(FidelityEvalSourceKind.SmallModel, exception),
                new MockFidelityEvalSource(FidelityEvalSourceKind.StrongModel),
                new MockFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly)
            ]);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("source-error", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeSourceError, row.OutcomeCode);
        Assert.Equal("fidelity_eval_error", row.ErrorCode);
        Assert.Empty(row.Sources);
        Assert.Empty(row.Candidates);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    [Fact]
    public async Task FallbackRequiredBlocksWithoutEvaluatingLaterSources()
    {
        var strong = new CountingFidelityEvalSource(FidelityEvalSourceKind.StrongModel);
        var harness = new CountingFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly);
        var evaluator = new FidelityEvaluator(
            [
                new MockFidelityEvalSource(FidelityEvalSourceKind.SmallModel, MockFidelityEvalBehavior.FallbackRequired),
                strong,
                harness
            ]);

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new FidelityEvalCase("fallback-required", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(FidelityEvaluator.OutcomeFallbackRequired, row.OutcomeCode);
        Assert.Equal(0, strong.CallCount);
        Assert.Equal(0, harness.CallCount);

        SanitizedEvalArtifactAssert.DoesNotContainSensitiveValues(row, SensitiveSentinels);
    }

    private static IReadOnlyList<IFidelityEvalSource> CreateMockSources(
        MockFidelityEvalBehavior smallBehavior = MockFidelityEvalBehavior.SchemaValid,
        MockFidelityEvalBehavior strongBehavior = MockFidelityEvalBehavior.SchemaValid,
        MockFidelityEvalBehavior harnessBehavior = MockFidelityEvalBehavior.SchemaValid) =>
        [
            new MockFidelityEvalSource(FidelityEvalSourceKind.SmallModel, smallBehavior),
            new MockFidelityEvalSource(FidelityEvalSourceKind.StrongModel, strongBehavior),
            new MockFidelityEvalSource(FidelityEvalSourceKind.HarnessOnly, harnessBehavior)
        ];

    private static FidelityEvalPacket CreatePacket() =>
        new(
            "packet-fidelity",
            [
                new FidelityTrustedCandidate("candidate-dining", FidelityCandidateCategory.Dining),
                new FidelityTrustedCandidate("candidate-activity", FidelityCandidateCategory.Activity),
                new FidelityTrustedCandidate("candidate-transit", FidelityCandidateCategory.Transit),
                new FidelityTrustedCandidate("candidate-downtime", FidelityCandidateCategory.Downtime)
            ],
            "en-US",
            PromptDigest: $"{RawPrompt} {Credential}",
            ContextDigest: $"{RawProviderPayload} {ApprovalToken} {HoldReference} {CandidateDisplayValue}");

    private static FidelityEvalSourceResult CreateResult(
        FidelityEvalPacket packet,
        FidelityEvalSourceKind sourceKind) =>
        new(
            packet.PacketId,
            sourceKind,
            FidelityEvalSourceResultKind.Completed,
            packet.Candidates
                .Select(candidate => new FidelityCandidateVerdict(candidate.CandidateId, FidelityCandidateDecision.Include))
                .ToArray(),
            ["schema_valid", "candidate_id_set_complete", "decision_allowlisted", "harness_rule_comparable"],
            123,
            "provider-safe-unless-rejected",
            "model-safe-unless-rejected",
            "request-safe-unless-rejected");

    private sealed class StaticFidelityEvalSource(
        FidelityEvalSourceKind sourceKind,
        FidelityEvalSourceResult result) : IFidelityEvalSource
    {
        public FidelityEvalSourceKind SourceKind { get; } = sourceKind;

        public Task<FidelityEvalSourceResult> EvaluateAsync(
            FidelityEvalPacket packet,
            FidelityEvalOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingFidelityEvalSource(
        FidelityEvalSourceKind sourceKind,
        Exception exception) : IFidelityEvalSource
    {
        public FidelityEvalSourceKind SourceKind { get; } = sourceKind;

        public Task<FidelityEvalSourceResult> EvaluateAsync(
            FidelityEvalPacket packet,
            FidelityEvalOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<FidelityEvalSourceResult>(exception);
    }

    private sealed class CountingFidelityEvalSource(FidelityEvalSourceKind sourceKind) : IFidelityEvalSource
    {
        public FidelityEvalSourceKind SourceKind { get; } = sourceKind;

        public int CallCount { get; private set; }

        public Task<FidelityEvalSourceResult> EvaluateAsync(
            FidelityEvalPacket packet,
            FidelityEvalOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CreateResult(packet, SourceKind));
        }
    }
}
