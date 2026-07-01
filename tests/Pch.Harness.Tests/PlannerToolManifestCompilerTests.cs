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

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveTextRedactedCode);
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

    [Fact]
    public void SafeModelAuthoredLabelsOptionsDefaultsSurviveValidation()
    {
        var session = PreparedCandidateSession();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.DaySkeletonGeneration);

        var result = Validate(session, manifest, [DynamicRankedChoice(manifest, "food-path", ["ramen", "markets"], "ramen,markets")]);

        Assert.True(result.IsAccepted);
        var primitive = Assert.Single(result.View.Primitives);
        Assert.Equal("Choose your Osaka evening shape", primitive.Label);
        Assert.Equal("Prioritize the safe model-authored options.", primitive.Prompt);
        Assert.Equal("Pick the order the planner should optimize first.", primitive.HelpText);
        Assert.Equal(["ramen", "markets"], primitive.Options.Select(option => option.OptionId).ToArray());
        Assert.Equal("ramen", Assert.Single(primitive.Defaults).Value);
        Assert.NotEqual("redacted", result.View.PrimitiveHash);
        Assert.Contains("food-path", result.View.RenderedPrimitiveIds);
    }

    [Fact]
    public void UnsafeModelAuthoredTextBlocksAndDoesNotSerialize()
    {
        var session = PreparedCandidateSession();
        var before = Counts(session);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.DaySkeletonGeneration);
        var unsafePrimitive = DynamicRankedChoice(manifest, "unsafe-food-path", ["ramen"], "ramen") with
        {
            Label = "RAW_PROMPT_SENTINEL_DO_NOT_LEAK",
            Options =
            [
                new(
                    "ramen",
                    "PROVIDER_PAYLOAD_SENTINEL_DO_NOT_LEAK",
                    "SECRET_SENTINEL_DO_NOT_LEAK",
                    PlannerMoodTokens.LivelyFood,
                    "media:lively_food",
                    [],
                    [])
            ]
        };

        var result = Validate(session, manifest, [unsafePrimitive]);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveTextRedactedCode);
        Assert.Equal(before, Counts(session));
        Assert.DoesNotContain("RAW_PROMPT_SENTINEL_DO_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PROVIDER_PAYLOAD_SENTINEL_DO_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL_DO_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentModelPrimitiveStructuresProduceDifferentValidatedTurnViews()
    {
        var session = PreparedCandidateSession();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.DaySkeletonGeneration);

        var food = Validate(session, manifest, [DynamicRankedChoice(manifest, "choice-food", ["ramen", "markets"], "ramen,markets")]);
        var nature = Validate(session, manifest, [DynamicRankedChoice(manifest, "choice-nature", ["glacier", "hot_spring"], "glacier,hot_spring")]);

        Assert.True(food.IsAccepted);
        Assert.True(nature.IsAccepted);
        Assert.NotEqual(food.View.PrimitiveHash, nature.View.PrimitiveHash);
        Assert.NotEqual(food.View.Primitives[0].Options.Select(option => option.OptionId), nature.View.Primitives[0].Options.Select(option => option.OptionId));
    }

    [Fact]
    public void AnswerDtoUpdatesPersistentPlanningContext()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var context = new PlanningSessionContext(session);
        var builder = new PlannerTurnContextBuilder();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var validated = Validate(session, manifest, [TextInput(manifest, "purpose-input", "/mission/purpose", "quiet food trip")]);

        builder.RecordValidatedTurn(context, validated.View);
        var result = context.ApplyAnswers(new PlannerAnswerApplicationRequest(
            session.SessionId,
            new PlannerToolManifestCompiler().CurrentGraphRevision(session),
            [
                new(
                    "answer-purpose-1",
                    "purpose-input",
                    PlannerPrimitiveIds.TextInput,
                    new Dictionary<string, string?> { ["value"] = "late night ramen and markets" },
                    [],
                    ["evidence-user-purpose"])
            ]));

        Assert.True(result.IsAccepted);
        Assert.Single(context.SubmittedAnswers);
        Assert.Contains(context.AcceptedFacts, fact => fact.FieldPath == "/mission/purpose" && fact.Value == "late night ramen and markets");
    }

    [Fact]
    public void SecondTurnContextContainsSubmittedAnswerValuesAndAcceptedFactsWithoutRawPromptSerialization()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var context = new PlanningSessionContext(session);
        var builder = new PlannerTurnContextBuilder();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var validated = Validate(session, manifest, [TextInput(manifest, "purpose-input", "/mission/purpose", "quiet food trip")]);
        builder.RecordValidatedTurn(context, validated.View);
        context.ApplyAnswers(new PlannerAnswerApplicationRequest(
            session.SessionId,
            new PlannerToolManifestCompiler().CurrentGraphRevision(session),
            [
                new(
                    "answer-purpose-2",
                    "purpose-input",
                    PlannerPrimitiveIds.TextInput,
                    new Dictionary<string, string?> { ["value"] = "quiet hiking and hot springs" },
                    [],
                    ["evidence-user-purpose"])
            ]));

        var turnContext = builder.Build(context, new PlannerTurnContextRequest(
            session.SessionId,
            "RAW_PROMPT_SENTINEL transient hiking prompt",
            "en-US",
            ["quiet"]));
        var serialized = JsonSerializer.Serialize(turnContext, JsonOptions);

        Assert.Contains(turnContext.SubmittedAnswers, answer => answer.Values.Values.Contains("quiet hiking and hot springs", StringComparer.Ordinal));
        Assert.Contains(turnContext.AcceptedFacts, fact => fact.Value == "quiet hiking and hot springs");
        Assert.Equal("family_support", builder.Build(context, new PlannerTurnContextRequest(session.SessionId, "family support planning", null, [])).PromptCategory);
        Assert.DoesNotContain("RAW_PROMPT_SENTINEL", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("transient hiking prompt", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskListPrimitiveValidatesAndBecomesTaskRailRefs()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var taskId = manifest.AllowedTaskIds.First();
        var definition = Definition(manifest, PlannerPrimitiveIds.TaskList);
        var primitive = Instance(definition, "task-list-1", label: "Planning tasks", prompt: "Review next work.", answers: new Dictionary<string, string?>()) with
        {
            MoodToken = PlannerMoodTokens.Logistics,
            TaskReferences =
            [
                new(taskId, "Confirm the mission direction.", null, ["evidence-user-purpose"], ["evidence-user-purpose"])
            ],
            ToolContextReferences = ["evidence-user-purpose"]
        };

        var result = Validate(session, manifest, [primitive]);

        Assert.True(result.IsAccepted);
        Assert.Contains(taskId, result.View.TaskRailItemRefs);
        Assert.Contains("evidence-user-purpose", result.View.ToolContextReferences);
    }

    [Fact]
    public void ToolSearchRequestValidatesOnlyWhenToolIsAllowed()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session);
        var allowed = Validate(session, manifest, [ToolRequest(manifest, PlannerPrimitiveIds.ToolSearchRequest, "tool-search-request")]);
        var narrowedManifest = manifest with { AllowedToolIds = [] };
        var blocked = Validate(session, narrowedManifest, [ToolRequest(narrowedManifest, PlannerPrimitiveIds.ToolSearchRequest, "tool-search-request")]);

        Assert.True(allowed.IsAccepted);
        Assert.Equal(PlannerPrimitiveValidator.ToolSearchRequestedCode, allowed.Code);
        AssertBlocked(blocked, PlannerPrimitiveValidator.ToolNotAllowedCode);
    }

    [Fact]
    public void AnswerValueOutsideValidatedOptionsBlocksWithoutContextMutation()
    {
        var session = PreparedCandidateSession();
        var context = new PlanningSessionContext(session);
        var builder = new PlannerTurnContextBuilder();
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.DaySkeletonGeneration);
        var validated = Validate(session, manifest, [DynamicRankedChoice(manifest, "food-path", ["ramen", "markets"], "ramen,markets")]);
        builder.RecordValidatedTurn(context, validated.View);

        var result = context.ApplyAnswers(new PlannerAnswerApplicationRequest(
            session.SessionId,
            new PlannerToolManifestCompiler().CurrentGraphRevision(session),
            [
                new(
                    "answer-invalid-option",
                    "food-path",
                    PlannerPrimitiveIds.RankedChoice,
                    new Dictionary<string, string?> { ["ranked"] = "temples" },
                    ["temples"],
                    [])
            ]));

        Assert.False(result.IsAccepted);
        Assert.True(result.IsBlocked);
        Assert.Equal(PlannerPrimitiveValidator.AnswerValueNotAllowedCode, result.Code);
        Assert.Empty(context.SubmittedAnswers);
        Assert.Empty(context.AcceptedFacts);
    }

    [Theory]
    [InlineData(PlannerPrimitiveIds.Select)]
    [InlineData(PlannerPrimitiveIds.RadioGroup)]
    public void ChoicePrimitiveWithoutOptionsBlocks(string primitiveId)
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var definition = Definition(manifest, primitiveId);
        var primitive = Instance(
            definition,
            $"{primitiveId}-missing-options",
            label: "Choose pace",
            prompt: "Choose one trusted option.",
            fieldPath: "/constraints/pace",
            answers: new Dictionary<string, string?> { ["selected"] = "balanced" });

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveOptionsMissingCode);
    }

    [Fact]
    public void DateRangeWithTextDefaultsBlocksAsAnswerSchemaInvalid()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var primitive = DateRange(manifest, "dates-input") with
        {
            Defaults =
            [
                new PlannerPrimitiveDefault("start", "soon"),
                new PlannerPrimitiveDefault("end", "later")
            ]
        };

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveAnswerSchemaInvalidCode);
    }

    [Fact]
    public void TextInputForChoiceFieldBlocksAsRendererMismatch()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var primitive = TextInput(manifest, "pace-input", "/constraints/pace", "balanced");

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveRendererMismatchCode);
    }

    [Fact]
    public void TaskDecompositionInvalidIdsOrderAndDependenciesBlock()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var definition = Definition(manifest, PlannerPrimitiveIds.TaskDecomposition);
        var primitive = Instance(
            definition,
            "task-decomposition-1",
            label: "Plan work",
            prompt: "Decompose planning work.",
            answers: new Dictionary<string, string?>()) with
        {
            TaskDecomposition =
            [
                new("task-a", "Ask for dates.", PlannerTaskStates.Pending, 1, ["missing-task"], []),
                new("task-b", "Build options.", PlannerTaskStates.Active, 1, [], [])
            ]
        };

        var result = Validate(session, manifest, [primitive]);

        AssertBlocked(result, PlannerPrimitiveValidator.TaskDecompositionInvalidCode);
    }

    [Fact]
    public void AcceptedTaskDecompositionPreservesTaskIdsTitlesAndStates()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var definition = Definition(manifest, PlannerPrimitiveIds.TaskDecomposition);
        var primitive = Instance(
            definition,
            "task-decomposition-accepted",
            label: "Plan work",
            prompt: "Decompose planning work.",
            answers: new Dictionary<string, string?>()) with
        {
            TaskDecomposition =
            [
                new("task-a", "Confirm destination.", PlannerTaskStates.Active, 0, [], ["evidence-user-purpose"]),
                new("task-b", "Collect dates.", PlannerTaskStates.Pending, 1, ["task-a"], ["evidence-user-purpose"])
            ]
        };

        var result = Validate(session, manifest, [primitive]);

        Assert.True(result.IsAccepted);
        var tasks = Assert.Single(result.View.Primitives).TaskDecomposition;
        Assert.Equal(["task-a", "task-b"], tasks.Select(task => task.TaskId).ToArray());
        Assert.Equal(["Confirm destination.", "Collect dates."], tasks.Select(task => task.Title).ToArray());
        Assert.Equal([PlannerTaskStates.Active, PlannerTaskStates.Pending], tasks.Select(task => task.State).ToArray());
        Assert.Contains("task-a", result.View.TaskRailItemRefs);
        Assert.Contains("task-b", result.View.TaskRailItemRefs);
    }

    [Fact]
    public void UnsafeChoiceLabelsAndToolRefsBlockWithoutPlanningContextMutation()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        var context = new PlanningSessionContext(session);
        var beforeAnswers = context.SubmittedAnswers.Count;
        var beforeFacts = context.AcceptedFacts.Count;
        var manifest = new PlannerToolManifestCompiler().Compile(session, HarnessStage.Intake);
        var definition = Definition(manifest, PlannerPrimitiveIds.Select);
        var primitive = Instance(
            definition,
            "pace-select",
            label: "Choose pace",
            prompt: "Choose one pace.",
            fieldPath: "/constraints/pace",
            answers: new Dictionary<string, string?> { ["selected"] = "balanced" }) with
        {
            Options =
            [
                new(
                    "balanced",
                    "RAW_PROMPT_SENTINEL_DO_NOT_LEAK",
                    "SECRET_SENTINEL_DO_NOT_LEAK",
                    PlannerMoodTokens.Neutral,
                    null,
                    [],
                    ["unknown-tool-context"])
            ]
        };

        var result = Validate(session, manifest, [primitive]);
        var serialized = JsonSerializer.Serialize(result, JsonOptions);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveTextRedactedCode);
        Assert.Equal(beforeAnswers, context.SubmittedAnswers.Count);
        Assert.Equal(beforeFacts, context.AcceptedFacts.Count);
        Assert.DoesNotContain("RAW_PROMPT_SENTINEL_DO_NOT_LEAK", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_SENTINEL_DO_NOT_LEAK", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySprint024PrimitiveTypeIsExposedByManifestOrExplicitlyBlocks()
    {
        var session = PreparedCandidateSession();
        var compiler = new PlannerToolManifestCompiler();
        var intake = compiler.Compile(session, HarnessStage.Intake);
        var planning = compiler.Compile(session, HarnessStage.DaySkeletonGeneration);
        var exposed = intake.AllowedPrimitives
            .Concat(planning.AllowedPrimitives)
            .Select(definition => definition.PrimitiveId)
            .ToHashSet(StringComparer.Ordinal);
        var required =
            new[]
            {
                PlannerPrimitiveIds.AssistantMessage,
                PlannerPrimitiveIds.StatusNotice,
                PlannerPrimitiveIds.TextInput,
                PlannerPrimitiveIds.Textarea,
                PlannerPrimitiveIds.NumberInput,
                PlannerPrimitiveIds.Slider,
                PlannerPrimitiveIds.Date,
                PlannerPrimitiveIds.DateRange,
                PlannerPrimitiveIds.RadioGroup,
                PlannerPrimitiveIds.Select,
                PlannerPrimitiveIds.MultiSelect,
                PlannerPrimitiveIds.Checkbox,
                PlannerPrimitiveIds.ChoiceCard,
                PlannerPrimitiveIds.CandidateDeck,
                PlannerPrimitiveIds.TaskDecomposition,
                PlannerPrimitiveIds.TimelineItem,
                PlannerPrimitiveIds.ToolSearchRequest,
                PlannerPrimitiveIds.ToolGapRequest
            };

        Assert.All(required, primitiveId => Assert.Contains(primitiveId, exposed));

        var unsupported = AssistantMessage(intake) with
        {
            PrimitiveId = "not_a_tool",
            RendererKey = "not-a-tool"
        };
        var result = Validate(session, intake, [unsupported]);

        AssertBlocked(result, PlannerPrimitiveValidator.PrimitiveNotSupportedCode);
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
            evidence: ["evidence-fixture-candidates"]) with
        {
            Options =
            [
                new(
                    candidateId,
                    "Trusted candidate option",
                    "Fixture option for a trusted slot candidate.",
                    PlannerMoodTokens.Neutral,
                    "media:neutral",
                    ["evidence-fixture-candidates"],
                    ["evidence-fixture-candidates"])
            ]
        };
    }

    private static PlannerPrimitiveInstance DynamicRankedChoice(
        PlannerToolManifest manifest,
        string instanceId,
        IReadOnlyList<string> optionIds,
        string rankedAnswer)
    {
        var definition = Definition(manifest, PlannerPrimitiveIds.RankedChoice);
        return Instance(
            definition,
            instanceId,
            label: "Choose your Osaka evening shape",
            prompt: "Prioritize the safe model-authored options.",
            moodToken: PlannerMoodTokens.LivelyFood,
            mediaToken: "media:lively_food",
            answers: new Dictionary<string, string?> { ["ranked"] = rankedAnswer },
            evidence: ["evidence-user-purpose"]) with
        {
            HelpText = "Pick the order the planner should optimize first.",
            Options = optionIds
                .Select(id => new PlannerPrimitiveOption(
                    id,
                    id == "ramen" ? "Late ramen lanes" : $"Option {id}",
                    id == "ramen" ? "Late snacks and market-adjacent meals." : "Safe model-authored option.",
                    id == "ramen" ? PlannerMoodTokens.LivelyFood : PlannerMoodTokens.Neutral,
                    id == "ramen" ? "media:lively_food" : "media:neutral",
                    ["evidence-user-purpose"],
                    ["evidence-user-purpose"]))
                .ToArray(),
            Defaults = [new PlannerPrimitiveDefault("ranked", optionIds[0])],
            RendererHints = [new PlannerRendererHint("renderer", "card_group")]
        };
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
