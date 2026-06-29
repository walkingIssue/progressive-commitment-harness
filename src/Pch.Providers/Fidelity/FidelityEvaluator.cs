using Pch.Providers.Errors;

namespace Pch.Providers.Fidelity;

public sealed class FidelityEvaluator
{
    public const string OutcomeAgreed = "fidelity_eval_agreed";
    public const string OutcomeDisagreement = "fidelity_eval_disagreement";
    public const string OutcomePacketIdMismatch = "fidelity_eval_packet_id_mismatch";
    public const string OutcomeSchemaInvalid = "fidelity_eval_schema_invalid";
    public const string OutcomeUnsupportedClaim = "fidelity_eval_unsupported_claim";
    public const string OutcomeMissingCandidateId = "fidelity_eval_missing_candidate_id";
    public const string OutcomeFallbackRequired = "fidelity_eval_fallback_required";
    public const string OutcomeSourceError = "fidelity_eval_source_error";

    private static readonly FidelityEvalSourceKind[] RequiredSourceKinds =
    [
        FidelityEvalSourceKind.SmallModel,
        FidelityEvalSourceKind.StrongModel,
        FidelityEvalSourceKind.HarnessOnly
    ];

    private static readonly HashSet<string> AllowedClaimCodes = new(StringComparer.Ordinal)
    {
        "schema_valid",
        "candidate_id_set_complete",
        "decision_allowlisted",
        "harness_rule_comparable"
    };

    private readonly IReadOnlyList<IFidelityEvalSource> _sources;

    public FidelityEvaluator(IReadOnlyList<IFidelityEvalSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!HasRequiredSources(sources))
        {
            throw new ArgumentException(
                "Fidelity evaluator requires one small-model, one strong-model, and one harness-only source.",
                nameof(sources));
        }

        _sources = sources
            .OrderBy(source => source.SourceKind)
            .ToArray();
    }

    public async Task<IReadOnlyList<SanitizedFidelityEvalRow>> EvaluateAsync(
        IReadOnlyList<FidelityEvalCase> cases,
        FidelityEvalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedFidelityEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            var malformedPacketRow = ValidateEvalCase(evalCase);
            if (malformedPacketRow is not null)
            {
                rows.Add(malformedPacketRow);
                continue;
            }

            var results = new List<FidelityEvalSourceResult>(_sources.Count);
            var blockedRow = default(SanitizedFidelityEvalRow);
            foreach (var source in _sources)
            {
                try
                {
                    var result = await source.EvaluateAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                    blockedRow = ValidateResult(evalCase, source.SourceKind, result);
                    if (blockedRow is not null)
                    {
                        break;
                    }

                    results.Add(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    blockedRow = Rejected(
                        evalCase.Name,
                        evalCase.Packet.PacketId,
                        OutcomeSourceError,
                        ToErrorCode(ex));
                    break;
                }
            }

            rows.Add(blockedRow ?? ToAcceptedRow(evalCase, results));
        }

        return rows;
    }

    private static SanitizedFidelityEvalRow? ValidateEvalCase(FidelityEvalCase? evalCase)
    {
        if (evalCase?.Packet is null)
        {
            return Rejected(evalCase?.Name ?? string.Empty, evalCase?.Packet?.PacketId ?? string.Empty, OutcomeSchemaInvalid);
        }

        if (evalCase.Packet.Candidates is null || evalCase.Packet.Candidates.Count == 0)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
        }

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in evalCase.Packet.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                !candidateIds.Add(candidate.CandidateId))
            {
                return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
            }
        }

        return null;
    }

    private static SanitizedFidelityEvalRow? ValidateResult(
        FidelityEvalCase evalCase,
        FidelityEvalSourceKind expectedSourceKind,
        FidelityEvalSourceResult? result)
    {
        if (result is null)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
        }

        if (result.SourceKind != expectedSourceKind)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
        }

        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomePacketIdMismatch);
        }

        if (result.Candidates is null || result.ClaimCodes is null ||
            result.Kind == FidelityEvalSourceResultKind.SchemaInvalid)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
        }

        if (result.Kind == FidelityEvalSourceResultKind.FallbackRequired)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeFallbackRequired);
        }

        if (result.Kind != FidelityEvalSourceResultKind.Completed)
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeSchemaInvalid);
        }

        if (result.ClaimCodes.Any(claim => !AllowedClaimCodes.Contains(claim)))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeUnsupportedClaim);
        }

        if (!CandidateIdSetMatches(evalCase.Packet.Candidates, result.Candidates))
        {
            return Rejected(evalCase.Name, evalCase.Packet.PacketId, OutcomeMissingCandidateId);
        }

        return null;
    }

    private static SanitizedFidelityEvalRow ToAcceptedRow(
        FidelityEvalCase evalCase,
        IReadOnlyList<FidelityEvalSourceResult> results)
    {
        var bySource = results.ToDictionary(result => result.SourceKind);
        var smallModel = bySource[FidelityEvalSourceKind.SmallModel].Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
        var strongModel = bySource[FidelityEvalSourceKind.StrongModel].Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
        var harnessOnly = bySource[FidelityEvalSourceKind.HarnessOnly].Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);

        var candidateRows = evalCase.Packet.Candidates
            .Select(candidate =>
            {
                var smallDecision = smallModel[candidate.CandidateId].Decision;
                var strongDecision = strongModel[candidate.CandidateId].Decision;
                var harnessDecision = harnessOnly[candidate.CandidateId].Decision;
                return new SanitizedFidelityCandidateComparisonRow(
                    candidate.CandidateId,
                    candidate.Category,
                    smallDecision,
                    strongDecision,
                    harnessDecision,
                    smallDecision == strongDecision && strongDecision == harnessDecision);
            })
            .OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        var agreementCount = candidateRows.Count(candidate => candidate.AllSourcesAgree);
        var disagreementCount = candidateRows.Length - agreementCount;
        var sourceRows = results
            .Select(result => new SanitizedFidelitySourceRow(
                result.SourceKind,
                result.Candidates.Count,
                result.Candidates.Count(candidate => candidate.Decision == FidelityCandidateDecision.Include),
                result.Candidates.Count(candidate => candidate.Decision == FidelityCandidateDecision.Exclude),
                result.Candidates.Count(candidate => candidate.Decision == FidelityCandidateDecision.Defer),
                result.ClaimCodes.Count(claim => !AllowedClaimCodes.Contains(claim)),
                result.ResponseContentLength,
                result.Provider,
                result.Model,
                result.RequestId))
            .OrderBy(source => source.SourceKind)
            .ToArray();

        return new SanitizedFidelityEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            disagreementCount == 0,
            disagreementCount == 0 ? OutcomeAgreed : OutcomeDisagreement,
            null,
            sourceRows,
            candidateRows,
            candidateRows.Length,
            agreementCount,
            disagreementCount);
    }

    private static bool CandidateIdSetMatches(
        IReadOnlyList<FidelityTrustedCandidate> trustedCandidates,
        IReadOnlyList<FidelityCandidateVerdict> resultCandidates)
    {
        var trustedIds = new HashSet<string>(
            trustedCandidates.Select(candidate => candidate.CandidateId),
            StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resultCandidate in resultCandidates)
        {
            if (string.IsNullOrWhiteSpace(resultCandidate.CandidateId) ||
                !trustedIds.Contains(resultCandidate.CandidateId) ||
                !resultIds.Add(resultCandidate.CandidateId))
            {
                return false;
            }
        }

        return resultIds.SetEquals(trustedIds);
    }

    private static bool HasRequiredSources(IReadOnlyList<IFidelityEvalSource> sources)
    {
        if (sources.Count != RequiredSourceKinds.Length)
        {
            return false;
        }

        var sourceKinds = sources.Select(source => source.SourceKind).ToHashSet();
        return RequiredSourceKinds.All(sourceKinds.Contains);
    }

    private static SanitizedFidelityEvalRow Rejected(
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
            [],
            0,
            0,
            0);

    private static string ToErrorCode(Exception exception) =>
        exception switch
        {
            TimeoutException => "timeout",
            ProviderException => "provider_error",
            _ => "fidelity_eval_error"
        };
}
