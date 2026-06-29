namespace Pch.Providers.EvidenceExport;

public interface IEvidenceExportProvider
{
    Task<EvidenceExportResult> ExportAsync(
        EvidenceExportPacket packet,
        EvidenceExportOptions? options = null,
        CancellationToken cancellationToken = default);
}
