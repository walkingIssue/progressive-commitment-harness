using Pch.Providers.CandidateExpansion;

namespace Pch.Providers.Mock;

public sealed class MockCandidateExpansionSource : ICandidateExpansionSource
{
    public const string ProviderName = "mock-candidate-expansion";

    public Task<CandidateExpansionResult> ExpandAsync(
        CandidateExpansionPacket packet,
        CandidateExpansionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        var candidatesPerSlot = Math.Max(1, options?.CandidatesPerSlot ?? 2);
        var slots = packet.Slots
            .Select(slot => new CandidateSlotExpansion(
                slot.SlotId,
                slot.Category,
                CreateCandidates(slot, candidatesPerSlot)))
            .ToArray();

        return Task.FromResult(new CandidateExpansionResult(
            packet.PacketId,
            slots,
            ResponseContentLength: 0,
            ProviderName,
            options?.Model ?? "mock-candidate-expansion-deterministic",
            $"mock-candidates-{packet.PacketId}"));
    }

    private static IReadOnlyList<ItineraryCandidate> CreateCandidates(
        CandidateExpansionSlot slot,
        int count) =>
        Enumerable.Range(1, count)
            .Select(index => CreateCandidate(slot, index))
            .ToArray();

    private static ItineraryCandidate CreateCandidate(CandidateExpansionSlot slot, int index) =>
        slot.Category switch
        {
            CandidateCategory.Dining => new(
                $"{slot.SlotId}-dining-{index}",
                CandidateCategory.Dining,
                index == 1 ? "Neighborhood lunch" : "Low-key dinner",
                ["meal", "local"],
                slot.DurationMinutes ?? 75,
                CandidateCostLevel.Medium,
                RequiresBooking: index != 1),
            CandidateCategory.Activity => new(
                $"{slot.SlotId}-activity-{index}",
                CandidateCategory.Activity,
                index == 1 ? "Museum visit" : "Guided neighborhood walk",
                ["culture", "daytime"],
                slot.DurationMinutes ?? 120,
                CandidateCostLevel.Medium,
                RequiresBooking: index == 1),
            CandidateCategory.Transit => new(
                $"{slot.SlotId}-transit-{index}",
                CandidateCategory.Transit,
                index == 1 ? "Direct train transfer" : "Rideshare transfer",
                ["transport", "connection"],
                slot.DurationMinutes ?? 45,
                index == 1 ? CandidateCostLevel.Low : CandidateCostLevel.Medium,
                RequiresBooking: false),
            CandidateCategory.Downtime => new(
                $"{slot.SlotId}-downtime-{index}",
                CandidateCategory.Downtime,
                index == 1 ? "Flexible rest window" : "Quiet cafe buffer",
                ["flexible", "recovery"],
                slot.DurationMinutes ?? 90,
                index == 1 ? CandidateCostLevel.Free : CandidateCostLevel.Low,
                RequiresBooking: false),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot.Category, null)
        };
}
