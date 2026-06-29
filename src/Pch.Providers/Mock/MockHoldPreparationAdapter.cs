using Pch.Providers.Errors;
using Pch.Providers.HoldPreparation;

namespace Pch.Providers.Mock;

public sealed class MockHoldPreparationAdapter : IHoldPreparationAdapter
{
    public const string ProviderName = "mock-hold-preparation";

    private readonly MockHoldPreparationBehavior _behavior;

    public MockHoldPreparationAdapter(MockHoldPreparationBehavior behavior = MockHoldPreparationBehavior.Normal)
    {
        _behavior = behavior;
    }

    public Task<HoldPreparationResult> PrepareAsync(
        HoldPreparationPacket packet,
        HoldPreparationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockHoldPreparationBehavior.PacketMismatch => Task.FromResult(CreateResult(
                "RAW_PACKET_ID_SHOULD_NOT_PERSIST",
                HoldPreparationResultKind.PreviewReady,
                packet,
                options,
                HoldPreparationCandidateStatus.PreviewAvailable)),
            MockHoldPreparationBehavior.ProviderUnavailable => Task.FromException<HoldPreparationResult>(
                new ProviderUnavailableException(ProviderName, "Mock hold preparation provider unavailable.")),
            MockHoldPreparationBehavior.UnsupportedResult => Task.FromResult(CreateResult(
                packet.PacketId,
                HoldPreparationResultKind.Unsupported,
                packet,
                options,
                HoldPreparationCandidateStatus.Unsupported)),
            _ => Task.FromResult(PrepareNormal(packet, options))
        };
    }

    private static HoldPreparationResult PrepareNormal(
        HoldPreparationPacket packet,
        HoldPreparationOptions? options)
    {
        if (packet.Operation == HoldPreparationOperation.Preview)
        {
            return CreateResult(
                packet.PacketId,
                HoldPreparationResultKind.PreviewReady,
                packet,
                options,
                HoldPreparationCandidateStatus.PreviewAvailable);
        }

        var requiredApprovalToken = options?.RequiredApprovalToken ?? "mock-approval-token";
        if (string.IsNullOrWhiteSpace(packet.ApprovalToken))
        {
            return CreateResult(
                packet.PacketId,
                HoldPreparationResultKind.ApprovalMissing,
                packet,
                options,
                HoldPreparationCandidateStatus.Unsupported);
        }

        if (!string.Equals(packet.ApprovalToken, requiredApprovalToken, StringComparison.Ordinal))
        {
            return CreateResult(
                packet.PacketId,
                HoldPreparationResultKind.ApprovalRejected,
                packet,
                options,
                HoldPreparationCandidateStatus.Unsupported);
        }

        return CreateResult(
            packet.PacketId,
            HoldPreparationResultKind.HoldPrepared,
            packet,
            options,
            HoldPreparationCandidateStatus.HoldPrepared);
    }

    private static HoldPreparationResult CreateResult(
        string packetId,
        HoldPreparationResultKind kind,
        HoldPreparationPacket packet,
        HoldPreparationOptions? options,
        HoldPreparationCandidateStatus status)
    {
        var candidates = packet.SelectedCandidates
            .Select(candidate => new HoldPreparationCandidateResult(
                candidate.SlotId,
                candidate.CandidateId,
                candidate.Category,
                status,
                status == HoldPreparationCandidateStatus.HoldPrepared
                    ? $"mock-hold-{candidate.SlotId}-{candidate.CandidateId}"
                    : null,
                status == HoldPreparationCandidateStatus.HoldPrepared
                    ? new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero)
                    : null))
            .ToArray();

        return new HoldPreparationResult(
            packetId,
            kind,
            candidates,
            ResponseContentLength: 0,
            ProviderName,
            options?.Model ?? "mock-hold-preparation-deterministic",
            $"mock-hold-prep-{packet.PacketId}");
    }
}

public enum MockHoldPreparationBehavior
{
    Normal,
    PacketMismatch,
    ProviderUnavailable,
    UnsupportedResult
}
