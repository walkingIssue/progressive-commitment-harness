using System.Text.Json;
using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class PlannerToolManifestCompilerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void NullOrMalformedManifestInputBlocks()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var validator = new PlannerPrimitiveValidator();

        var nullManifest = validator.Validate(session, null, null);
        var malformedManifest = validator.Validate(session, new PlannerToolManifest(
            "",
            0,
            "",
            session.SessionId,
            session.Stage.ToString(),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            0,
            0,
            false,
            false), null);

        AssertBlocked(nullManifest, PlannerPrimitiveValidator.InvalidManifestCode);
        AssertBlocked(malformedManifest, PlannerPrimitiveValidator.InvalidManifestCode);
    }

    [Fact]
    public void AllowedPrimitiveAccepted()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session);
        var result = Validate(session, manifest, [AssistantMessage(manifest)]);

        Assert.True(result.IsAccepted);
        Assert.False(result.IsBlocked);
        Assert.Equal(PlannerPrimitiveValidator.AcceptedCode, result.Code);
        Assert.Single(result.View.Primitives);
        Assert.Equal(PlannerPrimitiveIds.AssistantMessage, result.View.Primitives[0].PrimitiveId);
    }

    [Fact]
    public void UnsupportedPrimitiveBlocks()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session);
        var unsupported = AssistantMessage(manifest) with
        {
            PrimitiveId = "unsupported_primitive",
            RendererKey = "unsupported-renderer"
        };

        var result = Validate(session, manifest, [unsupported]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveNotSupportedCode);
    }

    [Fact]
    public void StageDisallowedPrimitiveBlocks()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var primitive = new PlannerPrimitiveInstance(
            "candidate-deck-1",
            PlannerPrimitiveIds.CandidateDeck,
            manifest.SchemaVersion,
            "candidate-deck",
            "Choose an activity",
            "Pick one trusted candidate.",
            null,
            null,
            null,
            null,
            PlannerMoodTokens.Neutral,
            null,
            new PlannerAnswerSchema(PlannerAnswerSchemaKinds.SingleSelect, true, null, 1, ["selected"]),
            new Dictionary<string, string?> { ["selected"] = "candidate-03" },
            [],
            []);

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveNotAllowedForStageCode);
    }

    [Fact]
    public void CompositeFormAccepted()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);

        var result = Validate(session, manifest,
        [
            TextInput(manifest, "purpose-input", "/mission/purpose", "quiet food trip"),
            DateRange(manifest, "dates-input")
        ]);

        Assert.True(result.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.AwaitingUserInputCode, result.Code);
        Assert.Contains(manifest.CompositeForms, form => form.FormId == "form-trip-basics-v1");
        Assert.Equal(2, result.View.Primitives.Count);
    }

    [Fact]
    public void InvalidAnswerSchemaBlocks()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var primitive = TextInput(manifest, "purpose-input", "/mission/purpose", "quiet food trip") with
        {
            Answers = new Dictionary<string, string?>()
        };

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.AnswerSchemaInvalidCode);
    }

    [Fact]
    public void StaleGraphRevisionBlocks()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session);
        var proposal = Proposal(session, manifest, [AssistantMessage(manifest)]) with
        {
            GraphRevision = "stale-revision"
        };

        var result = new PlannerPrimitiveValidator().Validate(session, manifest, proposal);

        AssertBlocked(result, PlannerPrimitiveValidator.StaleGraphRevisionCode);
    }

    [Fact]
    public void ToolSearchRequestAcceptedWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var before = Counts(session);
        var manifest = new PlannerToolManifestCompiler().Compile(session);

        var result = Validate(session, manifest, [ToolRequest(manifest, PlannerPrimitiveIds.ToolSearchRequest, "tool-search-request")]);

        Assert.True(result.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.ToolSearchRequestedCode, result.Code);
        Assert.Equal(before, Counts(session));
    }

    [Fact]
    public void ToolGapRequestAcceptedAsReviewRequiredWithoutMutation()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var before = Counts(session);
        var manifest = new PlannerToolManifestCompiler().Compile(session);

        var result = Validate(session, manifest, [ToolRequest(manifest, PlannerPrimitiveIds.ToolGapRequest, "tool-gap-request")]);

        Assert.True(result.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.ToolGapReviewRequiredCode, result.Code);
        Assert.Equal(before, Counts(session));
    }

    [Fact]
    public void UnsafePromptProviderCredentialMediaCssSentinelValuesBlockAndDoNotSerialize()
    {
        var session = PreparedCandidateSession();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.DaySkeletonGeneration);
        var slot = ActivitySlot(session);
        var unsafePrimitive = CandidateDeck(manifest, "candidate-03") with
        {
            SlotId = slot.SlotId,
            Prompt = "RAW_PROMPT_SHOULD_NOT_LEAK",
            MediaToken = "https://RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK/style.css"
        };

        var result = Validate(session, manifest, [unsafePrimitive]);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveMetadataRedactedCode);
        Assert.DoesNotContain("RAW_PROMPT_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("style.css", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DeterministicTwoTurnFakeModelFixtureCompilesToValidatedTurnViews()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var compiler = new PlannerToolManifestCompiler();
        var validator = new PlannerPrimitiveValidator();
        var firstManifest = compiler.Compile(session, HarnessStage.Intake);
        var first = validator.Validate(session, firstManifest, Proposal(session, firstManifest,
        [
            TextInput(firstManifest, "purpose-input", "/mission/purpose", "quiet food trip"),
            DateRange(firstManifest, "dates-input")
        ]));
        Assert.True(first.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.AwaitingUserInputCode, first.Code);

        CompileAndAssociate(session);
        var secondManifest = compiler.Compile(session, HarnessStage.DaySkeletonGeneration);
        var slot = ActivitySlot(session);
        var second = validator.Validate(session, secondManifest, Proposal(session, secondManifest,
        [
            CandidateDeck(secondManifest, "candidate-03") with
            {
                SlotId = slot.SlotId,
                MediaToken = "media:neutral"
            }
        ]));

        Assert.True(second.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.AwaitingUserInputCode, second.Code);
        Assert.Equal("validated-turn-fake-model-turn", second.View.TurnId);
        Assert.Equal(PlannerPrimitiveIds.CandidateDeck, Assert.Single(second.View.Primitives).PrimitiveId);
    }

    private static PlannerPrimitiveValidationResult Validate(
        TripSession session,
        PlannerToolManifest manifest,
        IReadOnlyList<PlannerPrimitiveInstance> primitives)
    {
        return new PlannerPrimitiveValidator().Validate(session, manifest, Proposal(session, manifest, primitives));
    }

    private static PlannerPrimitiveTurnProposal Proposal(
        TripSession session,
        PlannerToolManifest manifest,
        IReadOnlyList<PlannerPrimitiveInstance> primitives)
    {
        return new(
            "fake-model-turn",
            manifest.ManifestId,
            manifest.SchemaVersion,
            manifest.GraphRevision,
            session.SessionId,
            manifest.Stage,
            primitives);
    }

    private static PlannerPrimitiveInstance AssistantMessage(PlannerToolManifest manifest)
    {
        var definition = Definition(manifest, PlannerPrimitiveIds.AssistantMessage);
        return Instance(
            definition,
            "assistant-message-1",
            label: "Planning update",
            prompt: "I can help with the next planning step.",
            answers: new Dictionary<string, string?>());
    }

    private static PlannerPrimitiveInstance TextInput(
        PlannerToolManifest manifest,
        string instanceId,
        string fieldPath,
        string value)
    {
        var definition = Definition(manifest, PlannerPrimitiveIds.TextInput);
        return Instance(
            definition,
            instanceId,
            label: "Trip purpose",
            prompt: "What should this trip optimize for?",
            fieldPath: fieldPath,
            answers: new Dictionary<string, string?> { ["value"] = value });
    }

    private static PlannerPrimitiveInstance DateRange(PlannerToolManifest manifest, string instanceId)
    {
        var definition = Definition(manifest, PlannerPrimitiveIds.DateRange);
        return Instance(
            definition,
            instanceId,
            label: "Trip dates",
            prompt: "Confirm the date window.",
            fieldPath: "/mission/start_date",
            answers: new Dictionary<string, string?>
            {
                ["start"] = "2027-04-01",
                ["end"] = "2027-04-03"
            });
    }

    private static PlannerPrimitiveInstance ToolRequest(PlannerToolManifest manifest, string primitiveId, string rendererKey)
    {
        var definition = Definition(manifest, primitiveId);
        return Instance(
            definition,
            $"{primitiveId}-1",
            label: "Need a planning tool",
            prompt: "Request review for a missing tool.",
            answers: new Dictionary<string, string?> { ["value"] = "need_review" });
    }

    private static PlannerPrimitiveInstance CandidateDeck(PlannerToolManifest manifest, string candidateId)
    {
        var definition = Definition(manifest, PlannerPrimitiveIds.CandidateDeck);
        return Instance(
            definition,
            "candidate-deck-1",
            label: "Choose an activity",
            prompt: "Pick one trusted candidate.",
            candidateId: candidateId,
            moodToken: PlannerMoodTokens.Neutral,
            mediaToken: null,
            answers: new Dictionary<string, string?> { ["selected"] = candidateId },
            evidence: ["evidence-fixture-candidates"]);
    }

    private static PlannerPrimitiveInstance Instance(
        PlannerPrimitiveDefinition definition,
        string instanceId,
        string? label = null,
        string? prompt = null,
        string? fieldPath = null,
        string? slotId = null,
        string? candidateId = null,
        string? taskId = null,
        string? moodToken = null,
        string? mediaToken = null,
        IReadOnlyDictionary<string, string?>? answers = null,
        IReadOnlyList<string>? evidence = null)
    {
        return new(
            instanceId,
            definition.PrimitiveId,
            definition.SchemaVersion,
            definition.RendererKey,
            label,
            prompt,
            fieldPath,
            slotId,
            candidateId,
            taskId,
            moodToken,
            mediaToken,
            definition.AnswerSchema,
            answers ?? new Dictionary<string, string?>(),
            evidence ?? [],
            []);
    }

    private static PlannerPrimitiveDefinition Definition(PlannerToolManifest manifest, string primitiveId)
    {
        return manifest.AllowedPrimitives.First(definition => definition.PrimitiveId == primitiveId);
    }

    private static TripSession PreparedCandidateSession()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        CompileAndAssociate(session);
        return session;
    }

    private static void CompileAndAssociate(TripSession session)
    {
        var compiled = new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
        Assert.True(compiled.IsCompiled);
        session.AssociateItineraryCandidatePool(ActivitySlot(session).SlotId, "pool-logistics");
    }

    private static ItinerarySlot ActivitySlot(TripSession session)
    {
        return session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == ItinerarySlotKind.Activity);
    }

    private static (int Actions, int Decisions, int ItineraryDecisions, int Approvals, int Deferred) Counts(TripSession session)
    {
        return (
            session.Actions.Count,
            session.DecisionLedger.Records.Count,
            session.ItineraryDecisions.Count,
            session.ApprovalTokens.Count,
            session.DeferredSlots.Count);
    }

    private static void AssertBlocked(PlannerPrimitiveValidationResult result, string code)
    {
        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(code, result.Code);
        Assert.Empty(result.View.Primitives);
    }
}
