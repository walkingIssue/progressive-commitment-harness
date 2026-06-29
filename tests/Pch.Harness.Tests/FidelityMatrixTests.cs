using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class FidelityMatrixTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DefaultMatrixCoversStagePacketsReplayCasesAndOwnershipOutcomes()
    {
        var result = new FidelityMatrix().BuildDefaultMatrix();

        Assert.True(result.IsAccepted);
        Assert.Equal(FidelityMatrix.MatrixCompleteCode, result.Code);
        Assert.Equal(Enum.GetValues<HarnessStage>().Length + 8, result.EntryCount);
        Assert.True(result.Totals.HarnessOnlyCount > 0);
        Assert.True(result.Totals.SmallModelCandidateCount > 0);
        Assert.True(result.Totals.StrongModelRequiredCount > 0);
        Assert.True(result.Totals.BlockedUntilReviewCount > 0);
        Assert.Contains(result.Entries, entry => entry.Stage == HarnessStage.Intake.ToString()
            && entry.Ownership == FidelityMatrix.HarnessOnlyOutcome);
        Assert.Contains(result.Entries, entry => entry.Stage == HarnessStage.DaySkeletonGeneration.ToString()
            && entry.Ownership == FidelityMatrix.SmallModelCandidateOutcome);
        Assert.Contains(result.Entries, entry => entry.Stage == HarnessStage.ConflictVerify.ToString()
            && entry.Ownership == FidelityMatrix.StrongModelRequiredOutcome);
        Assert.Contains(result.Entries, entry => entry.Stage == HarnessStage.Logistics.ToString()
            && entry.Ownership == FidelityMatrix.BlockedUntilReviewOutcome
            && !entry.Metrics.CandidateIdsPreserved);
        Assert.Contains(result.Entries, entry => entry.Stage == HarnessStage.ApprovalQueue.ToString()
            && entry.Ownership == FidelityMatrix.BlockedUntilReviewOutcome);
        Assert.Contains(result.Entries, entry => entry.InputKind == "trip_run_replay"
            && entry.Scenario == "pending_confirmation"
            && entry.Ownership == FidelityMatrix.StrongModelRequiredOutcome);
        Assert.Contains(result.Entries, entry => entry.InputKind == "trip_run_replay"
            && entry.Scenario == "missing_approval"
            && entry.Ownership == FidelityMatrix.BlockedUntilReviewOutcome);
    }

    [Fact]
    public void DefaultMatrixOutputIsDeterministic()
    {
        var matrix = new FidelityMatrix();

        var first = matrix.BuildDefaultMatrix();
        var second = matrix.BuildDefaultMatrix();

        Assert.Equal(
            JsonSerializer.Serialize(first, JsonOptions),
            JsonSerializer.Serialize(second, JsonOptions));
    }

    [Fact]
    public void MatrixEntriesExposeBoundedReferencesAndMetrics()
    {
        var result = new FidelityMatrix().BuildDefaultMatrix();

        Assert.All(result.Entries, entry =>
        {
            Assert.True(entry.EvidenceReferences.Count <= 8);
            Assert.True(entry.TraceReferences.Count <= 8);
            Assert.True(entry.Metrics.SchemaValid);
            Assert.True(entry.Metrics.Faithful);
            Assert.Equal(0, entry.Metrics.UnsupportedClaimCount);
            Assert.True(entry.Metrics.IsReadOnly);
            Assert.True(entry.Metrics.MutationSafe);
        });
        Assert.All(
            result.Entries.Where(entry => entry.Ownership != FidelityMatrix.BlockedUntilReviewOutcome),
            entry => Assert.True(entry.Metrics.CandidateIdsPreserved));
        Assert.Contains(result.Entries, entry => !entry.Metrics.CandidateIdsPreserved
            && entry.Ownership == FidelityMatrix.BlockedUntilReviewOutcome);
        Assert.True(result.Totals.FallbackNeedCount > 0);
    }

    [Fact]
    public void MatrixBuildDoesNotMutateSessionDerivedInputs()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var before = SessionCounts(session);
        var packet = new ProjectionService().Project(session, HarnessStage.Logistics);
        var replayCase = new TripRunReplayAudit().ReplayCase("case-read-only", "blocked_compiler", session);
        var audit = new TripRunReplayAuditResult(
            true,
            TripRunReplayAudit.AuditCompleteCode,
            "Trip run replay audit corpus completed.",
            1,
            [replayCase]);

        var result = new FidelityMatrix().Build(new FidelityMatrixRequest([packet], audit));
        var after = SessionCounts(session);

        Assert.Equal(before, after);
        Assert.Single(result.Entries, entry => entry.InputKind == "stage_packet");
        Assert.Single(result.Entries, entry => entry.InputKind == "trip_run_replay");
    }

    [Fact]
    public void MatrixBlocksUnsafeInputsWithoutRawSentinelLeakage()
    {
        const string providerSentinel = "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK";
        const string promptSentinel = "RAW_PROMPT_SHOULD_NOT_LEAK";
        const string credentialSentinel = "RAW_CREDENTIAL_SHOULD_NOT_LEAK";
        const string approvalSentinel = "RAW_APPROVAL_TOKEN_SHOULD_NOT_LEAK";
        const string holdSentinel = "RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK";
        const string secretSentinel = "SECRET_API_KEY_SHOULD_NOT_LEAK";
        var packet = new StagePacket(
            $"packet-{providerSentinel}",
            $"session-{credentialSentinel}",
            HarnessStage.Logistics.ToString(),
            "Compare logistics candidates.",
            [$"purpose: {promptSentinel}"],
            [
                new(
                    "candidate-safe",
                    CandidateKind.Activity.ToString(),
                    providerSentinel,
                    credentialSentinel,
                    [$"evidence-{holdSentinel}"])
            ],
            ["constraint-safe: value"],
            ["Every user-visible claim must cite evidence IDs."],
            [HarnessAction.EmitChoiceSetKind],
            ["Preserve candidate IDs.", secretSentinel]);
        var replayCase = new TripRunReplayCaseResult(
            $"case-{providerSentinel}",
            $"scenario-{promptSentinel}",
            TripRunReplayAudit.ReplayCompleteSnapshotCode,
            $"summary-{credentialSentinel}",
            TripRunSnapshotBuilder.CompleteCode,
            $"snapshot-{promptSentinel}",
            true,
            false,
            $"snapshot-{providerSentinel}",
            $"session-{credentialSentinel}",
            "mission-safe",
            new string('a', 64),
            true,
            true,
            2,
            2,
            ["evidence-safe", holdSentinel],
            [new("trace-safe", "kind-safe"), new($"trace-{secretSentinel}", approvalSentinel)]);
        var audit = new TripRunReplayAuditResult(
            true,
            TripRunReplayAudit.AuditCompleteCode,
            "Trip run replay audit corpus completed.",
            1,
            [replayCase]);

        var result = new FidelityMatrix().Build(new FidelityMatrixRequest([packet], audit));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.Equal(FidelityMatrix.MatrixBlockedCode, result.Code);
        Assert.All(result.Entries, entry => Assert.Equal(FidelityMatrix.BlockedUntilReviewOutcome, entry.Ownership));
        Assert.True(result.Totals.UnsupportedClaimCount > 0);
        Assert.DoesNotContain(providerSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(promptSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(credentialSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(approvalSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(holdSentinel, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(secretSentinel, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRequestUsesFixedSanitizedCode()
    {
        var result = new FidelityMatrix().Build(null!);

        Assert.False(result.IsAccepted);
        Assert.Equal(FidelityMatrix.InvalidInputCode, result.Code);
        Assert.Equal("Fidelity matrix request failed validation.", result.Summary);
        Assert.Empty(result.Entries);
    }

    private static (int Actions, int Decisions, int Approvals, int ItineraryDecisions) SessionCounts(TripSession session)
    {
        return (
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ApprovalTokens.Count,
            session.ItineraryDecisions.Count);
    }
}
