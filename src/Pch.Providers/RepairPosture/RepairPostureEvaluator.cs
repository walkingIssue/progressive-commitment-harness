using Pch.Providers.Errors;

namespace Pch.Providers.RepairPosture;

public sealed class RepairPostureEvaluator
{
    public const string RejectedRowName = "repair_posture_rejected";
    public const string RejectedRowPacketId = "repair_posture_packet_redacted";
    public const string OutcomeAccepted = "repair_posture_accepted";
    public const string OutcomePacketMismatch = "repair_posture_packet_mismatch";
    public const string OutcomeNodeMismatch = "repair_posture_node_mismatch";
    public const string OutcomeMalformedPacket = "repair_posture_malformed_packet";
    public const string OutcomeMalformedResult = "repair_posture_malformed_result";
    public const string OutcomeUnsupportedMode = "repair_posture_unsupported_mode";
    public const string OutcomeLiveDisabled = "repair_posture_live_disabled";
    public const string OutcomeKeyMissing = "repair_posture_key_missing";
    public const string OutcomeCreditExhausted = "repair_posture_credit_exhausted";
    public const string OutcomeTimeout = "repair_posture_timeout";
    public const string OutcomeProviderUnavailable = "repair_posture_provider_unavailable";
    public const string OutcomeError = "repair_posture_error";

    private readonly IRepairPostureSource _source;

    public RepairPostureEvaluator(IRepairPostureSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<IReadOnlyList<SanitizedRepairPostureEvalRow>> EvaluateAsync(
        IReadOnlyList<RepairPostureEvalCase?>? cases,
        RepairPostureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(OutcomeMalformedPacket)];
        }

        var rows = new List<SanitizedRepairPostureEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            var malformedPacket = ValidateEvalCase(evalCase);
            if (malformedPacket is not null)
            {
                rows.Add(malformedPacket);
                continue;
            }

            try
            {
                var result = await _source.SuggestAsync(evalCase!.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedRepairPostureEvalRow? ValidateEvalCase(RepairPostureEvalCase? evalCase)
    {
        if (evalCase?.Packet is null ||
            string.IsNullOrWhiteSpace(evalCase.Packet.PacketId) ||
            evalCase.Packet.Nodes is null ||
            evalCase.Packet.Nodes.Count == 0)
        {
            return Rejected(OutcomeMalformedPacket);
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in evalCase.Packet.Nodes)
        {
            if (node is null ||
                string.IsNullOrWhiteSpace(node.NodeId) ||
                !Enum.IsDefined(node.NodeKind) ||
                !Enum.IsDefined(node.Status) ||
                node.DownstreamDependencyCount < 0 ||
                !nodeIds.Add(node.NodeId))
            {
                return Rejected(OutcomeMalformedPacket);
            }
        }

        return null;
    }

    private static SanitizedRepairPostureEvalRow ToRow(
        RepairPostureEvalCase evalCase,
        RepairPostureResult? result)
    {
        if (result?.Suggestions is null)
        {
            return Rejected(OutcomeMalformedResult);
        }

        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(OutcomePacketMismatch);
        }

        var packetNodes = evalCase.Packet.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var seenSuggestions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var suggestion in result.Suggestions)
        {
            if (suggestion is null ||
                string.IsNullOrWhiteSpace(suggestion.NodeId) ||
                suggestion.AffectedNodeCount < 0 ||
                !packetNodes.ContainsKey(suggestion.NodeId) ||
                !seenSuggestions.Add(suggestion.NodeId))
            {
                return Rejected(OutcomeNodeMismatch);
            }

            if (!Enum.IsDefined(suggestion.Mode) || !Enum.IsDefined(suggestion.ReasonCode))
            {
                return Rejected(OutcomeUnsupportedMode);
            }
        }

        var suggestionRows = result.Suggestions
            .Select(suggestion =>
            {
                var packetNode = packetNodes[suggestion.NodeId];
                return new SanitizedRepairSuggestionRow(
                    packetNode.NodeId,
                    packetNode.NodeKind,
                    suggestion.Mode,
                    suggestion.ReasonCode,
                    suggestion.AffectedNodeCount);
            })
            .OrderBy(suggestion => suggestion.NodeId, StringComparer.Ordinal)
            .ToArray();

        return new SanitizedRepairPostureEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            OutcomeAccepted,
            null,
            suggestionRows,
            evalCase.Packet.Nodes.Count,
            suggestionRows.Length,
            suggestionRows.Sum(suggestion => suggestion.AffectedNodeCount),
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static SanitizedRepairPostureEvalRow ExceptionRow(Exception exception) =>
        exception switch
        {
            RepairPostureGuardException guard => Rejected(guard.OutcomeCode, guard.ErrorCode),
            ProviderCreditExhaustedException => Rejected(OutcomeCreditExhausted, "credit_exhausted"),
            ProviderEmptyResponseException => Rejected(OutcomeMalformedResult, "empty_content"),
            ProviderMalformedResponseException => Rejected(OutcomeMalformedResult, "malformed_schema"),
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => Rejected(OutcomeTimeout, "timeout"),
            TimeoutException => Rejected(OutcomeTimeout, "timeout"),
            ProviderUnavailableException => Rejected(OutcomeProviderUnavailable, "provider_error"),
            ProviderException => Rejected(OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(OutcomeError, "repair_posture_error")
        };

    public static SanitizedRepairPostureEvalRow Rejected(
        string outcomeCode,
        string? errorCode = null) =>
        new(
            RejectedRowName,
            RejectedRowPacketId,
            false,
            outcomeCode,
            errorCode,
            [],
            0,
            0,
            0,
            null,
            null,
            null,
            null);
}
