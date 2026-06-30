using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class CardMediaProvenanceBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public void ValidReferenceIsAcceptedWithBoundedProviderNeutralFields()
    {
        var result = new CardMediaProvenanceBoundary().Validate(new CardMediaReference(
            "media-valid",
            CardMediaSourceClass.OpenLicense,
            CardMediaLicenseClass.OpenLicense,
            "https://example.invalid/media-valid",
            "Quiet garden path in Japan.",
            "moss-mist",
            1200,
            800,
            new CardMediaAttribution(
                "Open archive",
                "Fixture author",
                "https://example.invalid/source",
                "CC BY fixture",
                "https://example.invalid/license"),
            ["evidence-valid"]));

        Assert.True(result.IsAccepted);
        Assert.Equal(CardMediaProvenanceBoundary.AcceptedCode, result.Code);
        Assert.Equal("moss-mist", result.Media!.DominantColorToken);
        Assert.Equal("evidence-valid", Assert.Single(result.Media.EvidenceIds));
    }

    [Fact]
    public void UnsafeTextIsRedactedWithoutEchoingSentinels()
    {
        var result = new CardMediaProvenanceBoundary().Validate(new CardMediaReference(
            "media-RAW_SECRET_SHOULD_NOT_LEAK",
            CardMediaSourceClass.ProviderSupplied,
            CardMediaLicenseClass.ProviderTerms,
            "https://example.invalid/RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
            "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
            "sea-glass",
            900,
            600,
            new CardMediaAttribution(
                "RAW_CREDENTIAL_SHOULD_NOT_LEAK",
                "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
                "https://example.invalid/RAW_PROMPT_SHOULD_NOT_LEAK",
                "Provider terms",
                null),
            ["evidence-safe", "RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK"]));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsAccepted);
        Assert.DoesNotContain("RAW_SECRET_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CREDENTIAL_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.Equal("local://card-media/redacted", result.Media!.Uri);
        Assert.Null(result.Media.Attribution.SourceUri);
        Assert.Contains("redacted", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DisallowedUriSchemesAreSanitizedBeforeUiRendering()
    {
        var result = new CardMediaProvenanceBoundary().Validate(Reference() with
        {
            Uri = "javascript:alert(1)",
            Attribution = Reference().Attribution with
            {
                SourceUri = "file:///C:/unsafe-media-source",
                LicenseUri = "data:text/plain,unsafe"
            }
        });

        Assert.True(result.IsAccepted);
        Assert.Equal("local://card-media/redacted", result.Media!.Uri);
        Assert.Null(result.Media.Attribution.SourceUri);
        Assert.Null(result.Media.Attribution.LicenseUri);
    }

    [Theory]
    [InlineData(0, 800, CardMediaProvenanceBoundary.InvalidDimensionsCode)]
    [InlineData(1200, 9000, CardMediaProvenanceBoundary.InvalidDimensionsCode)]
    public void InvalidDimensionsRejectWithFixedSanitizedCode(int width, int height, string expectedCode)
    {
        var result = new CardMediaProvenanceBoundary().Validate(Reference() with { Width = width, Height = height });

        Assert.False(result.IsAccepted);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(result.Media);
    }

    [Fact]
    public void InvalidDominantColorRejectsWithFixedCode()
    {
        var result = new CardMediaProvenanceBoundary().Validate(Reference() with { DominantColorToken = "not a token" });

        Assert.False(result.IsAccepted);
        Assert.Equal(CardMediaProvenanceBoundary.InvalidDominantColorCode, result.Code);
        Assert.Null(result.Media);
    }

    [Fact]
    public void JapanMoodManifestMatchesDeterministicFixture()
    {
        var boundary = new CardMediaProvenanceBoundary();
        var result = boundary.ValidateManifest(boundary.BuildJapanMoodMediaManifest());

        Assert.True(result.IsAccepted);
        Assert.Equal(8, result.Manifest!.Items.Count);
        Assert.Contains(result.Manifest.Items, item => item.Mood == "lively_food");
        Assert.Contains(result.Manifest.Items, item => item.Mood == "logistics_transit");
        AssertFixture(result.Manifest, "japan-mood-media-manifest.json");
    }

    [Fact]
    public void NullManifestRejectsWithFixedCode()
    {
        var result = new CardMediaProvenanceBoundary().ValidateManifest(null);

        Assert.False(result.IsAccepted);
        Assert.Equal(CardMediaProvenanceBoundary.InvalidReferenceCode, result.Code);
        Assert.Null(result.Manifest);
    }

    private static CardMediaReference Reference()
    {
        return new CardMediaReference(
            "media-valid",
            CardMediaSourceClass.Generated,
            CardMediaLicenseClass.Generated,
            "local://card-media/japan/scenic",
            "Generated scenic backdrop.",
            "sea-glass",
            1200,
            800,
            new CardMediaAttribution("Harness", "fixture", null, "Generated", null),
            ["evidence-media"]);
    }

    private static void AssertFixture(CardMediaManifest manifest, string fileName)
    {
        var fixturePath = Path.Combine(FindRepoRoot(), "tests", "fixtures", "card-media", fileName);
        var expected = File.ReadAllText(fixturePath).ReplaceLineEndings("\n").Trim();
        var actual = JsonSerializer.Serialize(manifest, JsonOptions).ReplaceLineEndings("\n").Trim();
        Assert.Equal(expected, actual);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "fixtures", "card-media")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate card-media fixtures.");
    }
}
