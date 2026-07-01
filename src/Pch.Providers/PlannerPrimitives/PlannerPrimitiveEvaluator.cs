using Pch.Providers.Errors;
using Pch.Providers.LiveTurns;

namespace Pch.Providers.PlannerPrimitives;

public sealed class PlannerPrimitiveEvaluator
{
    private readonly PlannerPrimitiveRunner _runner;

    public PlannerPrimitiveEvaluator(PlannerPrimitiveRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedPlannerModelLogRow>> EvaluateAsync(
        IReadOnlyList<PlannerModelEvalCase?>? cases,
        PlannerModelOptions options,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid")];
        }

        var rows = new List<SanitizedPlannerModelLogRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            if (evalCase?.Request is null)
            {
                rows.Add(Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid"));
                continue;
            }

            try
            {
                var result = await _runner.RunAsync(evalCase.Request, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedPlannerModelLogRow ToRow(PlannerModelEvalCase evalCase, PlannerModelResult result)
    {
        if (!IsSafeRequest(evalCase.Request) ||
            !string.Equals(evalCase.Request.Manifest.ManifestId, result.ManifestId, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Request.Manifest.ManifestVersion, result.ManifestVersion, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Request.Manifest.GraphRevision, result.GraphRevision, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Request.Manifest.SessionId, result.SessionId, StringComparison.Ordinal))
        {
            return Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid");
        }

        if (!Enum.IsDefined(result.OutputKind) ||
            result.Primitives is null ||
            result.Primitives.Count == 0 ||
            result.Primitives.Count > evalCase.Request.Manifest.MaxPrimitiveCount)
        {
            return Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid");
        }

        if (result.HasUnsafeValue)
        {
            return Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid");
        }

        var allowed = evalCase.Request.Manifest.AllowedPrimitives.ToDictionary(
            primitive => primitive.PrimitiveId,
            StringComparer.Ordinal);
        var allowedFieldPaths = evalCase.Request.Manifest.AllowedFieldPaths.ToHashSet(StringComparer.Ordinal);
        var allowedMoodTokens = evalCase.Request.Manifest.AllowedMoodTokens.ToHashSet(StringComparer.Ordinal);
        var primitiveIds = new List<string>(result.Primitives.Count);
        foreach (var primitive in result.Primitives)
        {
            if (primitive is null ||
                !allowed.TryGetValue(primitive.PrimitiveId, out var definition) ||
                !string.Equals(definition.PrimitiveKind, primitive.PrimitiveKind, StringComparison.Ordinal) ||
                !string.Equals(definition.RendererKey, primitive.RendererKey, StringComparison.Ordinal) ||
                !PlannerPrimitiveRunner.IsSafeIdentifier(primitive.InstanceId) ||
                (!string.IsNullOrWhiteSpace(primitive.FieldPath) && !allowedFieldPaths.Contains(primitive.FieldPath)) ||
                (primitive.MoodToken is not null && !allowedMoodTokens.Contains(primitive.MoodToken)) ||
                primitive.CandidateIds.Any(id => !PlannerPrimitiveRunner.IsSafeIdentifier(id)))
            {
                return Rejected(PlannerPrimitiveRunner.OutcomeUnsupportedPrimitive, "unsupported_primitive");
            }

            primitiveIds.Add(definition.PrimitiveId);
        }

        return Accepted(evalCase, result, primitiveIds.Order(StringComparer.Ordinal).ToArray());
    }

    private static SanitizedPlannerModelLogRow Accepted(
        PlannerModelEvalCase evalCase,
        PlannerModelResult result,
        IReadOnlyList<string> primitiveIds)
    {
        var outcome = result.OutputKind switch
        {
            PlannerModelOutputKind.ToolSearchRequest => PlannerPrimitiveRunner.OutcomeToolSearch,
            PlannerModelOutputKind.ToolGapRequest => PlannerPrimitiveRunner.OutcomeToolGap,
            _ when result.WasRepaired => PlannerPrimitiveRunner.OutcomeRepairedJson,
            _ => PlannerPrimitiveRunner.OutcomeAccepted
        };

        return new SanitizedPlannerModelLogRow(
            evalCase.Name,
            evalCase.Request.RunId,
            evalCase.Request.TurnId,
            evalCase.Request.Manifest.ManifestId,
            evalCase.Request.Manifest.ManifestVersion,
            true,
            outcome,
            null,
            result.OutputKind,
            primitiveIds,
            primitiveIds.Count,
            result.WasRepaired,
            (int)Math.Min(result.Duration.TotalMilliseconds, int.MaxValue),
            DurationBucket(result.Duration),
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static bool IsSafeRequest(PlannerModelRequest request) =>
        PlannerPrimitiveRunner.IsSafeIdentifier(request.RunId) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.TurnId) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.Manifest.ManifestId) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.Manifest.ManifestVersion) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.Manifest.GraphRevision) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.Manifest.SessionId) &&
        PlannerPrimitiveRunner.IsSafeIdentifier(request.Manifest.Stage) &&
        request.Manifest.AllowedPrimitives.All(primitive =>
            PlannerPrimitiveRunner.IsSafeIdentifier(primitive.PrimitiveId) &&
            PlannerPrimitiveRunner.IsSafeIdentifier(primitive.PrimitiveKind) &&
            PlannerPrimitiveRunner.IsSafeIdentifier(primitive.RendererKey)) &&
        request.Manifest.AllowedFieldPaths.All(PlannerPrimitiveRunner.IsSafeIdentifier) &&
        request.Manifest.AllowedMoodTokens.All(PlannerPrimitiveRunner.IsSafeIdentifier);

    private static SanitizedPlannerModelLogRow ExceptionRow(Exception exception)
    {
        if (exception is PlannerModelGuardException guard)
        {
            return Rejected(guard.OutcomeCode, guard.FailureClassCode);
        }

        var failureClass = ProviderFailureClassifier.Classify(exception);
        return failureClass switch
        {
            ProviderFailureClass.ProviderCreditExhausted => Rejected(PlannerPrimitiveRunner.OutcomeCreditExhausted, "provider_credit_exhausted"),
            ProviderFailureClass.ProviderRateLimited => Rejected(PlannerPrimitiveRunner.OutcomeRateLimited, "provider_rate_limited"),
            ProviderFailureClass.ProviderTimeout => Rejected(PlannerPrimitiveRunner.OutcomeTimeout, "provider_timeout"),
            ProviderFailureClass.ProviderEmptyContent => Rejected(PlannerPrimitiveRunner.OutcomeEmptyContent, "provider_empty_content"),
            ProviderFailureClass.ProviderMalformedJson => Rejected(PlannerPrimitiveRunner.OutcomeMalformedJson, "provider_malformed_json"),
            ProviderFailureClass.ProviderSchemaInvalid => Rejected(PlannerPrimitiveRunner.OutcomeSchemaInvalid, "provider_schema_invalid"),
            _ => Rejected(PlannerPrimitiveRunner.OutcomeProviderUnavailable, ProviderFailureClassifier.CodeFor(failureClass))
        };
    }

    public static SanitizedPlannerModelLogRow Rejected(string outcomeCode, string? failureClassCode) =>
        new(
            PlannerPrimitiveRunner.RejectedRowName,
            PlannerPrimitiveRunner.RejectedRunId,
            PlannerPrimitiveRunner.RejectedTurnId,
            PlannerPrimitiveRunner.RejectedManifestId,
            PlannerPrimitiveRunner.RejectedManifestVersion,
            false,
            outcomeCode,
            failureClassCode,
            null,
            [],
            0,
            false,
            null,
            null,
            null,
            null,
            null,
            null);

    private static string DurationBucket(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1_000)
        {
            return "lt_1s";
        }

        if (duration.TotalMilliseconds < 5_000)
        {
            return "lt_5s";
        }

        if (duration.TotalMilliseconds < 15_000)
        {
            return "lt_15s";
        }

        return "gte_15s";
    }
}
