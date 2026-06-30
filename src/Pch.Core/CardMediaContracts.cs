using System.Text.Json.Serialization;

namespace Pch.Core;

public sealed record CardMediaReference(
    string MediaId,
    CardMediaSourceClass SourceClass,
    CardMediaLicenseClass LicenseClass,
    string Uri,
    string AltText,
    string DominantColorToken,
    int Width,
    int Height,
    CardMediaAttribution Attribution,
    IReadOnlyList<string> EvidenceIds);

public sealed record CardMediaAttribution(
    string SourceName,
    string? AuthorName,
    string? SourceUri,
    string? LicenseName,
    string? LicenseUri);

public sealed record CardMediaManifest(
    string ManifestId,
    string Locale,
    IReadOnlyList<CardMediaManifestItem> Items);

public sealed record CardMediaManifestItem(
    string Mood,
    string CandidateKind,
    CardMediaReference Media);

[JsonConverter(typeof(JsonStringEnumConverter<CardMediaSourceClass>))]
public enum CardMediaSourceClass
{
    DeterministicPlaceholder,
    Generated,
    Stock,
    OpenLicense,
    ProviderSupplied
}

[JsonConverter(typeof(JsonStringEnumConverter<CardMediaLicenseClass>))]
public enum CardMediaLicenseClass
{
    InternalFixture,
    Generated,
    StockCommercial,
    OpenLicense,
    ProviderTerms
}
