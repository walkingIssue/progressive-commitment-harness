using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class GoldenPacketCorpusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CorpusContainsRequiredEarlyFeasibilityPackets()
    {
        var corpus = new GoldenPacketCorpus().Create();
        var expected = new[]
        {
            "approval_request_packet",
            "business_trip_packet",
            "choice_collapse_packet",
            "conflict_review_packet",
            "funeral_downtime_packet",
            "slot_collection_packet"
        };

        Assert.Equal(expected, corpus.Keys);
        Assert.All(corpus.Values, packet =>
        {
            Assert.NotEmpty(packet.PacketId);
            Assert.NotEmpty(packet.AllowedActions);
            Assert.NotEmpty(packet.TraceRequirements);
            Assert.True(packet.LoadBearingFacts.Count <= 8);
            Assert.True(packet.Candidates.Count <= 6);
        });
    }

    [Fact]
    public void FixtureManifestMatchesGeneratedCorpusNames()
    {
        var manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "fixtures",
            "golden-packets",
            "manifest.json");

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(manifestPath)));
        var manifestNames = manifest.RootElement
            .GetProperty("packets")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();

        Assert.Equal(new GoldenPacketCorpus().Create().Keys, manifestNames);
        Assert.True(manifest.RootElement.GetProperty("provider_free").GetBoolean());
    }

    [Fact]
    public void FixturePacketsExistAndDeserializeAsStagePackets()
    {
        var fixtureDirectory = GetFixtureDirectory();
        var expectedStages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["approval_request_packet"] = "ApprovalQueue",
            ["business_trip_packet"] = "Logistics",
            ["choice_collapse_packet"] = "Logistics",
            ["conflict_review_packet"] = "ConflictVerify",
            ["funeral_downtime_packet"] = "ActivitiesDowntime",
            ["slot_collection_packet"] = "SlotCollection"
        };

        foreach (var name in new GoldenPacketCorpus().Create().Keys)
        {
            var path = Path.Combine(fixtureDirectory, $"{name}.json");
            Assert.True(File.Exists(path), $"Missing fixture {path}");

            var packet = JsonSerializer.Deserialize<StagePacket>(File.ReadAllText(path), JsonOptions);

            Assert.NotNull(packet);
            Assert.Equal(expectedStages[name], packet.Stage);
            Assert.NotEmpty(packet.AllowedActions);
            Assert.DoesNotContain(packet.LoadBearingFacts, fact => fact.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string GetFixtureDirectory()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "fixtures",
            "golden-packets"));
    }
}
