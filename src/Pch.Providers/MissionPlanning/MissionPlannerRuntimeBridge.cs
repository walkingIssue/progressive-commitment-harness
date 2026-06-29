namespace Pch.Providers.MissionPlanning;

public sealed class MissionPlannerRuntimeBridge
{
    public const string DecodeAccepted = "mission_planner_decode_accepted";
    public const string DecodePacketIdMismatch = "mission_planner_decode_packet_id_mismatch";
    public const string DecodeMalformedResult = "mission_planner_decode_malformed_result";
    public const string DecodeUnsupportedMissionKind = "mission_planner_decode_unsupported_mission_kind";
    public const string IntakeNotRunProviderLocalMirror = "mission_intake_not_run_provider_local_mirror";

    public MissionPlannerRuntimeHandoffResult Bridge(MissionPlannerPacket packet, MissionPlannerResult result)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(DecodePacketIdMismatch);
        }

        if (result.Fields is null ||
            result.Commitments is null ||
            result.Constraints is null ||
            result.PendingConfirmations is null)
        {
            return Rejected(DecodeMalformedResult);
        }

        if (!MissionKindPolicy.IsAllowed(result.MissionKind))
        {
            return Rejected(DecodeUnsupportedMissionKind);
        }

        var proposalId = $"mission-proposal-{packet.PacketId}";
        var fieldPaths = result.Fields
            .Select(field => field.FieldPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var commitmentIds = result.Commitments
            .Select(commitment => commitment.CommitmentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var commitmentKinds = result.Commitments
            .Select(commitment => commitment.CommitmentKind)
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var constraintIds = result.Constraints
            .Select(constraint => constraint.ConstraintId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var proposal = new ProviderLocalMissionIntakeProposalMetadata(
            proposalId,
            packet.PacketId,
            result.MissionKind,
            fieldPaths,
            commitmentIds,
            commitmentKinds,
            constraintIds,
            result.Fields.Count(field => field.AuthoritySource == MissionProposalSource.UserStated),
            result.Fields.Count(field => field.AuthoritySource == MissionProposalSource.ModelInferred),
            result.Commitments.Count,
            result.Constraints.Count,
            result.PendingConfirmations.Count,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
        var runtimeProposal = new ProviderRuntimeMissionIntakeProposal(proposalId, result);

        return new MissionPlannerRuntimeHandoffResult(
            true,
            DecodeAccepted,
            IntakeNotRunProviderLocalMirror,
            runtimeProposal,
            proposal);
    }

    private static MissionPlannerRuntimeHandoffResult Rejected(string decodeOutcomeCode) =>
        new(
            false,
            decodeOutcomeCode,
            IntakeNotRunProviderLocalMirror,
            null,
            null);
}
