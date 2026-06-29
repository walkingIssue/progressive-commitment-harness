using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class PromptPacketBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void VacationPromptBuildsBoundedPacketWithoutRawPrompt()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var memory = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-vacation-memory",
            [
                new("/mission/purpose", "Cherry blossom vacation", AuthoritySource.User, ["evidence-user-purpose"]),
                new("/mission/destination_country", "Japan", AuthoritySource.StrongModelInference, ["evidence-model-country"])
            ],
            [],
            [])).Digest;
        const string rawPrompt = "RAW_VACATION_SENTINEL plan a vacation around cherry blossoms";

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            rawPrompt,
            memory,
            "en-US",
            ["vacation", "spring"]));

        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsAccepted);
        Assert.Equal("prompt_packet_built", result.Code);
        Assert.Equal("vacation", result.Prompt.Category);
        Assert.Equal(rawPrompt.Length, result.Prompt.Length);
        Assert.NotEmpty(result.Prompt.Sha256);
        Assert.Equal("en-US", result.Packet!.Locale);
        Assert.Contains("vacation", result.Packet.ScenarioHints);
        Assert.Contains(result.Packet.CurrentMissionFacts, fact => fact == "purpose: Cherry blossom vacation");
        Assert.Contains(result.Packet.PendingConfirmations, pending => pending.FieldPath == "/mission/destination_country");
        Assert.DoesNotContain(rawPrompt, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_VACATION_SENTINEL", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessPromptIncludesKnownConstraintsAndEvidenceReferences()
    {
        var session = SyntheticTripFactory.CreateBusinessTripSession();
        var memory = new StructuredMemoryDigest(
            "digest-business",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Business trip"],
            [],
            ["evidence-user-purpose"]);
        const string rawPrompt = "Business trip for a client workshop with tight arrival constraints.";

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            rawPrompt,
            memory,
            "en-GB",
            ["business", "client-workshop"]));

        Assert.True(result.IsAccepted);
        Assert.Equal("business", result.Prompt.Category);
        Assert.True(result.Packet!.KnownConstraints.Count <= 8);
        Assert.Contains(result.Packet.KnownConstraints, constraint => constraint.ConstraintId == "constraint-pace");
        Assert.Contains("evidence-user-purpose", result.Packet.EvidenceReferences);
        Assert.DoesNotContain(result.Packet.CurrentMissionFacts, fact => fact.Contains(rawPrompt, StringComparison.Ordinal));
    }

    [Fact]
    public void FamilySupportPromptUsesScenarioHintsWithoutPersistingPrompt()
    {
        var session = SyntheticTripFactory.CreateSession(14);
        const string rawPrompt = "Help family move apartment, keep mornings flexible.";

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            rawPrompt,
            null,
            "sv-SE",
            ["family-support", "family-support", "morning-flex"]));

        Assert.True(result.IsAccepted);
        Assert.Equal("family_support", result.Prompt.Category);
        Assert.Equal(["family-support", "morning-flex"], result.Packet!.ScenarioHints);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain(rawPrompt, JsonSerializer.Serialize(result, JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public void FuneralDowntimePromptCarriesPendingConfirmations()
    {
        var session = SyntheticTripFactory.CreateFuneralDowntimeSession();
        var pending = new MissionPendingConfirmation(
            "/constraints/pace",
            "very gentle",
            AuthoritySource.StrongModelInference,
            "requires_confirmation",
            ["evidence-model-pace"]);
        var memory = new StructuredMemoryDigest(
            "digest-funeral",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Funeral travel", "destination_country: Japan"],
            [pending],
            ["evidence-model-pace"]);

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            "Funeral trip with quiet downtime and gentle pacing.",
            memory,
            "en-US",
            ["downtime"]));

        Assert.True(result.IsAccepted);
        Assert.Equal("funeral_downtime", result.Prompt.Category);
        Assert.Contains(result.Packet!.PendingConfirmations, confirmation => confirmation.FieldPath == "/constraints/pace");
        Assert.Contains(result.Packet.CurrentMissionFacts, fact => fact == "purpose: Funeral travel");
    }

    [Fact]
    public void OverlongPromptRejectsWithFixedCodeAndNoMutationOrRawEcho()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;
        var rawPrompt = "RAW_OVERLONG_SENTINEL_" + new string('x', 4_001);

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            rawPrompt,
            null,
            "en-US",
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("prompt_too_long", result.Code);
        Assert.Equal("Prompt intake request exceeded length limits.", result.Summary);
        Assert.Null(result.Packet);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
        Assert.DoesNotContain("RAW_OVERLONG_SENTINEL", JsonSerializer.Serialize(result, JsonOptions), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlankPromptRejectsWithFixedCodeAndNoMutation(string? rawPrompt)
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var originalPurpose = session.Mission.Purpose;

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            rawPrompt!,
            null,
            "en-US",
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_prompt", result.Code);
        Assert.Equal("Prompt intake request failed validation.", result.Summary);
        Assert.Equal(originalPurpose, session.Mission.Purpose);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.DecisionLedger.Records);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void MissingSessionIdRejectsWithFixedCode()
    {
        var session = SyntheticTripFactory.CreateSession(7);

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            "",
            "Plan a vacation.",
            null,
            "en-US",
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_session", result.Code);
        Assert.Null(result.Packet);
    }

    [Fact]
    public void TooManyMemoryItemsRejectsWithoutRawSentinelLeak()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var sentinel = "RAW_MEMORY_SENTINEL_SHOULD_NOT_LEAK";
        var memory = new StructuredMemoryDigest(
            "digest-too-large",
            session.SessionId,
            session.Mission.MissionId,
            Enumerable.Range(1, 25).Select(index => index == 25 ? sentinel : $"fact {index}").ToArray(),
            [],
            []);

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            "Plan a vacation.",
            memory,
            "en-US",
            []));

        Assert.False(result.IsAccepted);
        Assert.Equal("too_many_memory_items", result.Code);
        Assert.Null(result.Packet);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(result, JsonOptions), StringComparison.Ordinal);
        Assert.Null(session.MemoryDigest);
        Assert.Empty(session.Actions);
    }

    [Fact]
    public void PacketBoundsFactsPendingConstraintsHintsAndEvidence()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var memory = new StructuredMemoryDigest(
            "digest-bounded",
            session.SessionId,
            session.Mission.MissionId,
            Enumerable.Range(1, 12).Select(index => $"fact {index}").ToArray(),
            Enumerable.Range(1, 8)
                .Select(index => new MissionPendingConfirmation(
                    $"/mission/pending-{index}",
                    $"value {index}",
                    AuthoritySource.StrongModelInference,
                    "requires_confirmation",
                    [$"evidence-pending-{index}"]))
                .ToArray(),
            Enumerable.Range(1, 12).Select(index => $"evidence-{index}").ToArray());

        var result = new PromptPacketBuilder().Build(session, new PromptIntakeRequest(
            session.SessionId,
            "Plan a holiday.",
            memory,
            "en-US",
            Enumerable.Range(1, 8).Select(index => $"hint-{index}").ToArray()));

        Assert.True(result.IsAccepted);
        Assert.True(result.Packet!.CurrentMissionFacts.Count <= 8);
        Assert.True(result.Packet.PendingConfirmations.Count <= 6);
        Assert.True(result.Packet.KnownConstraints.Count <= 8);
        Assert.True(result.Packet.ScenarioHints.Count <= 6);
        Assert.True(result.Packet.EvidenceReferences.Count <= 8);
    }
}
