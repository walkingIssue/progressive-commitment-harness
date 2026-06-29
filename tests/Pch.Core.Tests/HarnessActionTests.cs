using Pch.Core;
using Xunit;

namespace Pch.Core.Tests;

public sealed class HarnessActionTests
{
    [Fact]
    public void KnownKindsContainRequiredClosedActionSet()
    {
        var expected = new[]
        {
            "emit_form",
            "emit_choice_set",
            "propose_search",
            "summarize",
            "request_approval",
            "state_patch",
            "defer_slot",
            "handoff"
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), HarnessAction.KnownKinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ModelFacingStagePacketKeepsCandidateSummariesFlat()
    {
        var packet = new StagePacket(
            "packet-1",
            "session-1",
            "Logistics",
            "Compare travel options",
            ["destination_country: Japan"],
            [new CandidateSummary("candidate-1", "Transit", "Rail pass", "Fixture summary", ["evidence-1"])],
            ["constraint-pace: balanced"],
            ["Small-model drafts cannot auto-apply protected state."],
            [HarnessAction.EmitChoiceSetKind],
            ["Preserve candidate IDs."]);

        Assert.Equal("candidate-1", packet.Candidates.Single().CandidateId);
        Assert.DoesNotContain(packet.LoadBearingFacts, fact => fact.Contains('{'));
    }

    [Fact]
    public void ChoiceSelectionAndApprovalTokenAreFlatUiContracts()
    {
        var selectedAt = new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
        var selection = new ChoiceSelection("choice-logistics", ["candidate-01"], selectedAt);
        var token = new ApprovalToken("approval-1", "token-1", selectedAt);

        Assert.Equal("candidate-01", selection.CandidateIds.Single());
        Assert.Equal("approval-1", token.ApprovalId);
        Assert.Equal("token-1", token.Token);
    }
}
