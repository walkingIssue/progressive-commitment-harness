using Pch.Providers.Errors;
using Pch.Providers.EvidenceExport;

namespace Pch.Providers.Mock;

public sealed class MockEvidenceExportProvider : IEvidenceExportProvider
{
    public const string ProviderName = "mock-evidence-export";

    private readonly MockEvidenceExportBehavior _behavior;

    public MockEvidenceExportProvider(MockEvidenceExportBehavior behavior = MockEvidenceExportBehavior.Normal)
    {
        _behavior = behavior;
    }

    public Task<EvidenceExportResult> ExportAsync(
        EvidenceExportPacket packet,
        EvidenceExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockEvidenceExportBehavior.PacketMismatch => Task.FromResult(CreateResult(
                packet,
                options,
                EvidenceExportResultKind.ExportReady,
                packetId: "RAW_PACKET_ID_SHOULD_NOT_PERSIST")),
            MockEvidenceExportBehavior.ResultMismatch => Task.FromResult(CreateResult(
                packet,
                options,
                EvidenceExportResultKind.ExportReady,
                export: CreateExport(packet) with
                {
                    PlanId = "RAW_PLAN_ID_SHOULD_NOT_PERSIST",
                    EvidenceIds = ["RAW_EVIDENCE_ID_SHOULD_NOT_PERSIST"]
                })),
            MockEvidenceExportBehavior.Unsupported => Task.FromResult(CreateResult(
                packet,
                options,
                EvidenceExportResultKind.Unsupported)),
            MockEvidenceExportBehavior.Malformed => Task.FromResult(CreateResult(
                packet,
                options,
                EvidenceExportResultKind.Malformed,
                export: CreateExport(packet) with
                {
                    CandidateIds = ["RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST"]
                })),
            MockEvidenceExportBehavior.ProviderUnavailable => Task.FromException<EvidenceExportResult>(
                new ProviderUnavailableException(ProviderName, "Mock evidence export provider unavailable.")),
            _ => Task.FromResult(CreateResult(packet, options, EvidenceExportResultKind.ExportReady))
        };
    }

    private static EvidenceExportResult CreateResult(
        EvidenceExportPacket packet,
        EvidenceExportOptions? options,
        EvidenceExportResultKind kind,
        string? packetId = null,
        TripPlanEvidenceExport? export = null) =>
        new(
            packetId ?? packet.PacketId,
            kind,
            export ?? CreateExport(packet),
            ResponseContentLength: 0,
            ProviderName,
            options?.Model ?? "mock-evidence-export-deterministic",
            $"mock-evidence-{packet.PacketId}");

    private static TripPlanEvidenceExport CreateExport(EvidenceExportPacket packet) =>
        new(
            packet.Summary.PlanId,
            packet.Summary.DayCount,
            packet.Summary.SelectedCandidateCount,
            packet.Summary.DeferredCandidateCount,
            packet.Summary.PreparedHoldCount,
            packet.Evidence.Select(evidence => evidence.EvidenceId).Order(StringComparer.Ordinal).ToArray(),
            packet.HoldOutcomes.Select(hold => hold.SlotId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            packet.HoldOutcomes.Select(hold => hold.CandidateId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
}

public enum MockEvidenceExportBehavior
{
    Normal,
    PacketMismatch,
    ResultMismatch,
    Unsupported,
    Malformed,
    ProviderUnavailable
}
