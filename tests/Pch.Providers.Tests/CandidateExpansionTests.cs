using System.Text.Json;
using Pch.Providers.CandidateExpansion;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class CandidateExpansionTests
{
    [Fact]
    public async Task MockSourceReturnsDeterministicCandidatesForAllCategories()
    {
        var source = new MockCandidateExpansionSource();

        var result = await source.ExpandAsync(CreatePacket());

        Assert.Equal("packet-candidates", result.PacketId);
        Assert.Equal(MockCandidateExpansionSource.ProviderName, result.Provider);
        Assert.Equal("mock-candidates-packet-candidates", result.RequestId);
        Assert.Equal(4, result.Slots.Count);
        Assert.Contains(result.Slots, slot => slot.Category == CandidateCategory.Dining);
        Assert.Contains(result.Slots, slot => slot.Category == CandidateCategory.Activity);
        Assert.Contains(result.Slots, slot => slot.Category == CandidateCategory.Transit);
        Assert.Contains(result.Slots, slot => slot.Category == CandidateCategory.Downtime);
        Assert.All(result.Slots, slot =>
        {
            Assert.Equal(2, slot.Candidates.Count);
            Assert.All(slot.Candidates, candidate =>
            {
                Assert.Equal(slot.Category, candidate.Category);
                Assert.StartsWith(slot.SlotId, candidate.CandidateId, StringComparison.Ordinal);
                Assert.NotEmpty(candidate.DisplayName);
                Assert.NotEmpty(candidate.Tags);
            });
        });
    }

    [Fact]
    public async Task SanitizedEvalRowsPersistOnlySlotCountsAndProviderMetadata()
    {
        const string contextSentinel = "RAW_CONTEXT_SENTINEL_SHOULD_NOT_PERSIST";
        var evaluator = new CandidateExpansionEvaluator(new MockCandidateExpansionSource());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new CandidateExpansionEvalCase("candidate-case", CreatePacket(contextSentinel))]));

        Assert.True(row.Passed);
        Assert.Equal(CandidateExpansionEvaluator.OutcomeAccepted, row.OutcomeCode);
        Assert.Null(row.ErrorCode);
        Assert.Equal(4, row.Slots.Count);
        Assert.Equal(8, row.TotalCandidateCount);
        Assert.Equal(MockCandidateExpansionSource.ProviderName, row.Provider);
        Assert.Equal("mock-candidate-expansion-deterministic", row.Model);
        Assert.Equal("mock-candidates-packet-candidates", row.RequestId);
        Assert.Contains(row.Slots, slot => slot is { SlotId: "slot-dining", Category: CandidateCategory.Dining, CandidateCount: 2 });
        Assert.Contains(row.Slots, slot => slot is { SlotId: "slot-activity", Category: CandidateCategory.Activity, CandidateCount: 2 });
        Assert.Contains(row.Slots, slot => slot is { SlotId: "slot-transit", Category: CandidateCategory.Transit, CandidateCount: 2 });
        Assert.Contains(row.Slots, slot => slot is { SlotId: "slot-downtime", Category: CandidateCategory.Downtime, CandidateCount: 2 });

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(contextSentinel, serialized);
        Assert.DoesNotContain("Neighborhood lunch", serialized);
        Assert.DoesNotContain("Museum visit", serialized);
        Assert.DoesNotContain("Direct train transfer", serialized);
        Assert.DoesNotContain("Flexible rest window", serialized);
    }

    [Fact]
    public async Task EvalRowsUseFixedOutcomeForPacketMismatchWithoutRawResultIds()
    {
        const string sentinel = "RAW_PACKET_ID_SHOULD_NOT_PERSIST";
        var evaluator = new CandidateExpansionEvaluator(new MismatchedSource(sentinel));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new CandidateExpansionEvalCase("mismatch", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(CandidateExpansionEvaluator.OutcomePacketIdMismatch, row.OutcomeCode);
        Assert.Empty(row.Slots);
        Assert.Equal(0, row.TotalCandidateCount);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(sentinel, serialized);
        Assert.DoesNotContain("MISMATCH_CANDIDATE_SHOULD_NOT_PERSIST", serialized);
    }

    [Fact]
    public async Task EvalRowsUseFixedErrorCodeInsteadOfRawExceptionText()
    {
        const string sentinel = "RAW_EXCEPTION_SHOULD_NOT_PERSIST";
        var evaluator = new CandidateExpansionEvaluator(new ThrowingSource(new InvalidOperationException(sentinel)));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new CandidateExpansionEvalCase("error", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(CandidateExpansionEvaluator.OutcomeError, row.OutcomeCode);
        Assert.Equal("candidate_expansion_error", row.ErrorCode);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(sentinel, serialized);
    }

    private static CandidateExpansionPacket CreatePacket(string? contextDigest = null) =>
        new(
            "packet-candidates",
            [
                new CandidateExpansionSlot("slot-dining", CandidateCategory.Dining, "safe area hint", 75),
                new CandidateExpansionSlot("slot-activity", CandidateCategory.Activity, "safe area hint", 120),
                new CandidateExpansionSlot("slot-transit", CandidateCategory.Transit, "safe station hint", 45),
                new CandidateExpansionSlot("slot-downtime", CandidateCategory.Downtime, null, 90)
            ],
            "en-US",
            contextDigest);

    private sealed class MismatchedSource(string packetId) : ICandidateExpansionSource
    {
        public Task<CandidateExpansionResult> ExpandAsync(
            CandidateExpansionPacket packet,
            CandidateExpansionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CandidateExpansionResult(
                packetId,
                [
                    new CandidateSlotExpansion(
                        "slot-dining",
                        CandidateCategory.Dining,
                        [
                            new ItineraryCandidate(
                                "candidate-mismatch",
                                CandidateCategory.Dining,
                                "MISMATCH_CANDIDATE_SHOULD_NOT_PERSIST",
                                ["sentinel"],
                                60,
                                CandidateCostLevel.High,
                                RequiresBooking: true)
                        ])
                ],
                42,
                "test-provider",
                "test-model",
                "request-1"));
    }

    private sealed class ThrowingSource(Exception exception) : ICandidateExpansionSource
    {
        public Task<CandidateExpansionResult> ExpandAsync(
            CandidateExpansionPacket packet,
            CandidateExpansionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CandidateExpansionResult>(exception);
    }
}
