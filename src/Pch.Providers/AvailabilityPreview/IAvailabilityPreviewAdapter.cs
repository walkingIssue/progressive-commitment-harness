namespace Pch.Providers.AvailabilityPreview;

public interface IAvailabilityPreviewAdapter
{
    Task<AvailabilityPreviewResult> PreviewAsync(
        AvailabilityPreviewPacket packet,
        AvailabilityPreviewOptions? options = null,
        CancellationToken cancellationToken = default);
}
