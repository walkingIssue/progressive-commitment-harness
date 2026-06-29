using Pch.UI.Features.StageCockpit;
using Xunit;

namespace Pch.UI.Tests;

public sealed class HarnessStageCockpitServiceTests
{
    [Fact]
    public void ApplyFormAfterApprovalRequestBlocksWithoutAdvancingApprovalStage()
    {
        var service = new HarnessStageCockpitService();
        var approvalFixture = service.RequestApprovalStage();

        var repairedFixture = service.ApplyForm(new Dictionary<string, string>
        {
            ["destination_country"] = "Japan",
            ["purpose"] = "family travel"
        });

        Assert.Equal("ApprovalQueue", repairedFixture.Packet.Name);
        Assert.Equal("approval-review", repairedFixture.Approval.Id);
        Assert.Equal("approval-required", repairedFixture.Approval.State);
        Assert.Contains(
            repairedFixture.Session.Responses,
            response => response.State == SessionResponseState.ApprovalRequired
                && response.ApprovalId == "approval-review");
        Assert.Contains(
            repairedFixture.Session.Responses,
            response => response.State == SessionResponseState.Blocked
                && response.ApprovalId == "approval-review"
                && response.Summary.Contains("Cannot apply form", StringComparison.Ordinal));
        Assert.Equal("ApprovalQueue", approvalFixture.Packet.Name);
    }
}
