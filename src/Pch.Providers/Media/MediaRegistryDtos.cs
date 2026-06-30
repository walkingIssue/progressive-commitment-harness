namespace Pch.Providers.Media;

public sealed record MediaRegistryPacket(
    string PacketId,
    IReadOnlyList<MediaRegistryCandidate> Candidates,
    string Locale,
    string? ContextDigest = null);

public sealed record MediaRegistryCandidate(
    string SlotId,
    string CandidateId,
    MediaCandidateCategory Category,
    string? Mood = null);

public sealed record MediaRegistryOptions(
    int AssetsPerCandidate = 1,
    IReadOnlyList<MediaSourceClass>? EnabledSources = null);

public sealed record MediaRegistryResult(
    string PacketId,
    IReadOnlyList<CandidateMediaMapping> CandidateMedia,
    int ResponseContentLength,
    string Provider,
    string Model,
    string? RequestId);

public sealed record CandidateMediaMapping(
    string SlotId,
    string CandidateId,
    MediaCandidateCategory Category,
    IReadOnlyList<MediaAsset> Assets);

public sealed record MediaAsset(
    string MediaId,
    MediaSource Source,
    MediaLicense License,
    MediaAttribution Attribution,
    int Width,
    int Height,
    string? ImageUrl = null,
    string? ThumbnailUrl = null,
    string? AltText = null,
    string? DominantColorToken = null);

public sealed record MediaSource(
    string SourceId,
    MediaSourceClass SourceClass,
    string ProviderName,
    string? SourceUrl = null);

public sealed record MediaLicense(
    MediaLicenseClass LicenseClass,
    string LicenseName,
    string? LicenseUrl,
    bool RequiresAttribution,
    bool AllowsCommercialUse);

public sealed record MediaAttribution(
    string? AuthorName,
    string? AuthorUrl,
    string? AttributionText);

public sealed record MediaRegistryEvalCase(
    string Name,
    MediaRegistryPacket Packet);

public sealed record SanitizedMediaRegistryEvalRow(
    string Name,
    string PacketId,
    bool Passed,
    string OutcomeCode,
    string? ErrorCode,
    IReadOnlyList<SanitizedCandidateMediaRow> Candidates,
    int CandidateCount,
    int TotalMediaAssetCount,
    int? ResponseContentLength,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed record SanitizedCandidateMediaRow(
    string SlotId,
    string CandidateId,
    MediaCandidateCategory Category,
    IReadOnlyList<SanitizedMediaAssetRow> Assets);

public sealed record SanitizedMediaAssetRow(
    string MediaId,
    MediaSourceClass SourceClass,
    string SourceId,
    string ProviderName,
    MediaLicenseClass LicenseClass,
    string LicenseName,
    string? AuthorName,
    string? AuthorUrl,
    string? AttributionText,
    int Width,
    int Height);

public enum MediaCandidateCategory
{
    Flight,
    Lodging,
    Activity,
    Dining,
    Transit,
    Downtime
}

public enum MediaSourceClass
{
    ProviderSupplied,
    Generated,
    Pexels,
    Unsplash,
    Openverse,
    Wikimedia,
    DeterministicPlaceholder
}

public enum MediaLicenseClass
{
    ProviderTerms,
    GeneratedInternal,
    FreeCommercial,
    CreativeCommons,
    PublicDomain,
    Unknown
}
