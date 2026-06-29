using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class ItinerarySlotCompilerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly FixedDate = new(2027, 4, 2);
    private static readonly DateTimeOffset FixedAt = new(2027, 4, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VacationDayCompilesCoreSlotTypes()
    {
        var session = SyntheticTripFactory.CreateSession(1);

        var result = new ItinerarySlotCompiler().Compile(session, Request(session));

        Assert.True(result.IsCompiled);
        Assert.Equal("itinerary_slots_compiled", result.Code);
        Assert.Single(result.Days);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Sleep);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Meal);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Transit);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Activity);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Downtime);
        Assert.Equal(result, session.LastItineraryCompilation);
    }

    [Fact]
    public void FamilySupportDayAddsUnresolvedConfirmationSlots()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var memory = new StructuredMemoryDigest(
            "digest-family",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Help family move"],
            [
                new("/mission/family_anchor", "moving help", AuthoritySource.StrongModelInference, "requires_confirmation", ["evidence-family"])
            ],
            ["evidence-family"]);

        var result = new ItinerarySlotCompiler().Compile(session, Request(session, memory));

        Assert.True(result.IsCompiled);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.UnresolvedConfirmation
            && slot.PendingFieldPath == "/mission/family_anchor");
    }

    [Fact]
    public void BusinessDayIncludesFixedCommitmentSlot()
    {
        var session = SyntheticTripFactory.CreateBusinessTripSession();
        AddCommitments(session, new Commitment(
            "commitment-client-workshop",
            CommitmentKind.FixedAnchor,
            "Client workshop",
            FixedAt,
            FixedAt.AddHours(2),
            "Tokyo office",
            false,
            false));

        var result = new ItinerarySlotCompiler().Compile(session, Request(session, startDate: FixedDate, endDate: FixedDate));

        Assert.True(result.IsCompiled);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.FixedCommitment
            && slot.CommitmentId == "commitment-client-workshop");
    }

    [Fact]
    public void FuneralDowntimeDayPreservesDowntimeAndPendingConfirmation()
    {
        var session = SyntheticTripFactory.CreateFuneralDowntimeSession();
        var memory = new StructuredMemoryDigest(
            "digest-funeral",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Funeral travel"],
            [
                new("/constraints/pace", "very gentle", AuthoritySource.StrongModelInference, "requires_confirmation", ["evidence-pace"])
            ],
            ["evidence-pace"]);

        var result = new ItinerarySlotCompiler().Compile(session, Request(session, memory));

        Assert.True(result.IsCompiled);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.Downtime);
        Assert.Contains(result.Days[0].Slots, slot => slot.Kind == ItinerarySlotKind.UnresolvedConfirmation);
    }

    [Fact]
    public void FixedCommitmentConflictsBlockWithSanitizedResult()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        AddCommitments(
            session,
            new Commitment("commitment-a", CommitmentKind.FixedAnchor, "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", FixedAt, FixedAt.AddHours(2), null, false, false),
            new Commitment("commitment-b", CommitmentKind.FixedAnchor, "Overlapping meeting", FixedAt.AddHours(1), FixedAt.AddHours(3), null, false, false));

        var result = new ItinerarySlotCompiler().Compile(session, Request(session, startDate: FixedDate, endDate: FixedDate));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.False(result.IsCompiled);
        Assert.Equal("fixed_commitment_conflict", result.Code);
        Assert.Equal("Itinerary slot compilation found conflicting fixed commitments.", result.Summary);
        Assert.Single(result.Conflicts);
        Assert.Equal(1, result.ConflictCount);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDateWindowBlocksWithoutMutation()
    {
        var session = CreateSessionWithDateWindow(default, new DateOnly(2027, 4, 3));

        var result = new ItinerarySlotCompiler().Compile(session, Request(session));

        Assert.False(result.IsCompiled);
        Assert.Equal("missing_date_window", result.Code);
        Assert.Equal("Itinerary slot compilation requires a date window.", result.Summary);
        Assert.Null(session.LastItineraryCompilation);
        Assert.Empty(session.Actions);
        Assert.Empty(session.DecisionLedger.Records);
    }

    [Fact]
    public void InvalidDateWindowBlocksWithFixedCode()
    {
        var session = CreateSessionWithDateWindow(new DateOnly(2027, 4, 3), new DateOnly(2027, 4, 1));

        var result = new ItinerarySlotCompiler().Compile(session, Request(session));

        Assert.False(result.IsCompiled);
        Assert.Equal("invalid_date_window", result.Code);
        Assert.Equal("Itinerary slot compilation requires a valid date window.", result.Summary);
        Assert.Null(session.LastItineraryCompilation);
    }

    [Fact]
    public void ProjectionIncludesBoundedSlotAndConflictCounts()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var compile = new ItinerarySlotCompiler().Compile(session, Request(session));

        var packet = new ProjectionService().Project(session, HarnessStage.DaySkeletonGeneration);

        Assert.True(compile.IsCompiled);
        Assert.Contains("itinerary_day_count: 1", packet.LoadBearingFacts);
        Assert.Contains($"itinerary_slot_count: {compile.SlotCount}", packet.LoadBearingFacts);
        Assert.Contains("itinerary_conflict_count: 0", packet.LoadBearingFacts);
        Assert.True(packet.LoadBearingFacts.Count <= 12);
    }

    [Fact]
    public void SerializedResultDoesNotContainRawPromptOrProviderPayloadSentinels()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var memory = new StructuredMemoryDigest(
            "digest-safe",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Vacation"],
            [
                new("/mission/raw_prompt", "RAW_PROMPT_SHOULD_NOT_LEAK", AuthoritySource.SmallModelDraft, "requires_confirmation", ["evidence-safe"])
            ],
            ["evidence-safe"]);

        var result = new ItinerarySlotCompiler().Compile(session, Request(session, memory, ["RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK"]));
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        Assert.True(result.IsCompiled);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    private static ItineraryCompilationRequest Request(
        TripSession session,
        StructuredMemoryDigest? memory = null,
        IReadOnlyList<string>? hints = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        return new(
            session.SessionId,
            startDate,
            endDate,
            memory,
            hints ?? []);
    }

    private static void AddCommitments(TripSession session, params Commitment[] commitments)
    {
        session.ReplaceMission(session.Mission with
        {
            Commitments = [.. session.Mission.Commitments, .. commitments]
        });
    }

    private static TripSession CreateSessionWithDateWindow(DateOnly startDate, DateOnly endDate)
    {
        var mission = new TripMission(
            MissionId: "mission-date-window-test",
            Purpose: "Date window test",
            DestinationCountry: "Japan",
            StartDate: startDate,
            EndDate: endDate,
            Travelers:
            [
                new("traveler-1", "Primary traveler", "ARN", [])
            ],
            Constraints: [],
            Commitments: []);

        return new TripSession("session-date-window-test", mission);
    }
}
