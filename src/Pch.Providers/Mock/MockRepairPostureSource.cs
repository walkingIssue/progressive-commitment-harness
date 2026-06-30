using Pch.Providers.Errors;
using Pch.Providers.RepairPosture;

namespace Pch.Providers.Mock;

public sealed class MockRepairPostureSource : IRepairPostureSource
{
    public const string ProviderName = "mock-repair-posture";
    public const string ModelName = "mock-repair-posture-deterministic";

    private readonly MockRepairPostureBehavior _behavior;

    public MockRepairPostureSource(MockRepairPostureBehavior behavior = MockRepairPostureBehavior.Accepted)
    {
        _behavior = behavior;
    }

    public Task<RepairPostureResult> SuggestAsync(
        RepairPosturePacket packet,
        RepairPostureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockRepairPostureBehavior.PacketMismatch => Task.FromResult(CreateResult(packet, "RAW_PACKET_ID_SHOULD_NOT_PERSIST")),
            MockRepairPostureBehavior.NodeMismatch => Task.FromResult(CreateResult(packet, packet.PacketId, nodeIdOverride: "RAW_NODE_ID_SHOULD_NOT_PERSIST")),
            MockRepairPostureBehavior.UnsupportedMode => Task.FromResult(CreateResult(packet, packet.PacketId, modeOverride: (RepairMode)999)),
            MockRepairPostureBehavior.MalformedResult => Task.FromResult(new RepairPostureResult(packet.PacketId, null!, 88, ProviderName, ModelName, "request-malformed")),
            MockRepairPostureBehavior.ProviderTimeout => Task.FromException<RepairPostureResult>(new TimeoutException("Mock repair posture timed out.")),
            MockRepairPostureBehavior.ProviderUnavailable => Task.FromException<RepairPostureResult>(new ProviderUnavailableException(ProviderName, "Mock repair posture unavailable.")),
            _ => Task.FromResult(CreateResult(packet, packet.PacketId))
        };
    }

    private static RepairPostureResult CreateResult(
        RepairPosturePacket packet,
        string packetId,
        string? nodeIdOverride = null,
        RepairMode? modeOverride = null)
    {
        var suggestions = packet.Nodes.Select(node =>
        {
            var (mode, reason) = SuggestFor(node);
            return new RepairSuggestion(
                nodeIdOverride ?? node.NodeId,
                modeOverride ?? mode,
                reason,
                Math.Max(node.DownstreamDependencyCount, node.Status == RepairPostureNodeStatus.Preserved ? 0 : 1));
        }).ToArray();

        return new RepairPostureResult(
            packetId,
            suggestions,
            512,
            ProviderName,
            ModelName,
            $"mock-repair-{packet.PacketId}");
    }

    private static (RepairMode Mode, RepairReasonCode Reason) SuggestFor(RepairPostureNode node)
    {
        if (node.Status == RepairPostureNodeStatus.Blocked || node.HasAvailabilityOrHold)
        {
            return (RepairMode.BlockedReview, RepairReasonCode.AvailabilityOrHoldRisk);
        }

        if (node.UserConfirmationRequired || node.Status == RepairPostureNodeStatus.NeedsUser)
        {
            return (RepairMode.AskUser, RepairReasonCode.NeedsUserConfirmation);
        }

        if (node.NodeKind == RepairPostureNodeKind.SelectedCandidate || node.NodeKind == RepairPostureNodeKind.DeferredCandidate)
        {
            return (RepairMode.ReselectCandidate, RepairReasonCode.CandidateInvalidated);
        }

        if (node.NodeKind is RepairPostureNodeKind.Day or RepairPostureNodeKind.Slot || node.DownstreamDependencyCount > 0)
        {
            return (RepairMode.ReplanDay, RepairReasonCode.DownstreamDayImpact);
        }

        return (RepairMode.Keep, RepairReasonCode.NoRepairNeeded);
    }
}

public enum MockRepairPostureBehavior
{
    Accepted,
    PacketMismatch,
    NodeMismatch,
    UnsupportedMode,
    MalformedResult,
    ProviderTimeout,
    ProviderUnavailable
}
