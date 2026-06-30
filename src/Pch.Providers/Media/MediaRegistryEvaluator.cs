using Pch.Providers.Errors;

namespace Pch.Providers.Media;

public sealed class MediaRegistryEvaluator
{
    public const string RejectedRowName = "media_registry_rejected";
    public const string RejectedRowPacketId = "media_registry_packet_redacted";
    public const string OutcomeAccepted = "media_registry_accepted";
    public const string OutcomePacketMismatch = "media_registry_packet_mismatch";
    public const string OutcomeCandidateMismatch = "media_registry_candidate_mismatch";
    public const string OutcomeMalformedPacket = "media_registry_malformed_packet";
    public const string OutcomeMalformedResult = "media_registry_malformed_result";
    public const string OutcomeUnsupportedSource = "media_registry_unsupported_source";
    public const string OutcomeUnsupportedLicense = "media_registry_unsupported_license";
    public const string OutcomeTimeout = "media_registry_timeout";
    public const string OutcomeProviderUnavailable = "media_registry_provider_unavailable";
    public const string OutcomeError = "media_registry_error";

    private readonly IMediaRegistrySource _source;

    public MediaRegistryEvaluator(IMediaRegistrySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public async Task<IReadOnlyList<SanitizedMediaRegistryEvalRow>> EvaluateAsync(
        IReadOnlyList<MediaRegistryEvalCase?>? cases,
        MediaRegistryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(OutcomeMalformedPacket)];
        }

        var rows = new List<SanitizedMediaRegistryEvalRow>(cases.Count);
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
                var result = await _source.ResolveAsync(evalCase!.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedMediaRegistryEvalRow? ValidateEvalCase(MediaRegistryEvalCase? evalCase)
    {
        if (evalCase?.Packet is null ||
            string.IsNullOrWhiteSpace(evalCase.Packet.PacketId) ||
            evalCase.Packet.Candidates is null ||
            evalCase.Packet.Candidates.Count == 0)
        {
            return Rejected(OutcomeMalformedPacket);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in evalCase.Packet.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.SlotId) ||
                string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                !IsSupportedCategory(candidate.Category) ||
                !keys.Add(CandidateKey(candidate.SlotId, candidate.CandidateId)))
            {
                return Rejected(OutcomeMalformedPacket);
            }
        }

        return null;
    }

    private static SanitizedMediaRegistryEvalRow ToRow(
        MediaRegistryEvalCase evalCase,
        MediaRegistryResult? result)
    {
        if (result?.CandidateMedia is null)
        {
            return Rejected(OutcomeMalformedResult);
        }

        if (!string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal))
        {
            return Rejected(OutcomePacketMismatch);
        }

        var validationOutcome = ValidateResultMappings(evalCase.Packet, result);
        if (validationOutcome is not null)
        {
            return Rejected(validationOutcome);
        }

        var resultByKey = result.CandidateMedia.ToDictionary(
            mapping => CandidateKey(mapping.SlotId, mapping.CandidateId),
            StringComparer.Ordinal);
        var candidateRows = evalCase.Packet.Candidates
            .Select(candidate =>
            {
                var resultMapping = resultByKey[CandidateKey(candidate.SlotId, candidate.CandidateId)];
                var assetRows = resultMapping.Assets
                    .Select(asset => new SanitizedMediaAssetRow(
                        asset.MediaId,
                        asset.Source.SourceClass,
                        asset.Source.SourceId,
                        asset.Source.ProviderName,
                        asset.License.LicenseClass,
                        asset.License.LicenseName,
                        asset.Attribution.AuthorName,
                        asset.Attribution.AuthorUrl,
                        asset.Attribution.AttributionText,
                        asset.Width,
                        asset.Height))
                    .OrderBy(asset => asset.MediaId, StringComparer.Ordinal)
                    .ToArray();

                return new SanitizedCandidateMediaRow(
                    candidate.SlotId,
                    candidate.CandidateId,
                    candidate.Category,
                    assetRows);
            })
            .OrderBy(candidate => candidate.SlotId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        return new SanitizedMediaRegistryEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            true,
            OutcomeAccepted,
            null,
            candidateRows,
            candidateRows.Length,
            candidateRows.Sum(candidate => candidate.Assets.Count),
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static string? ValidateResultMappings(
        MediaRegistryPacket packet,
        MediaRegistryResult result)
    {
        var packetByKey = packet.Candidates.ToDictionary(
            candidate => CandidateKey(candidate.SlotId, candidate.CandidateId),
            StringComparer.Ordinal);
        var resultKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in result.CandidateMedia)
        {
            if (mapping is null ||
                string.IsNullOrWhiteSpace(mapping.SlotId) ||
                string.IsNullOrWhiteSpace(mapping.CandidateId) ||
                mapping.Assets is null)
            {
                return OutcomeCandidateMismatch;
            }

            var key = CandidateKey(mapping.SlotId, mapping.CandidateId);
            if (!resultKeys.Add(key) ||
                !packetByKey.TryGetValue(key, out var packetCandidate) ||
                packetCandidate.Category != mapping.Category)
            {
                return OutcomeCandidateMismatch;
            }

            foreach (var asset in mapping.Assets)
            {
                if (asset is null ||
                    string.IsNullOrWhiteSpace(asset.MediaId) ||
                    asset.Source is null ||
                    asset.License is null ||
                    asset.Attribution is null ||
                    asset.Width <= 0 ||
                    asset.Height <= 0)
                {
                    return OutcomeMalformedResult;
                }

                if (!IsSupportedSource(asset.Source.SourceClass) ||
                    string.IsNullOrWhiteSpace(asset.Source.SourceId) ||
                    string.IsNullOrWhiteSpace(asset.Source.ProviderName))
                {
                    return OutcomeUnsupportedSource;
                }

                if (!IsSupportedLicense(asset.License.LicenseClass) ||
                    asset.License.LicenseClass == MediaLicenseClass.Unknown ||
                    string.IsNullOrWhiteSpace(asset.License.LicenseName))
                {
                    return OutcomeUnsupportedLicense;
                }
            }
        }

        return resultKeys.SetEquals(packetByKey.Keys)
            ? null
            : OutcomeCandidateMismatch;
    }

    private static SanitizedMediaRegistryEvalRow ExceptionRow(Exception exception) =>
        exception switch
        {
            TimeoutException => Rejected(OutcomeTimeout, "timeout"),
            ProviderUnavailableException => Rejected(OutcomeProviderUnavailable, "provider_error"),
            ProviderException => Rejected(OutcomeProviderUnavailable, "provider_error"),
            _ => Rejected(OutcomeError, "media_registry_error")
        };

    private static SanitizedMediaRegistryEvalRow Rejected(
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
            null,
            null,
            null,
            null);

    private static string CandidateKey(string slotId, string candidateId) =>
        $"{slotId}\u001F{candidateId}";

    private static bool IsSupportedCategory(MediaCandidateCategory category) =>
        Enum.IsDefined(category);

    private static bool IsSupportedSource(MediaSourceClass sourceClass) =>
        Enum.IsDefined(sourceClass);

    private static bool IsSupportedLicense(MediaLicenseClass licenseClass) =>
        Enum.IsDefined(licenseClass);
}
