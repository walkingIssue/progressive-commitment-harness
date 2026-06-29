namespace Pch.Providers.CandidateExpansion;

public sealed class CandidateExpansionEvaluator
{
    public const string OutcomeAccepted = "candidate_expansion_accepted";
    public const string OutcomePacketIdMismatch = "candidate_expansion_packet_id_mismatch";
    public const string OutcomeSlotMismatch = "candidate_expansion_slot_mismatch";
    public const string OutcomeError = "candidate_expansion_error";

    private readonly ICandidateExpansionSource _source;

    public CandidateExpansionEvaluator(ICandidateExpansionSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<IReadOnlyList<SanitizedCandidateExpansionEvalRow>> EvaluateAsync(
        IReadOnlyList<CandidateExpansionEvalCase> cases,
        CandidateExpansionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedCandidateExpansionEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var result = await _source.ExpandAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new SanitizedCandidateExpansionEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    false,
                    OutcomeError,
                    ToErrorCode(ex),
                    [],
                    0,
                    null,
                    null,
                    null,
                    null));
            }
        }

        return rows;
    }

    private static SanitizedCandidateExpansionEvalRow ToRow(
        CandidateExpansionEvalCase evalCase,
        CandidateExpansionResult result)
    {
        var packetMatches = string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal);
        if (!packetMatches)
        {
            return new SanitizedCandidateExpansionEvalRow(
                evalCase.Name,
                evalCase.Packet.PacketId,
                false,
                OutcomePacketIdMismatch,
                null,
                [],
                0,
                null,
                null,
                null,
                null);
        }

        var packetSlots = evalCase.Packet.Slots.ToDictionary(slot => slot.SlotId, StringComparer.Ordinal);
        var resultSlotIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resultSlot in result.Slots)
        {
            if (!resultSlotIds.Add(resultSlot.SlotId) ||
                !packetSlots.TryGetValue(resultSlot.SlotId, out var packetSlot) ||
                packetSlot.Category != resultSlot.Category)
            {
                return new SanitizedCandidateExpansionEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    false,
                    OutcomeSlotMismatch,
                    null,
                    [],
                    0,
                    null,
                    null,
                    null,
                null);
            }
        }

        if (resultSlotIds.Count != packetSlots.Count)
        {
            return new SanitizedCandidateExpansionEvalRow(
                evalCase.Name,
                evalCase.Packet.PacketId,
                false,
                OutcomeSlotMismatch,
                null,
                [],
                0,
                null,
                null,
                null,
                null);
        }

        var resultSlotsById = result.Slots.ToDictionary(slot => slot.SlotId, StringComparer.Ordinal);
        var slotRows = evalCase.Packet.Slots
            .Where(packetSlot => resultSlotsById.ContainsKey(packetSlot.SlotId))
            .Select(packetSlot => new SanitizedCandidateSlotEvalRow(
                packetSlot.SlotId,
                packetSlot.Category,
                resultSlotsById[packetSlot.SlotId].Candidates.Count))
            .OrderBy(slot => slot.SlotId, StringComparer.Ordinal)
            .ToArray();

        return new SanitizedCandidateExpansionEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            OutcomeAccepted,
            null,
            slotRows,
            slotRows.Sum(slot => slot.CandidateCount),
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static string ToErrorCode(Exception exception) =>
        exception.GetType().Name.Contains("Provider", StringComparison.Ordinal)
            ? "provider_error"
            : "candidate_expansion_error";
}
