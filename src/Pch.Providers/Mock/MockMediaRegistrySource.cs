using Pch.Providers.Errors;
using Pch.Providers.Media;

namespace Pch.Providers.Mock;

public sealed class MockMediaRegistrySource : IMediaRegistrySource
{
    public const string ProviderName = "mock-media-registry";
    public const string ModelName = "mock-media-registry-deterministic";

    private readonly MockMediaRegistryBehavior _behavior;

    public MockMediaRegistrySource(MockMediaRegistryBehavior behavior = MockMediaRegistryBehavior.Accepted)
    {
        _behavior = behavior;
    }

    public Task<MediaRegistryResult> ResolveAsync(
        MediaRegistryPacket packet,
        MediaRegistryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _behavior switch
        {
            MockMediaRegistryBehavior.PacketMismatch => Task.FromResult(CreateResult(packet, "RAW_PACKET_ID_SHOULD_NOT_PERSIST")),
            MockMediaRegistryBehavior.CandidateMismatch => Task.FromResult(CreateResult(packet, packet.PacketId, candidateIdOverride: "RAW_CANDIDATE_ID_SHOULD_NOT_PERSIST")),
            MockMediaRegistryBehavior.UnsupportedSource => Task.FromResult(CreateResult(packet, packet.PacketId, sourceOverride: (MediaSourceClass)999)),
            MockMediaRegistryBehavior.UnsupportedLicense => Task.FromResult(CreateResult(packet, packet.PacketId, licenseOverride: MediaLicenseClass.Unknown)),
            MockMediaRegistryBehavior.MalformedResult => Task.FromResult(CreateResult(packet, packet.PacketId, widthOverride: 0)),
            MockMediaRegistryBehavior.ProviderTimeout => Task.FromException<MediaRegistryResult>(new TimeoutException("Mock media provider timed out.")),
            MockMediaRegistryBehavior.ProviderUnavailable => Task.FromException<MediaRegistryResult>(new ProviderUnavailableException(ProviderName, "Mock media provider unavailable.")),
            _ => Task.FromResult(CreateResult(packet, packet.PacketId))
        };
    }

    private static MediaRegistryResult CreateResult(
        MediaRegistryPacket packet,
        string packetId,
        string? candidateIdOverride = null,
        MediaSourceClass? sourceOverride = null,
        MediaLicenseClass? licenseOverride = null,
        int? widthOverride = null)
    {
        var mappings = packet.Candidates.Select((candidate, index) =>
        {
            var sourceClass = sourceOverride ?? SourceClassFor(candidate.Category);
            var licenseClass = licenseOverride ?? LicenseClassFor(sourceClass);
            return new CandidateMediaMapping(
                candidate.SlotId,
                candidateIdOverride ?? candidate.CandidateId,
                candidate.Category,
                [
                    new MediaAsset(
                        $"media-{candidate.CandidateId}",
                        new MediaSource(
                            $"source-{sourceClass.ToString().ToLowerInvariant()}",
                            sourceClass,
                            ProviderName,
                            SourceUrl: "https://provider.example/source/RAW_SOURCE_URL_SHOULD_NOT_PERSIST"),
                        new MediaLicense(
                            licenseClass,
                            LicenseNameFor(licenseClass),
                            LicenseUrl: "https://provider.example/license",
                            RequiresAttribution: licenseClass is not MediaLicenseClass.GeneratedInternal,
                            AllowsCommercialUse: true),
                        new MediaAttribution(
                            $"Mock Author {index + 1}",
                            "https://provider.example/author",
                            $"Mock attribution {index + 1}"),
                        widthOverride ?? 1200,
                        800,
                        ImageUrl: "https://images.example/RAW_IMAGE_URL_SHOULD_NOT_PERSIST.jpg",
                        ThumbnailUrl: "https://images.example/RAW_THUMB_URL_SHOULD_NOT_PERSIST.jpg",
                        AltText: "RAW_ALT_TEXT_SHOULD_NOT_PERSIST",
                        DominantColorToken: "soft_nature")
                ]);
        }).ToArray();

        return new MediaRegistryResult(
            packetId,
            mappings,
            768,
            ProviderName,
            ModelName,
            $"mock-media-{packet.PacketId}");
    }

    private static MediaSourceClass SourceClassFor(MediaCandidateCategory category) =>
        category switch
        {
            MediaCandidateCategory.Lodging => MediaSourceClass.ProviderSupplied,
            MediaCandidateCategory.Dining => MediaSourceClass.Pexels,
            MediaCandidateCategory.Activity => MediaSourceClass.Openverse,
            MediaCandidateCategory.Transit => MediaSourceClass.Wikimedia,
            MediaCandidateCategory.Flight => MediaSourceClass.Generated,
            _ => MediaSourceClass.DeterministicPlaceholder
        };

    private static MediaLicenseClass LicenseClassFor(MediaSourceClass sourceClass) =>
        sourceClass switch
        {
            MediaSourceClass.ProviderSupplied => MediaLicenseClass.ProviderTerms,
            MediaSourceClass.Generated => MediaLicenseClass.GeneratedInternal,
            MediaSourceClass.Pexels or MediaSourceClass.Unsplash => MediaLicenseClass.FreeCommercial,
            MediaSourceClass.Openverse or MediaSourceClass.Wikimedia => MediaLicenseClass.CreativeCommons,
            _ => MediaLicenseClass.GeneratedInternal
        };

    private static string LicenseNameFor(MediaLicenseClass licenseClass) =>
        licenseClass switch
        {
            MediaLicenseClass.ProviderTerms => "Provider content terms",
            MediaLicenseClass.GeneratedInternal => "Generated internal-use asset",
            MediaLicenseClass.FreeCommercial => "Free commercial media license",
            MediaLicenseClass.CreativeCommons => "Creative Commons license",
            MediaLicenseClass.PublicDomain => "Public domain",
            _ => "Unknown license"
        };
}

public enum MockMediaRegistryBehavior
{
    Accepted,
    PacketMismatch,
    CandidateMismatch,
    UnsupportedSource,
    UnsupportedLicense,
    MalformedResult,
    ProviderTimeout,
    ProviderUnavailable
}
