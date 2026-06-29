using System.Text.Json;
using Pch.Providers.Mock;
using Pch.Providers.MissionPlanning;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class MissionPlannerRuntimeTests
{
    [Fact]
    public async Task RuntimeClientCreatesHandoffWithoutSerializedRawMissionValues()
    {
        var runtime = new MissionPlannerRuntimeClient(new MockMissionPlannerClient());

        var handoff = await runtime.RunAsync(CreatePacket("vacation", "SECRET_PROMPT_SHOULD_NOT_LEAK"));

        Assert.True(handoff.IsAccepted);
        Assert.Equal(MissionPlannerRuntimeBridge.DecodeAccepted, handoff.DecodeOutcomeCode);
        Assert.Equal(MissionPlannerRuntimeBridge.IntakeNotRunProviderLocalMirror, handoff.IntakeOutcomeCode);
        Assert.NotNull(handoff.Proposal);
        Assert.Equal("mission-proposal-packet-vacation", handoff.Proposal.ProposalId);
        Assert.Equal("vacation", handoff.Proposal.MissionKind);
        Assert.Contains("/mission/destination_country", handoff.Proposal.FieldPaths);
        Assert.Contains("commitment-rest", handoff.Proposal.CommitmentIds);
        Assert.Contains("downtime", handoff.Proposal.CommitmentKinds);
        Assert.Contains("constraint-flex-day", handoff.Proposal.ConstraintIds);
        Assert.NotNull(handoff.RuntimeProposal);
        Assert.Equal("Japan", handoff.RuntimeProposal.Result.Fields.Single(field => field.FieldPath == "/mission/destination_country").Value);

        var serialized = JsonSerializer.Serialize(handoff, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("runtimeProposal", serialized);
        Assert.DoesNotContain("SECRET_PROMPT_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("Japan", serialized);
        Assert.DoesNotContain("Protect downtime", serialized);
        Assert.DoesNotContain("Keep one flexible day", serialized);
        Assert.DoesNotContain("Vacation mission", serialized);
        Assert.DoesNotContain("evidence-model-pace", serialized);
    }

    [Fact]
    public void RuntimeBridgeRejectsPacketIdMismatchWithoutPersistedProposal()
    {
        const string sentinel = "RAW_PACKET_ID_SHOULD_NOT_LEAK";
        var packet = CreatePacket("business");
        var result = CreateSentinelResult(sentinel);

        var handoff = new MissionPlannerRuntimeBridge().Bridge(packet, result);

        Assert.False(handoff.IsAccepted);
        Assert.Equal(MissionPlannerRuntimeBridge.DecodePacketIdMismatch, handoff.DecodeOutcomeCode);
        Assert.Null(handoff.Proposal);
        Assert.Null(handoff.RuntimeProposal);

        var serialized = JsonSerializer.Serialize(handoff, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(sentinel, serialized);
        Assert.DoesNotContain("FIELD_VALUE_SENTINEL_SHOULD_NOT_LEAK", serialized);
    }

    [Fact]
    public void RuntimeBridgeRejectsMalformedResultWithFixedCode()
    {
        var packet = CreatePacket("business");
        var result = CreateSentinelResult(packet.PacketId) with { MissionKind = " " };

        var handoff = new MissionPlannerRuntimeBridge().Bridge(packet, result);

        Assert.False(handoff.IsAccepted);
        Assert.Equal(MissionPlannerRuntimeBridge.DecodeMalformedResult, handoff.DecodeOutcomeCode);
        Assert.Null(handoff.Proposal);
        Assert.Null(handoff.RuntimeProposal);
    }

    [Fact]
    public async Task RuntimeEvalRowsPersistOnlySanitizedCountsAndCodes()
    {
        const string promptSentinel = "PROMPT_SENTINEL_SHOULD_NOT_LEAK";
        var runner = new SanitizedMissionPlannerRuntimeEvalRunner(new MockMissionPlannerClient());

        var row = Assert.Single(await runner.EvaluateAsync(
            [new MissionPlannerEvalCase("vacation-runtime", CreatePacket("vacation", promptSentinel), "vacation")]));

        Assert.True(row.Passed);
        Assert.Equal(MissionPlannerRuntimeBridge.DecodeAccepted, row.DecodeOutcomeCode);
        Assert.Equal(MissionPlannerRuntimeBridge.IntakeNotRunProviderLocalMirror, row.IntakeOutcomeCode);
        Assert.Equal("vacation", row.ActualMissionKind);
        Assert.True(row.UserStatedFieldCount > 0);
        Assert.True(row.InferredFieldCount > 0);
        Assert.True(row.CommitmentCount > 0);
        Assert.True(row.ConstraintCount > 0);
        Assert.True(row.PendingConfirmationCount > 0);

        var serialized = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(promptSentinel, serialized);
        Assert.DoesNotContain("Japan", serialized);
        Assert.DoesNotContain("Protect downtime", serialized);
        Assert.DoesNotContain("Keep one flexible day", serialized);
        Assert.DoesNotContain("Vacation mission", serialized);
        Assert.DoesNotContain("evidence-model-pace", serialized);
    }

    [Fact]
    public async Task RuntimeEvalRowsUseFixedErrorCodesInsteadOfRawExceptionText()
    {
        const string sentinel = "RAW_EXCEPTION_SHOULD_NOT_LEAK";
        var runner = new SanitizedMissionPlannerRuntimeEvalRunner(new ThrowingPlanner(new InvalidOperationException(sentinel)));

        var row = Assert.Single(await runner.EvaluateAsync(
            [new MissionPlannerEvalCase("runtime-error", CreatePacket("helping_family"), "helping_family")]));

        Assert.False(row.Passed);
        Assert.Equal("mission_planner_decode_not_run", row.DecodeOutcomeCode);
        Assert.Equal("mission_intake_not_run", row.IntakeOutcomeCode);
        Assert.Equal("mission_planner_runtime_error", row.ErrorCode);

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

    private static MissionPlannerResult CreateSentinelResult(string packetId) =>
        new(
            packetId,
            "business",
            [
                new MissionFieldProposal(
                    "/mission/purpose",
                    "FIELD_VALUE_SENTINEL_SHOULD_NOT_LEAK",
                    MissionProposalSource.UserStated,
                    ["evidence-field-sentinel"],
                    false)
            ],
            [
                new MissionCommitmentProposal(
                    "commitment-sentinel",
                    "sentinel_kind",
                    "COMMITMENT_TITLE_SENTINEL_SHOULD_NOT_LEAK",
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
                    "CONSTRAINT_VALUE_SENTINEL_SHOULD_NOT_LEAK",
                    MissionProposalSource.ModelInferred,
                    IsHard: true,
                    ["evidence-constraint-sentinel"])
            ],
            ["Confirm sentinel."],
            "MEMORY_DIGEST_SENTINEL_SHOULD_NOT_LEAK",
            42,
            "test-provider",
            "test-model",
            "request-sentinel-safe-id");

    private sealed class ThrowingPlanner(Exception exception) : IMissionPlannerClient
    {
        public Task<MissionPlannerResult> PlanAsync(
            MissionPlannerPacket packet,
            MissionPlannerOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MissionPlannerResult>(exception);
    }
}
