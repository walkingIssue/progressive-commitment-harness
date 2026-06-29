using Pch.Providers.Errors;

namespace Pch.Providers.AvailabilityPreview;

public sealed class AvailabilityPreviewEvaluator
{
    public const string OutcomeQuoteReady = "availability_preview_quote_ready";
    public const string OutcomeUnavailable = "availability_preview_unavailable";
    public const string OutcomePacketMismatch = "availability_preview_packet_mismatch";
    public const string OutcomeCandidateMismatch = "availability_preview_candidate_mismatch";
    public const string OutcomeMalformedPacket = "availability_preview_malformed_packet";
    public const string OutcomeMalformedResult = "availability_preview_malformed_result";
    public const string OutcomeUnsupportedResult = "availability_preview_unsupported_result";
    public const string OutcomeUnsupportedCategory = "availability_preview_unsupported_category";
    public const string OutcomeTimeout = "availability_preview_timeout";
    public const string OutcomeProviderUnavailable = "availability_preview_provider_unavailable";
    public const string OutcomeError = "availability_preview_error";

    private readonly IAvailabilityPreviewAdapter _adapter;

    public AvailabilityPreviewEvaluator(IAvailabilityPreviewAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public async Task<IReadOnlyList<SanitizedAvailabilityPreviewEvalRow>> EvaluateAsync(
        IReadOnlyList<AvailabilityPreviewEvalCase> cases,
        AvailabilityPreviewOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedAvailabilityPreviewEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            var malformedPacketRow = ValidateEvalCase(evalCase);
            if (malformedPacketRow is not null)
            {
                rows.Add(malformedPacketRow);
                continue;
            }

            try
            {
                var result = await _adapter.PreviewAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(evalCase.Name, evalCase.Packet.PacketId, ex));
            }
        }

        return rows;
    }

    private static SanitizedAvailabilityPreviewEvalRow? ValidateEvalCase(AvailabilityPreviewEvalCase? evalCase)
    {
        if (evalCase?.Packet is null)
        {
            return Rejected(evalCase?.Name ?? string.Empty, evalCase?.Packet?.PacketId ?? string.Empty, OutcomeMalformedPacket);
        }

        if (evalCase.Packet.Candidates is null || evalCase.Packet.Candidates.Count == 0)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedPacket);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in evalCase.Packet.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.SlotId) ||
                string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                !IsSupportedCategory(candidate.Category))
            {
                return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedPacket);
            }

            if (!keys.Add(CandidateKey(candidate.SlotId, candidate.CandidateId)))
            {
                return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedPacket);
            }
        }

        return null;
    }

    private static SanitizedAvailabilityPreviewEvalRow ToRow(
        AvailabilityPreviewEvalCase evalCase,
        AvailabilityPreviewResult? result)
    {
        if (result is null || result.Candidates is null)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedResult);
        }

        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomePacketMismatch);
        }

        var resultOutcome = result.Kind switch
        {
            AvailabilityPreviewResultKind.QuoteReady => OutcomeQuoteReady,
            AvailabilityPreviewResultKind.Unavailable => OutcomeUnavailable,
            AvailabilityPreviewResultKind.Unsupported => OutcomeUnsupportedResult,
            AvailabilityPreviewResultKind.Malformed => OutcomeMalformedResult,
            _ => OutcomeMalformedResult
        };

        if (resultOutcome is OutcomeUnsupportedResult or OutcomeMalformedResult)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, resultOutcome);
        }

        var validationOutcome = ValidateResultCandidates(evalCase.Packet, result);
        if (validationOutcome is not null)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, validationOutcome);
        }

        var expectedStatus = resultOutcome == OutcomeQuoteReady
            ? AvailabilityPreviewCandidateStatus.QuoteReady
            : AvailabilityPreviewCandidateStatus.Unavailable;
        if (result.Candidates.Any(candidate => candidate.Status != expectedStatus))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMalformedResult);
        }

        var resultByKey = result.Candidates.ToDictionary(
            candidate => CandidateKey(candidate.SlotId, candidate.CandidateId),
            StringComparer.Ordinal);
        var candidateRows = evalCase.Packet.Candidates
            .Select(candidate =>
            {
                var resultCandidate = resultByKey[CandidateKey(candidate.SlotId, candidate.CandidateId)];
                return new SanitizedAvailabilityPreviewCandidateRow(
                    candidate.SlotId,
                    candidate.CandidateId,
                    candidate.Category,
                    resultCandidate.Status);
            })
            .OrderBy(candidate => candidate.SlotId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        return new SanitizedAvailabilityPreviewEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            resultOutcome == OutcomeQuoteReady,
            resultOutcome,
            null,
            candidateRows,
            candidateRows.Length,
            candidateRows.Count(candidate => candidate.Status == AvailabilityPreviewCandidateStatus.QuoteReady),
            candidateRows.Count(candidate => candidate.Status == AvailabilityPreviewCandidateStatus.Unavailable),
            resultOutcome == OutcomeQuoteReady ? result.ResponseContentLength : null,
            resultOutcome == OutcomeQuoteReady ? result.Provider : null,
            resultOutcome == OutcomeQuoteReady ? result.Model : null,
            resultOutcome == OutcomeQuoteReady ? result.RequestId : null);
    }

    private static string? ValidateResultCandidates(
        AvailabilityPreviewPacket packet,
        AvailabilityPreviewResult result)
    {
        var packetByKey = packet.Candidates.ToDictionary(
            candidate => CandidateKey(candidate.SlotId, candidate.CandidateId),
            StringComparer.Ordinal);
        var resultKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var resultCandidate in result.Candidates)
        {
            if (resultCandidate is null ||
                string.IsNullOrWhiteSpace(resultCandidate.SlotId) ||
                string.IsNullOrWhiteSpace(resultCandidate.CandidateId))
            {
                return OutcomeCandidateMismatch;
            }

            if (!IsSupportedCategory(resultCandidate.Category))
            {
                return OutcomeUnsupportedCategory;
            }

            var key = CandidateKey(resultCandidate.SlotId, resultCandidate.CandidateId);
            if (!resultKeys.Add(key) ||
                !packetByKey.TryGetValue(key, out var packetCandidate) ||
                packetCandidate.Category != resultCandidate.Category)
            {
                return OutcomeCandidateMismatch;
            }
        }

        return resultKeys.SetEquals(packetByKey.Keys)
            ? null
            : OutcomeCandidateMismatch;
    }

    private static SanitizedAvailabilityPreviewEvalRow ExceptionRow(string name, string packetId, Exception exception) =>
        exception switch
        {
            TimeoutException => Rejected(name, packetId, OutcomeTimeout, "timeout"),
            ProviderUnavailableException => Rejected(name, packetId, OutcomeProviderUnavailable, "provider_error"),
            ProviderException => Rejected(name, packetId, OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(name, packetId, OutcomeError, "availability_preview_error")
        };

    private static SanitizedAvailabilityPreviewEvalRow Rejected(
        string name,
        string packetId,
        string outcomeCode,
        string? errorCode = null) =>
        new(
            name,
            packetId,
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

    private static string CandidateKey(string slotId, string candidateId) =>
        $"{slotId}\u001F{candidateId}";

    private static bool IsSupportedCategory(AvailabilityPreviewCategory category) =>
        Enum.IsDefined(category);
}
