using Pch.Core;
using Xunit;

namespace Pch.Core.Tests;

public sealed class StateAuthorityPolicyTests
{
    [Fact]
    public void UserPatchOnUnprotectedPathCanAutoApply()
    {
        var patch = new StatePatchProposal(
            "patch-1",
            AuthoritySource.User,
            "/travelers/0/needs",
            null,
            "quiet hotel",
            ["evidence-user"]);

        Assert.True(StateAuthorityPolicy.Default.CanAutoApply(patch));
    }

    [Fact]
    public void StrongModelInferenceRequiresConfirmation()
    {
        var patch = new StatePatchProposal(
            "patch-2",
            AuthoritySource.StrongModelInference,
            "/travelers/0/needs",
            null,
            "wheelchair assistance",
            ["evidence-inference"]);

        Assert.True(StateAuthorityPolicy.Default.RequiresConfirmation(patch));
    }

    [Fact]
    public void ProtectedBookingPathRequiresConfirmationEvenFromTrustedSource()
    {
        var patch = new StatePatchProposal(
            "patch-3",
            AuthoritySource.TrustedTool,
            "/booking/hold",
            null,
            "hold candidate-1",
            ["evidence-tool"]);

        Assert.False(StateAuthorityPolicy.Default.CanAutoApply(patch));
    }
}
