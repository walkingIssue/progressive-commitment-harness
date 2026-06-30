using System.Text.Json;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class GoldenTurnTraceRunnerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public void DefaultScriptsProduceHappyAndBlockedGoldenTraces()
    {
        var results = new GoldenTurnTraceRunner().RunDefaultScripts();

        Assert.Equal(2, results.Count);
        var happy = Assert.Single(results, result => result.Scenario == GoldenTurnTraceRunner.HappyScenario);
        var blocked = Assert.Single(results, result => result.Scenario == GoldenTurnTraceRunner.BlockedScenario);
        Assert.True(happy.IsAccepted);
        Assert.False(happy.IsBlocked);
        Assert.Equal(GoldenTurnTraceRunner.TraceCompleteCode, happy.Code);
        Assert.False(blocked.IsAccepted);
        Assert.True(blocked.IsBlocked);
        Assert.Equal(GoldenTurnTraceRunner.TraceBlockedCode, blocked.Code);
        Assert.Contains(blocked.Turns, turn => turn.Kind == "blocked" && turn.Code == "approval_required_preview");
        Assert.Contains(happy.Turns, turn => turn.Kind == "evidence" && turn.Code == "complete");
    }

    [Fact]
    public void DefaultScriptsAreDeterministic()
    {
        var runner = new GoldenTurnTraceRunner();

        var first = JsonSerializer.Serialize(runner.RunDefaultScripts(), JsonOptions);
        var second = JsonSerializer.Serialize(runner.RunDefaultScripts(), JsonOptions);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DefaultTracesMatchGoldenFixtures()
    {
        var results = new GoldenTurnTraceRunner().RunDefaultScripts();

        AssertFixture(results.Single(result => result.Scenario == GoldenTurnTraceRunner.HappyScenario), "happy_trip_planning.json");
        AssertFixture(results.Single(result => result.Scenario == GoldenTurnTraceRunner.BlockedScenario), "blocked_safety.json");
    }

    [Fact]
    public void TracesCoverTranscriptTurnKindsAndStayBounded()
    {
        var results = new GoldenTurnTraceRunner().RunDefaultScripts();

        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "user");
        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "assistant");
        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "harness");
        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "decision");
        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "blocked");
        Assert.Contains(results.SelectMany(result => result.Turns), turn => turn.Kind == "evidence");
        Assert.All(results, result =>
        {
            Assert.True(result.Turns.Count <= 16);
            Assert.True(result.EvidenceReferences.Count <= 8);
            Assert.Equal(64, result.TranscriptHash.Length);
            Assert.All(result.Turns, turn =>
            {
                Assert.True(turn.Markers.Count <= 8);
                Assert.True(turn.EvidenceReferences.Count <= 8);
            });
        });
    }

    [Fact]
    public void SerializedTracesDoNotLeakRawOrCandidateDisplaySentinels()
    {
        var serialized = JsonSerializer.Serialize(new GoldenTurnTraceRunner().RunDefaultScripts(), JsonOptions);

        Assert.DoesNotContain("Plan a one day vacation", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CREDENTIAL_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("approved-golden-token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidScriptUsesFixedSanitizedFailure()
    {
        var result = new GoldenTurnTraceRunner().Run(null!);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(GoldenTurnTraceRunner.InvalidScriptCode, result.Code);
        Assert.Equal("Golden turn trace script failed validation.", result.Summary);
        Assert.Empty(result.Turns);
        Assert.DoesNotContain("Exception", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertFixture(GoldenTurnTraceResult result, string fileName)
    {
        var fixturePath = Path.Combine(FindRepoRoot(), "tests", "fixtures", "golden-turn-traces", fileName);
        var expected = File.ReadAllText(fixturePath).ReplaceLineEndings("\n").Trim();
        var actual = JsonSerializer.Serialize(result, JsonOptions).ReplaceLineEndings("\n").Trim();
        Assert.Equal(expected, actual);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "fixtures", "golden-turn-traces")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate golden-turn-traces fixtures.");
    }
}
