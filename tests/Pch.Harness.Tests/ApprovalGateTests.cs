using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class ApprovalGateTests
{
    [Fact]
    public void RefusesBookingActionWithoutApprovalToken()
    {
        var action = new RequestApprovalAction(
            "action-approval",
            new ApprovalRequest(
                "approval-1",
                "mock-booking",
                "Approve mocked booking.",
                ["booking"],
                100,
                "USD",
                null));

        var result = new ApprovalGate().Evaluate(action);

        Assert.False(result.IsAllowed);
        Assert.Contains("approval token", result.RefusalReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsBookingActionWithApprovalToken()
    {
        var action = new RequestApprovalAction(
            "action-approval",
            new ApprovalRequest(
                "approval-1",
                "mock-booking",
                "Approve mocked booking.",
                ["booking"],
                100,
                "USD",
                "approved-token"));

        var result = new ApprovalGate().Evaluate(action);

        Assert.True(result.IsAllowed);
        Assert.Equal("approved-token", result.ApprovalToken);
    }

    [Fact]
    public void RefusesSpendStatePatchWithoutApprovalToken()
    {
        var action = new StatePatchAction(
            "action-patch",
            new StatePatchProposal(
                "patch-spend",
                AuthoritySource.TrustedTool,
                "/spend/hold",
                null,
                "reserve candidate-1",
                ["evidence-tool"]));

        Assert.False(new ApprovalGate().Evaluate(action).IsAllowed);
    }
}
