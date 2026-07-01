using Pch.Core;
using Xunit;

namespace Pch.Core.Tests;

public sealed class PlannerPrimitiveContractsTests
{
    [Fact]
    public void PrimitiveAndMoodTokenSetsExposeSprintMinimums()
    {
        Assert.Contains(PlannerPrimitiveIds.AssistantMessage, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.CandidateDeck, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ToolSearchRequest, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerPrimitiveIds.ToolGapRequest, PlannerPrimitiveIds.Known);
        Assert.Contains(PlannerMoodTokens.ReflectiveCulture, PlannerMoodTokens.Known);
        Assert.Contains(PlannerMoodTokens.LivelyFood, PlannerMoodTokens.Known);
        Assert.Contains(PlannerMoodTokens.Neutral, PlannerMoodTokens.Known);
    }
}
