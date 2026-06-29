using System.Text.Json;
using Pch.Providers.ModelActions;

namespace Pch.Providers.Mock;

public sealed class MockModelActionResultSource
{
    public ModelActionRunResult AcceptedDeferSlot(ModelActionPacket packet) =>
        Create(
            packet,
            "defer_slot",
            new
            {
                slot_id = "meal-window",
                reason = "Need user preference."
            },
            "mock-accepted");

    public ModelActionRunResult BlockedHandoff(ModelActionPacket packet) =>
        Create(
            packet,
            "handoff",
            new
            {
                target = "booking-adapter",
                reason = "Spend handoff requires approval."
            },
            "mock-blocked");

    public ModelActionRunResult MalformedArguments(ModelActionPacket packet) =>
        new(
            packet.PacketId,
            packet.AllowedActions.FirstOrDefault()?.Name ?? "emit_form",
            JsonSerializer.SerializeToElement("not-an-object"),
            "malformed deterministic mock",
            0,
            "mock-action",
            "mock-action-deterministic",
            $"mock-malformed-{packet.PacketId}");

    private static ModelActionRunResult Create(
        ModelActionPacket packet,
        string actionName,
        object arguments,
        string requestPrefix)
    {
        return new ModelActionRunResult(
            packet.PacketId,
            actionName,
            JsonSerializer.SerializeToElement(arguments),
            "deterministic mock runtime proposal",
            0,
            "mock-action",
            "mock-action-deterministic",
            $"{requestPrefix}-{packet.PacketId}");
    }
}
