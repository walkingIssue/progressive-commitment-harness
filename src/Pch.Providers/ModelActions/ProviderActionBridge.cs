namespace Pch.Providers.ModelActions;

public sealed class ProviderActionBridge
{
    public const string DecodeAccepted = "decode_accepted";
    public const string DecodeActionOutsideAllowedSet = "decode_action_outside_allowed_set";
    public const string DecodeMissingArguments = "decode_missing_arguments";
    public const string DecodePacketIdMismatch = "decode_packet_id_mismatch";
    public const string IntakeNotRunProviderLocalMirror = "intake_not_run_provider_local_mirror";

    public ProviderActionBridgeResult Bridge(ModelActionPacket packet, ModelActionRunResult result)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(DecodePacketIdMismatch);
        }

        if (!packet.AllowedActions.Any(action => string.Equals(action.Name, result.ActionName, StringComparison.Ordinal)))
        {
            return Rejected(DecodeActionOutsideAllowedSet);
        }

        if (result.Arguments.ValueKind is not System.Text.Json.JsonValueKind.Object)
        {
            return Rejected(DecodeMissingArguments);
        }

        var argumentKeys = result.Arguments.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var proposal = new ProviderLocalExternalActionProposal(
            $"proposal-{packet.PacketId}",
            packet.PacketId,
            result.ActionName,
            argumentKeys,
            result.Provider,
            result.Model,
            result.RequestId);
        var runtimeProposal = new ProviderRuntimeActionProposal(
            proposal.ProposalId,
            result.ActionName,
            result.Arguments.Clone());

        return new ProviderActionBridgeResult(
            true,
            DecodeAccepted,
            IntakeNotRunProviderLocalMirror,
            runtimeProposal,
            proposal);
    }

    private static ProviderActionBridgeResult Rejected(string decodeOutcomeCode) =>
        new(
            false,
            decodeOutcomeCode,
            IntakeNotRunProviderLocalMirror,
            null,
            null);
}
