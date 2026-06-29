using Pch.Core;
using Pch.Harness;
using Xunit;

namespace Pch.Harness.Tests;

public sealed class ProjectionServiceTests
{
    [Fact]
    public void StableFixturePacketKeepsExpectedIdentityAndAllowedActions()
    {
        var packet = new ProjectionService().StableFixturePacket();

        Assert.Equal("packet-session-7-day-slotcollection", packet.PacketId);
        Assert.Equal("SlotCollection", packet.Stage);
        Assert.Contains(HarnessAction.EmitFormKind, packet.AllowedActions);
        Assert.Contains(packet.TraceRequirements, requirement => requirement.Contains("candidate IDs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SyntheticTripProjectionsStayBoundedForOneSevenAndFourteenDays()
    {
        var packets = new ProjectionService().ProjectSyntheticTrips(1, 7, 14);

        Assert.Equal(3, packets.Count);
        Assert.All(packets, packet =>
        {
            Assert.True(packet.LoadBearingFacts.Count <= 12);
            Assert.True(packet.Candidates.Count <= 6);
            Assert.True(packet.Constraints.Count <= 8);
            Assert.Contains(packet.LoadBearingFacts, fact => fact.StartsWith("day_count:", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void StageMachineAdvancesThroughSkeletonGraph()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var machine = new StageMachine();

        Assert.Equal(HarnessStage.Intake, session.Stage);
        Assert.Equal(HarnessStage.SlotCollection, machine.Advance(session));
        Assert.IsType<EmitFormAction>(machine.NextSkeletonAction(session));
    }
}
