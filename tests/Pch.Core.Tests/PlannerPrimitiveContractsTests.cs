using Pch.Core;
using Xunit;

namespace Pch.Core.Tests;

public sealed class PlannerPrimitiveContractsTests
{
    [Fact]
    public void PrimitiveAndMoodTokenSetsExposeSprintMinimums()
    {
        Assert.Contains(PlannerPrimitiveIds.AssistantMessage, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.StatusNotice, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.TextInput, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.Textarea, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.NumberInput, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.Slider, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.Date, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.DateRange, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.RadioGroup, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.Select, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.MultiSelect, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.Checkbox, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ChoiceCard, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.CandidateDeck, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.TaskDecomposition, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.TimelineItem, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.TaskList, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.TaskGroup, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ToolSearchRequest, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ToolGapRequest, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ToolContextReference, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerMoodTokens.ReflectiveCulture, PlannerMoodTokens.Known);
        Assert.Contains(PlannerMoodTokens.LivelyFood, PlannerMoodTokens.Known);
        Assert.Contains(PlannerMoodTokens.Neutral, PlannerMoodTokens.Known);
    }

    [Fact]
    public void DynamicPrimitiveContractsExposeRendererNeutralModelAuthoredFields()
    {
        var option = new PlannerPrimitiveOption(
            "food_first",
            "Food-first nights",
            "Markets and late meals.",
            PlannerMoodTokens.LivelyFood,
            "media:lively_food",
            ["evidence-user-purpose"],
            ["evidence-user-purpose"]);
        var primitive = new PlannerPrimitiveInstance(
            "travel-style",
            PlannerPrimitiveIds.SingleSelect,
            1,
            "single-select",
            "Choose the feel",
            "Which planning direction should the model pursue?",
            "/mission/purpose",
            null,
            null,
            null,
            PlannerMoodTokens.LivelyFood,
            null,
            new PlannerAnswerSchema(PlannerAnswerSchemaKinds.SingleSelect, true, null, 1, ["food_first"]),
            new Dictionary<string, string?> { ["selected"] = "food_first" },
            ["evidence-user-purpose"],
            [])
        {
            HelpText = "A card click and radio choice submit the same answer.",
            Options = [option],
            Defaults = [new PlannerPrimitiveDefault("selected", "food_first")],
            TaskReferences = [new PlannerTaskReference("task-food", "Shape food-first evenings.", null, [], ["evidence-user-purpose"])],
            ToolContextReferences = ["evidence-user-purpose"],
            ValidationRules = [new PlannerPrimitiveValidationRule("rule-required", "required", "true", "answer_schema_invalid")],
            RendererHints = [new PlannerRendererHint("renderer", "card_group")]
        };

        Assert.Equal("food_first", Assert.Single(primitive.Options).OptionId);
        Assert.Equal("food_first", Assert.Single(primitive.Defaults).Value);
        Assert.Equal("task-food", Assert.Single(primitive.TaskReferences).TaskId);
        Assert.Equal("card_group", Assert.Single(primitive.RendererHints).Value);
    }

    [Fact]
    public void HtmlPrimitiveContractsExposeExplicitRendererAndAnswerKinds()
    {
        Assert.Equal("select", PlannerRendererKeys.Select);
        Assert.Equal("radio-group", PlannerRendererKeys.RadioGroup);
        Assert.Equal("date-range", PlannerRendererKeys.DateRange);
        Assert.Equal("task-decomposition", PlannerRendererKeys.TaskDecomposition);
        Assert.Equal("single_choice", PlannerAnswerValueKinds.SingleChoice);
        Assert.Equal("date_range", PlannerAnswerValueKinds.DateRange);
        Assert.Contains(PlannerTaskStates.Pending, PlannerTaskStates.Known);
        Assert.Contains(PlannerTaskStates.Blocked, PlannerTaskStates.Known);
    }
}
