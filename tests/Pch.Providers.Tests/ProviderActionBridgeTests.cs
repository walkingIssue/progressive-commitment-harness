using System.Text.Json;
using Pch.Providers.Mock;
using Pch.Providers.ModelActions;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class ProviderActionBridgeTests
{
    [Fact]
    public void BridgeCreatesProviderLocalProposalWithoutRawArguments()
    {
        var packet = CreatePacket();
        var result = CreateResult("emit_form", new { field = "destination", secret = "SECRET_VALUE_SHOULD_NOT_LEAK" });

        var bridge = new ProviderActionBridge().Bridge(packet, result);

        Assert.True(bridge.IsAccepted);
        Assert.Equal(ProviderActionBridge.DecodeAccepted, bridge.DecodeOutcomeCode);
        Assert.Equal(ProviderActionBridge.IntakeNotRunProviderLocalMirror, bridge.IntakeOutcomeCode);
        Assert.NotNull(bridge.Proposal);
        Assert.Equal("proposal-packet-bridge", bridge.Proposal.ProposalId);
        Assert.Equal("emit_form", bridge.Proposal.ActionKind);
        Assert.Equal(["field", "secret"], bridge.Proposal.ArgumentKeys);
        Assert.NotNull(bridge.RuntimeProposal);
        Assert.Equal("proposal-packet-bridge", bridge.RuntimeProposal.ActionId);
        Assert.Equal("emit_form", bridge.RuntimeProposal.Kind);
        Assert.Equal("SECRET_VALUE_SHOULD_NOT_LEAK", bridge.RuntimeProposal.Arguments.GetProperty("secret").GetString());

        var serialized = JsonSerializer.Serialize(bridge, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("SECRET_VALUE_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("destination", serialized);
        Assert.DoesNotContain("runtimeProposal", serialized);
    }

    [Fact]
    public void BridgeRejectsActionOutsideAllowedSetWithSanitizedOutcome()
    {
        var result = CreateResult("RAW_PROVIDER_ACTION_SHOULD_NOT_LEAK", new { field = "destination" });

        var bridge = new ProviderActionBridge().Bridge(CreatePacket(), result);

        Assert.False(bridge.IsAccepted);
        Assert.Equal(ProviderActionBridge.DecodeActionOutsideAllowedSet, bridge.DecodeOutcomeCode);
        Assert.Null(bridge.Proposal);
        Assert.Null(bridge.RuntimeProposal);

        var serialized = JsonSerializer.Serialize(bridge, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("RAW_PROVIDER_ACTION_SHOULD_NOT_LEAK", serialized);
        Assert.DoesNotContain("destination", serialized);
    }

    [Fact]
    public void DeterministicRuntimeSourceCreatesAcceptedBlockedAndMalformedCases()
    {
        var source = new MockModelActionResultSource();
        var packet = CreateRuntimePacket();
        var bridge = new ProviderActionBridge();

        var accepted = bridge.Bridge(packet, source.AcceptedDeferSlot(packet));
        var blocked = bridge.Bridge(packet, source.BlockedHandoff(packet));
        var malformed = bridge.Bridge(packet, source.MalformedArguments(packet));

        Assert.True(accepted.IsAccepted);
        Assert.Equal("defer_slot", accepted.RuntimeProposal?.Kind);
        Assert.Equal("meal-window", accepted.RuntimeProposal?.Arguments.GetProperty("slot_id").GetString());
        Assert.Equal(ProviderActionBridge.DecodeActionOutsideAllowedSet, blocked.DecodeOutcomeCode);
        Assert.Null(blocked.RuntimeProposal);
        Assert.Equal(ProviderActionBridge.DecodeMissingArguments, malformed.DecodeOutcomeCode);
        Assert.Null(malformed.RuntimeProposal);

        var serializedAccepted = JsonSerializer.Serialize(accepted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("meal-window", serializedAccepted);
        Assert.DoesNotContain("Need user preference", serializedAccepted);
    }

    private static ModelActionPacket CreatePacket()
    {
        var input = JsonSerializer.SerializeToElement(new { destinationKnown = false });
        return new ModelActionPacket(
            "packet-bridge",
            "stage-ask",
            "Collect missing destination details.",
            ["Ask only for missing fields."],
            new Dictionary<string, JsonElement> { ["trip"] = input },
            [new ModelActionDefinition("emit_form", "Ask for structured fields.")]);
    }

    private static ModelActionPacket CreateRuntimePacket()
    {
        var input = JsonSerializer.SerializeToElement(new { destinationKnown = false });
        return new ModelActionPacket(
            "packet-runtime",
            "stage-runtime",
            "Apply deterministic runtime proposal.",
            ["Use the allowed action list."],
            new Dictionary<string, JsonElement> { ["trip"] = input },
            [new ModelActionDefinition("defer_slot", "Defer a slot.")]);
    }

    private static ModelActionRunResult CreateResult(string actionName, object arguments)
    {
        return new ModelActionRunResult(
            "packet-bridge",
            actionName,
            JsonSerializer.SerializeToElement(arguments),
            "summary",
            42,
            "provider",
            "model",
            "request-1");
    }
}
