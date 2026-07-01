using System.Diagnostics;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LiveTurns;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.PlannerPrimitives;

public sealed class PlannerPrimitiveRunner
{
    public const string RejectedRowName = "planner_model_rejected";
    public const string RejectedRunId = "planner_model_run_redacted";
    public const string RejectedTurnId = "planner_model_turn_redacted";
    public const string RejectedManifestId = "planner_model_manifest_redacted";
    public const string RejectedManifestVersion = "manifest_version_redacted";

    public const string OutcomeAccepted = "planner_model_accepted";
    public const string OutcomeRepairedJson = "planner_model_repaired_json";
    public const string OutcomeToolSearch = "planner_model_tool_search_requested";
    public const string OutcomeToolGap = "planner_model_tool_gap_requested";
    public const string OutcomeKeyMissing = "planner_model_key_missing";
    public const string OutcomeCreditExhausted = "planner_model_credit_exhausted";
    public const string OutcomeRateLimited = "planner_model_rate_limited";
    public const string OutcomeTimeout = "planner_model_timeout";
    public const string OutcomeEmptyContent = "planner_model_empty_content";
    public const string OutcomeMalformedJson = "planner_model_malformed_json";
    public const string OutcomeSchemaInvalid = "planner_model_schema_invalid";
    public const string OutcomeUnsafeText = "planner_model_unsafe_text";
    public const string OutcomeUnsupportedPrimitive = "planner_model_unsupported_primitive";
    public const string OutcomeFieldPathNotAllowed = "planner_model_field_path_not_allowed";
    public const string OutcomeToolNotAllowed = "planner_model_tool_not_allowed";
    public const string OutcomeProviderUnavailable = "planner_model_provider_unavailable";
    public const string OutcomeFallbackDisabled = "planner_model_fallback_disabled";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public PlannerPrimitiveRunner(IModelCompletionClient completionClient, IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<PlannerModelResult> RunAsync(
        PlannerModelRequest request,
        PlannerModelOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        ValidateOptions(options);

        if (!options.Enabled)
        {
            throw new PlannerModelGuardException(OutcomeProviderUnavailable, "provider_disabled");
        }

        if (!options.ApiKeyAvailable)
        {
            throw new PlannerModelGuardException(OutcomeKeyMissing, "provider_key_missing");
        }

        if (options.AllowPaidProviderFallback)
        {
            throw new PlannerModelGuardException(OutcomeFallbackDisabled, "provider_fallback_disabled");
        }

        using var timeout = CreateTimeoutSource(options.Timeout, cancellationToken, out var operationToken);

        if (options.CreditGuardEnabled)
        {
            try
            {
                var credits = _creditClient is null
                    ? new ProviderCreditStatus(null, null, null, IsExhausted: true)
                    : await _creditClient.GetCreditStatusAsync(operationToken).ConfigureAwait(false);
                if (credits.IsExhausted)
                {
                    throw new ProviderCreditExhaustedException(options.Provider, "Planner primitive credits are exhausted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderUnavailableException(options.Provider, "Planner primitive request timed out.");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var first = await CompleteOnceAsync(request, options, isRepair: false, operationToken).ConfigureAwait(false);
        if (TryParse(request, first, stopwatch.Elapsed, wasRepaired: false, out var parsed, out var failure))
        {
            return parsed;
        }

        if (options.RepairEnabled &&
            failure is PlannerParseFailure.MalformedJson or PlannerParseFailure.SchemaInvalid)
        {
            var repaired = await CompleteOnceAsync(request, options, isRepair: true, operationToken).ConfigureAwait(false);
            if (TryParse(request, repaired, stopwatch.Elapsed, wasRepaired: true, out var repairedParsed, out var repairedFailure))
            {
                return repairedParsed;
            }

            failure = repairedFailure;
        }

        throw failure switch
        {
            PlannerParseFailure.MalformedJson => new ProviderMalformedResponseException(options.Provider, "Planner primitive provider returned malformed JSON."),
            _ => new ProviderMalformedResponseException(options.Provider, "Planner primitive provider returned malformed schema.")
        };
    }

    private async Task<ModelCompletionResponse> CompleteOnceAsync(
        PlannerModelRequest request,
        PlannerModelOptions options,
        bool isRepair,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for provider-local planner primitives. Use only primitive ids from the manifest. Do not include raw provider payloads, secrets, credentials, approval tokens, hold refs, booking refs, payment data, arbitrary URLs, CSS, or HTML."),
                        new ModelMessage(ModelMessageRole.User, PlannerPrimitivePromptBuilder.Build(request, isRepair, failureReason: isRepair ? "schema_or_json_invalid" : null))
                    ],
                    options.Model,
                    "planner_primitive_output",
                    PlannerPrimitiveJsonSchema.Schema,
                    Temperature: 0,
                    MaxTokens: options.MaxTokens),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderUnavailableException(options.Provider, "Planner primitive request timed out.");
        }
    }

    private static bool TryParse(
        PlannerModelRequest request,
        ModelCompletionResponse completion,
        TimeSpan duration,
        bool wasRepaired,
        out PlannerModelResult result,
        out PlannerParseFailure failure)
    {
        result = null!;
        failure = PlannerParseFailure.SchemaInvalid;
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Planner primitive provider returned empty content.");
        }

        PlannerModelEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlannerModelEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException)
        {
            failure = PlannerParseFailure.MalformedJson;
            return false;
        }

        if (parsed is null ||
            string.IsNullOrWhiteSpace(parsed.ManifestId) ||
            string.IsNullOrWhiteSpace(parsed.ManifestVersion) ||
            string.IsNullOrWhiteSpace(parsed.GraphRevision) ||
            string.IsNullOrWhiteSpace(parsed.SessionId) ||
            string.IsNullOrWhiteSpace(parsed.OutputKind) ||
            parsed.Primitives is null)
        {
            failure = PlannerParseFailure.SchemaInvalid;
            return false;
        }

        result = new PlannerModelResult(
            parsed.ManifestId,
            parsed.ManifestVersion,
            parsed.GraphRevision,
            parsed.SessionId,
            ParseOutputKind(parsed.OutputKind),
            parsed.Primitives.Select(ToPrimitive).ToArray(),
            parsed.Tasks?.Select(ToTask).ToArray() ?? [],
            wasRepaired,
            HasUnsafeValues(parsed),
            HasPromptSpecificContent(request, parsed),
            duration,
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
        failure = PlannerParseFailure.None;
        return true;
    }

    private static void ValidateRequest(PlannerModelRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.RunId) ||
            string.IsNullOrWhiteSpace(request.TurnId) ||
            request.Manifest is null ||
            string.IsNullOrWhiteSpace(request.Manifest.ManifestId) ||
            string.IsNullOrWhiteSpace(request.Manifest.ManifestVersion) ||
            string.IsNullOrWhiteSpace(request.Manifest.GraphRevision) ||
            string.IsNullOrWhiteSpace(request.Manifest.SessionId) ||
            string.IsNullOrWhiteSpace(request.Manifest.Stage) ||
            request.Manifest.AllowedPrimitives is null ||
            request.Manifest.AllowedPrimitives.Count == 0 ||
            request.Manifest.AllowedPrimitives.Any(primitive =>
                primitive is null ||
                string.IsNullOrWhiteSpace(primitive.PrimitiveId) ||
                string.IsNullOrWhiteSpace(primitive.PrimitiveKind) ||
                string.IsNullOrWhiteSpace(primitive.RendererKey)) ||
            request.Manifest.MaxPrimitiveCount <= 0 ||
            request.SubmittedAnswers.Any(answer =>
                answer is null ||
                string.IsNullOrWhiteSpace(answer.AnswerId) ||
                string.IsNullOrWhiteSpace(answer.FieldPath)) ||
            request.ContextToolResults.Any(result =>
                result is null ||
                string.IsNullOrWhiteSpace(result.ToolId) ||
                string.IsNullOrWhiteSpace(result.ResultId) ||
                string.IsNullOrWhiteSpace(result.Category) ||
                string.IsNullOrWhiteSpace(result.SourceClass)))
        {
            throw new ProviderMalformedResponseException("planner-primitive", "Planner primitive request schema is malformed.");
        }
    }

    private static void ValidateOptions(PlannerModelOptions options)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ProviderMalformedResponseException("planner-primitive", "Planner primitive options schema is malformed.");
        }
    }

    private static PlannerPrimitiveInvocation ToPrimitive(PlannerPrimitiveEnvelope primitive) =>
        new(
            primitive.PrimitiveId ?? string.Empty,
            primitive.PrimitiveKind ?? string.Empty,
            primitive.InstanceId ?? string.Empty,
            primitive.RendererKey ?? string.Empty,
            primitive.FieldPath,
            primitive.MoodToken,
            primitive.MediaToken,
            primitive.CandidateIds ?? [],
            primitive.TaskRefs ?? [],
            primitive.EvidenceRefs ?? [],
            primitive.ToolContextRefs ?? [],
            primitive.Options?.Select(ToOption).ToArray() ?? [],
            primitive.Label,
            primitive.PromptText,
            primitive.HelpText,
            primitive.DefaultValue,
            primitive.RendererHints ?? new Dictionary<string, string>());

    private static PlannerPrimitiveOption ToOption(PlannerPrimitiveOptionEnvelope option) =>
        new(
            option.OptionId ?? string.Empty,
            option.MoodToken,
            option.MediaToken,
            option.ToolContextRefs ?? [],
            option.Label,
            option.Summary);

    private static PlannerTaskInvocation ToTask(PlannerTaskEnvelope task) =>
        new(
            task.TaskId ?? string.Empty,
            task.PrimitiveRefs ?? [],
            task.Title,
            task.Summary);

    internal static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 120 &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '/' or ':' or '.') &&
        !ContainsUnsafeMarker(value);

    internal static bool ContainsUnsafeMarker(string? value) =>
        ContainsPlannerUnsafeMarker(value);

    private static bool ContainsPlannerUnsafeMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var upper = value.ToUpperInvariant();
        return upper.Contains("RAW_", StringComparison.Ordinal) ||
            upper.Contains("SHOULD_NOT_PERSIST", StringComparison.Ordinal) ||
            upper.Contains("SECRET", StringComparison.Ordinal) ||
            upper.Contains("SENTINEL", StringComparison.Ordinal) ||
            upper.Contains("CREDENTIAL", StringComparison.Ordinal) ||
            upper.Contains("API_KEY", StringComparison.Ordinal) ||
            upper.Contains("PAYLOAD", StringComparison.Ordinal) ||
            upper.Contains("COMPLETION", StringComparison.Ordinal) ||
            upper.Contains("APPROVAL", StringComparison.Ordinal) ||
            upper.Contains("HOLD", StringComparison.Ordinal) ||
            upper.Contains("BOOKING", StringComparison.Ordinal) ||
            upper.Contains("PAYMENT", StringComparison.Ordinal) ||
            upper.Contains("CANDIDATE_DISPLAY", StringComparison.Ordinal) ||
            upper.StartsWith("SK-", StringComparison.Ordinal);
    }

    private static bool HasUnsafeValues(PlannerModelEnvelope parsed) =>
        ContainsUnsafeMarker(parsed.ManifestId) ||
        ContainsUnsafeMarker(parsed.ManifestVersion) ||
        ContainsUnsafeMarker(parsed.GraphRevision) ||
        ContainsUnsafeMarker(parsed.SessionId) ||
        ContainsUnsafeMarker(parsed.OutputKind) ||
        parsed.Primitives.Any(primitive =>
            ContainsUnsafeMarker(primitive.PrimitiveId) ||
            ContainsUnsafeMarker(primitive.PrimitiveKind) ||
            ContainsUnsafeMarker(primitive.InstanceId) ||
            ContainsUnsafeMarker(primitive.RendererKey) ||
            ContainsUnsafeMarker(primitive.FieldPath) ||
            ContainsUnsafeMarker(primitive.MoodToken) ||
            ContainsUnsafeMarker(primitive.MediaToken) ||
            ContainsUnsafeMarker(primitive.Label) ||
            ContainsUnsafeMarker(primitive.PromptText) ||
            ContainsUnsafeMarker(primitive.HelpText) ||
            ContainsUnsafeMarker(primitive.DefaultValue) ||
            primitive.CandidateIds?.Any(id => !IsSafeIdentifier(id)) == true ||
            primitive.TaskRefs?.Any(id => !IsSafeIdentifier(id)) == true ||
            primitive.EvidenceRefs?.Any(id => !IsSafeIdentifier(id)) == true ||
            primitive.ToolContextRefs?.Any(id => !IsSafeIdentifier(id)) == true ||
            primitive.RendererHints?.Any(hint => !IsSafeIdentifier(hint.Key) || ContainsUnsafeMarker(hint.Value)) == true ||
            primitive.Options?.Any(option =>
                !IsSafeIdentifier(option.OptionId) ||
                ContainsUnsafeMarker(option.MoodToken) ||
                ContainsUnsafeMarker(option.MediaToken) ||
                ContainsUnsafeMarker(option.Label) ||
                ContainsUnsafeMarker(option.Summary) ||
                option.ToolContextRefs?.Any(id => !IsSafeIdentifier(id)) == true) == true) ||
        parsed.Tasks?.Any(task =>
            !IsSafeIdentifier(task.TaskId) ||
            ContainsUnsafeMarker(task.Title) ||
            ContainsUnsafeMarker(task.Summary) ||
            task.PrimitiveRefs?.Any(id => !IsSafeIdentifier(id)) == true) == true;

    private static bool HasPromptSpecificContent(PlannerModelRequest request, PlannerModelEnvelope parsed)
    {
        var tokens = PromptSpecificTokens(request.RuntimePrompt);
        if (tokens.Count == 0)
        {
            return true;
        }

        var text = string.Join(' ', parsed.Primitives.SelectMany(primitive => new[]
            {
                primitive.PrimitiveId,
                primitive.FieldPath,
                primitive.MoodToken,
                primitive.MediaToken,
                primitive.Label,
                primitive.PromptText,
                primitive.HelpText,
                primitive.DefaultValue
            })
            .Concat(parsed.Primitives.SelectMany(primitive => primitive.Options ?? [])
                .SelectMany(option => new[]
                {
                    option.OptionId,
                    option.MoodToken,
                    option.MediaToken,
                    option.Label,
                    option.Summary
                }))
            .Concat((parsed.Tasks ?? []).SelectMany(task => new[] { task.TaskId, task.Title, task.Summary }))
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> PromptSpecificTokens(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return [];
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "plan", "trip", "with", "from", "that", "this", "into", "make", "focus", "focused", "early", "late", "nights", "option", "options"
        };
        var tokens = new List<string>();
        foreach (var token in prompt.Split(
            [' ', ',', '.', ';', ':', '!', '?', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 4 &&
                !stopWords.Contains(token) &&
                !ContainsUnsafeMarker(token) &&
                token.All(char.IsLetterOrDigit))
            {
                tokens.Add(token);
            }
        }

        return tokens.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
    }

    internal static PlannerModelOutputKind ParseOutputKind(string? outputKind) =>
        outputKind switch
        {
            "composite_form" => PlannerModelOutputKind.CompositeForm,
            "tool_search_request" => PlannerModelOutputKind.ToolSearchRequest,
            "tool_gap_request" => PlannerModelOutputKind.ToolGapRequest,
            _ => (PlannerModelOutputKind)999
        };

    private static CancellationTokenSource? CreateTimeoutSource(
        TimeSpan? timeout,
        CancellationToken callerToken,
        out CancellationToken operationToken)
    {
        if (timeout is null || timeout.Value <= TimeSpan.Zero)
        {
            operationToken = callerToken;
            return null;
        }

        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource.CancelAfter(timeout.Value);
        operationToken = timeoutSource.Token;
        return timeoutSource;
    }

    private sealed record PlannerModelEnvelope(
        string? ManifestId,
        string? ManifestVersion,
        string? GraphRevision,
        string? SessionId,
        string? OutputKind,
        IReadOnlyList<PlannerPrimitiveEnvelope> Primitives,
        IReadOnlyList<PlannerTaskEnvelope>? Tasks);

    private sealed record PlannerPrimitiveEnvelope(
        string? PrimitiveId,
        string? PrimitiveKind,
        string? InstanceId,
        string? RendererKey,
        string? FieldPath,
        string? MoodToken,
        string? MediaToken,
        IReadOnlyList<string>? CandidateIds,
        IReadOnlyList<string>? TaskRefs,
        IReadOnlyList<string>? EvidenceRefs,
        IReadOnlyList<string>? ToolContextRefs,
        IReadOnlyList<PlannerPrimitiveOptionEnvelope>? Options,
        string? Label,
        string? PromptText,
        string? HelpText,
        string? DefaultValue,
        IReadOnlyDictionary<string, string>? RendererHints);

    private sealed record PlannerPrimitiveOptionEnvelope(
        string? OptionId,
        string? MoodToken,
        string? MediaToken,
        IReadOnlyList<string>? ToolContextRefs,
        string? Label,
        string? Summary);

    private sealed record PlannerTaskEnvelope(
        string? TaskId,
        IReadOnlyList<string>? PrimitiveRefs,
        string? Title,
        string? Summary);

    private enum PlannerParseFailure
    {
        None,
        MalformedJson,
        SchemaInvalid
    }
}

public sealed class PlannerModelGuardException : ProviderException
{
    public PlannerModelGuardException(string outcomeCode, string failureClassCode)
        : base("planner-primitive", "Planner model guard blocked execution.")
    {
        OutcomeCode = outcomeCode;
        FailureClassCode = failureClassCode;
    }

    public string OutcomeCode { get; }

    public string FailureClassCode { get; }
}
