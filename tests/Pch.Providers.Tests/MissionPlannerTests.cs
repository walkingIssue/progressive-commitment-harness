using System.Text.Json;
using Pch.Providers.Mock;
using Pch.Providers.MissionPlanning;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class MissionPlannerTests
{
    [Theory]
    [InlineData("vacation", "vacation")]
    [InlineData("business", "business")]
    [InlineData("funeral_downtime", "funeral_downtime")]
    [InlineData("helping_family", "helping_family")]
    public async Task MockPlannerReturnsDeterministicScenarioOutput(string scenario, string expectedKind)
    {
        var planner = new MockMissionPlannerClient();

        var result = await planner.PlanAsync(CreatePacket(scenario));

        Assert.Equal(expectedKind, result.MissionKind);
        Assert.Equal("mock-mission-planner", result.Provider);
        Assert.NotEmpty(result.Fields);
        Assert.Contains(result.Fields, field => field.AuthoritySource == MissionProposalSource.UserStated);
        Assert.Contains(result.Fields, field => field.AuthoritySource == MissionProposalSource.ModelInferred);
        Assert.Contains(result.Fields, field => field.FieldPath == "/mission/purpose");
        Assert.All(result.Fields, field =>
        {
            Assert.StartsWith("/mission/", field.FieldPath, StringComparison.Ordinal);
            Assert.NotEmpty(field.Value);
            Assert.NotEmpty(field.EvidenceIds);
        });
        Assert.NotEmpty(result.Constraints);
        Assert.All(result.Constraints, constraint =>
        {
            Assert.NotEmpty(constraint.ConstraintId);
            Assert.NotEmpty(constraint.Label);
            Assert.NotEmpty(constraint.Value);
            Assert.NotEmpty(constraint.EvidenceIds);
        });
        Assert.NotEmpty(result.Commitments);
        Assert.All(result.Commitments, commitment =>
        {
            Assert.NotEmpty(commitment.CommitmentId);
            Assert.NotEmpty(commitment.CommitmentKind);
            Assert.NotEmpty(commitment.Title);
            Assert.NotEmpty(commitment.EvidenceIds);
        });
        Assert.NotEmpty(result.PendingConfirmations);
        Assert.NotEmpty(result.MemoryDigest);
    }

    [Fact]
    public async Task SanitizedEvalRowsDoNotPersistRawPromptOrProviderPayload()
    {
        const string sentinel = "SECRET_PROMPT_SHOULD_NOT_PERSIST";
        var evaluator = new MissionPlannerEvaluator(new MockMissionPlannerClient());
        var cases = new[]
        {
            new MissionPlannerEvalCase("vacation-case", CreatePacket("vacation", sentinel), "vacation")
        };

        var row = Assert.Single(await evaluator.EvaluateAsync(cases));

        Assert.True(row.Passed);
        Assert.Equal(MissionPlannerEvaluator.OutcomeAccepted, row.OutcomeCode);
        Assert.Equal("vacation", row.ActualMissionKind);
        Assert.True(row.UserStatedFieldCount > 0);
        Assert.True(row.InferredFieldCount > 0);
        Assert.True(row.CommitmentCount > 0);
        Assert.True(row.ConstraintCount > 0);
        Assert.True(row.PendingConfirmationCount > 0);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(sentinel, serialized);
        Assert.DoesNotContain("Vacation mission", serialized);
        Assert.DoesNotContain("Japan", serialized);
        Assert.DoesNotContain("Protect downtime", serialized);
        Assert.DoesNotContain("Keep one flexible day", serialized);
        Assert.DoesNotContain("evidence-model-pace", serialized);
    }

    [Fact]
    public async Task SanitizedEvalRowsDoNotPersistStructuredProposalValues()
    {
        const string fieldValue = "FIELD_VALUE_SENTINEL_SHOULD_NOT_PERSIST";
        const string commitmentTitle = "COMMITMENT_TITLE_SENTINEL_SHOULD_NOT_PERSIST";
        const string constraintValue = "CONSTRAINT_VALUE_SENTINEL_SHOULD_NOT_PERSIST";
        const string memoryDigest = "MEMORY_DIGEST_SENTINEL_SHOULD_NOT_PERSIST";
        const string credential = "sk-credential-sentinel-should-not-persist";
        var evaluator = new MissionPlannerEvaluator(new SentinelPlanner(
            fieldValue,
            commitmentTitle,
            constraintValue,
            memoryDigest));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MissionPlannerEvalCase("sentinel", CreatePacket("vacation", credential), "vacation")]));

        Assert.True(row.Passed);
        Assert.Equal(1, row.UserStatedFieldCount);
        Assert.Equal(1, row.CommitmentCount);
        Assert.Equal(1, row.ConstraintCount);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(fieldValue, serialized);
        Assert.DoesNotContain(commitmentTitle, serialized);
        Assert.DoesNotContain(constraintValue, serialized);
        Assert.DoesNotContain(memoryDigest, serialized);
        Assert.DoesNotContain(credential, serialized);
    }

    [Fact]
    public async Task EvalRowsUseFixedCodesForPacketMismatch()
    {
        var evaluator = new MissionPlannerEvaluator(new MismatchedPlanner());
        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MissionPlannerEvalCase("mismatch", CreatePacket("business"), "business")]));

        Assert.False(row.Passed);
        Assert.Equal(MissionPlannerEvaluator.OutcomePacketIdMismatch, row.OutcomeCode);
        Assert.Null(row.ActualMissionKind);
        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_PACKET_ID_SHOULD_NOT_PERSIST", serialized);
    }

    [Fact]
    public async Task EvalRowsUseFixedErrorCodeInsteadOfRawExceptionText()
    {
        const string sentinel = "RAW_EXCEPTION_SHOULD_NOT_PERSIST";
        var evaluator = new MissionPlannerEvaluator(new ThrowingPlanner(new InvalidOperationException(sentinel)));

        var row = Assert.Single(await evaluator.EvaluateAsync(
            [new MissionPlannerEvalCase("error", CreatePacket("helping_family"), "helping_family")]));

        Assert.False(row.Passed);
        Assert.Equal(MissionPlannerEvaluator.OutcomeError, row.OutcomeCode);
        Assert.Equal("mission_planner_error", row.ErrorCode);
        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(sentinel, serialized);
    }

    private static MissionPlannerPacket CreatePacket(string scenario, string? prompt = null) =>
        new(
            $"packet-{scenario}",
            scenario,
            prompt ?? $"Plan this {scenario} trip.",
            "en-US",
            ["keep diagnostics sanitized"]);

    private sealed class MismatchedPlanner : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MissionPlannerResult(
                "RAW_PACKET_ID_SHOULD_NOT_PERSIST",
                "business",
                [
                    new MissionFieldProposal(
                        "/mission/purpose",
                        "business",
                        MissionProposalSource.UserStated,
                        ["evidence-user-purpose"],
                        false)
                ],
                [],
                [],
                [],
                "digest",
                0,
                "test-provider",
                "test-model",
                "request-1"));
        }
    }

    private sealed class ThrowingPlanner(Exception exception) : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MissionPlannerResult>(exception);
    }

    private sealed class SentinelPlanner(
        string fieldValue,
        string commitmentTitle,
        string constraintValue,
        string memoryDigest) : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MissionPlannerResult(
                packet.PacketId,
                "vacation",
                [
                    new MissionFieldProposal(
                        "/mission/purpose",
                        fieldValue,
                        MissionProposalSource.UserStated,
                        ["evidence-field-sentinel"],
                        false)
                ],
                [
                    new MissionCommitmentProposal(
                        "commitment-sentinel",
                        "sentinel_kind",
                        commitmentTitle,
                        StartsAt: null,
                        EndsAt: null,
                        Location: null,
                        IsIrreversible: false,
                        RequiresSpend: true,
                        MissionCommitmentPriority.High,
                        MissionProposalSource.UserStated,
                        ["evidence-commitment-sentinel"])
                ],
                [
                    new MissionConstraintProposal(
                        "constraint-sentinel",
                        "Sentinel constraint",
                        constraintValue,
                        MissionProposalSource.ModelInferred,
                        IsHard: true,
                        ["evidence-constraint-sentinel"])
                ],
                ["Confirm sentinel."],
                memoryDigest,
                42,
                "test-provider",
                "test-model",
                "request-sentinel-safe-id"));
        }
    }
}
