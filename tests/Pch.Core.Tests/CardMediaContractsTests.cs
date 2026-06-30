using System.Text.Json;
using Pch.Core;
using Xunit;

namespace Pch.Core.Tests;

public sealed class CardMediaContractsTests
{
    [Fact]
    public void CardMediaReferenceKeepsProviderNeutralProvenance()
    {
        var media = new CardMediaReference(
            "media-1",
            CardMediaSourceClass.Generated,
            CardMediaLicenseClass.Generated,
            "local://card-media/japan/scenic",
            "Generated scenic backdrop.",
            "sea-glass",
            1200,
            800,
            new CardMediaAttribution(
                "Progressive Commitment Harness",
                "deterministic fixture",
                null,
                "Generated internal fixture",
                null),
            ["evidence-media"]);

        var serialized = JsonSerializer.Serialize(media, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("media-1", media.MediaId);
        Assert.Contains("Generated", serialized, StringComparison.Ordinal);
        Assert.Contains("sea-glass", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Pch.Providers", serialized, StringComparison.Ordinal);
    }
}
