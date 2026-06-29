using System.Text.Json;
using Pch.Providers.EvidenceExport;
using Pch.Providers.Mock;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class EvidenceExportTests
{
    [Fact]
    public async Task MockExporterReturnsDeterministicExport()
    {
        var provider = new MockEvidenceExportProvider();

        var result = await provider.ExportAsync(CreatePacket());

        Assert.Equal("packet-export", result.PacketId);
        Assert.Equal(EvidenceExportResultKind.ExportReady, result.Kind);
        Assert.Equal("plan-safe", result.Export.PlanId);
        Assert.Equal(2, result.Export.SelectedCandidateCount);
        Assert.Equal(1, result.Export.DeferredCandidateCount);
        Assert.Equal(1, result.Export.PreparedHoldCount);
        Assert.Equal(["evidence-candidate", "evidence-hold", "evidence-summary"], result.Export.EvidenceIds);
        Assert.Equal(MockEvidenceExportProvider.ProviderName, result.Provider);
        Assert.Equal("mock-evidence-export-deterministic", result.Model);
        Assert.Equal("mock-evidence-packet-export", result.RequestId);
    }

    [Fact]
    public async Task SanitizedRowsPersistOnlyTrustedCountsIdsAndMetadata()
    {
        const string contextSentinel = "RAW_PROMPT_PROVIDER_PAYLOAD_APPROVAL_TOKEN_SHOULD_NOT_PERSIST";
        var evaluator = new EvidenceExportEvaluator(new MockEvidenceExportProvider());

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new EvidenceExportEvalCase("export", CreatePacket(contextSentinel))]));

        Assert.True(row.Passed);
        Assert.Equal(EvidenceExportEvaluator.OutcomeExportReady, row.OutcomeCode);
        Assert.Equal("plan-safe", row.PlanId);
        Assert.Equal(2, row.SelectedCandidateCount);
        Assert.Equal(1, row.DeferredCandidateCount);
        Assert.Equal(1, row.PreparedHoldCount);
        Assert.Equal(3, row.EvidenceCount);
        Assert.Equal(["evidence-candidate", "evidence-hold", "evidence-summary"], row.EvidenceIds);
        Assert.Equal(["slot-dining", "slot-transit"], row.SlotIds);
        Assert.Equal(["candidate-dining", "candidate-transit"], row.CandidateIds);
        Assert.Equal(MockEvidenceExportProvider.ProviderName, row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(contextSentinel, serialized);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("Candidate Display Should Not Persist", serialized);
        Assert.DoesNotContain("credential", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PacketMismatchUsesFixedOutcomeWithoutRawResultIds()
    {
        var evaluator = new EvidenceExportEvaluator(new MockEvidenceExportProvider(MockEvidenceExportBehavior.PacketMismatch));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new EvidenceExportEvalCase("packet-mismatch", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(EvidenceExportEvaluator.OutcomePacketIdMismatch, row.OutcomeCode);
        Assert.Null(row.PlanId);
        Assert.Empty(row.EvidenceIds);
        Assert.Null(row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", serialized);
    }

    [Fact]
    public async Task ResultMismatchUsesFixedOutcomeWithoutRawProviderExportValues()
    {
        const string contextSentinel = "RAW_CONTEXT_SENTINEL_SHOULD_NOT_PERSIST";
        var evaluator = new EvidenceExportEvaluator(new MockEvidenceExportProvider(MockEvidenceExportBehavior.ResultMismatch));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new EvidenceExportEvalCase("result-mismatch", CreatePacket(contextSentinel))]));

        Assert.False(row.Passed);
        Assert.Equal(EvidenceExportEvaluator.OutcomeResultMismatch, row.OutcomeCode);
        Assert.Null(row.PlanId);
        Assert.Empty(row.EvidenceIds);
        Assert.Null(row.Provider);
        Assert.Null(row.RequestId);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(contextSentinel, serialized);
        Assert.DoesNotContain("RAW_PLAN_ID_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("RAW_EVIDENCE_ID_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("mock-evidence", serialized);
    }

    [Theory]
    [InlineData(MockEvidenceExportBehavior.Unsupported, "evidence_export_unsupported")]
    [InlineData(MockEvidenceExportBehavior.Malformed, "evidence_export_malformed")]
    public async Task UnsupportedAndMalformedUseFixedOutcomesWithoutRawProviderValues(
        MockEvidenceExportBehavior behavior,
        string expectedOutcome)
    {
        var evaluator = new EvidenceExportEvaluator(new MockEvidenceExportProvider(behavior));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new EvidenceExportEvalCase("blocked", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(expectedOutcome, row.OutcomeCode);
        Assert.Empty(row.CandidateIds);
        Assert.Null(row.Provider);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST", serialized);
        Assert.DoesNotContain("mock-evidence", serialized);
    }

    [Fact]
    public async Task ProviderExceptionUsesFixedErrorCodeWithoutRawExceptionText()
    {
        var evaluator = new EvidenceExportEvaluator(new MockEvidenceExportProvider(MockEvidenceExportBehavior.ProviderUnavailable));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new EvidenceExportEvalCase("provider-error", CreatePacket())]));

        Assert.False(row.Passed);
        Assert.Equal(EvidenceExportEvaluator.OutcomeError, row.OutcomeCode);
        Assert.Equal("provider_error", row.ErrorCode);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("Mock evidence export provider unavailable", serialized);
    }

    private static EvidenceExportPacket CreatePacket(string? contextDigest = null) =>
        new(
            "packet-export",
            new TripPlanEvidenceSummary(
                "plan-safe",
                DayCount: 2,
                SelectedCandidateCount: 2,
                DeferredCandidateCount: 1,
                PreparedHoldCount: 1,
                EvidenceCount: 3),
            [
                new TripPlanEvidenceItem("evidence-summary", EvidenceKind.MissionField, "mission-purpose"),
                new TripPlanEvidenceItem("evidence-candidate", EvidenceKind.Candidate, "candidate-dining"),
                new TripPlanEvidenceItem("evidence-hold", EvidenceKind.Hold, "slot-dining")
            ],
            [
                new TripPlanHoldOutcome("slot-dining", "candidate-dining", HoldOutcomeKind.HoldPrepared, "evidence-hold"),
                new TripPlanHoldOutcome("slot-transit", "candidate-transit", HoldOutcomeKind.Deferred, "evidence-candidate")
            ],
            "en-US",
            contextDigest ?? "Candidate Display Should Not Persist; RAW_HOLD_REFERENCE_SHOULD_NOT_PERSIST; credential sentinel");
}
