using System.Security.Cryptography;
using System.Text;
using Pch.Core;

namespace Pch.Harness;

public sealed class PlanningSessionContext
{
    private const int MaxItems = 16;

    private readonly List<PlannerAcceptedFact> _acceptedFacts = [];
    private readonly List<PlannerPrimitiveAnswer> _submittedAnswers = [];
    private readonly List<PlannerValidatedTurnSummary> _validatedTurns = [];
    private readonly List<PlannerToolContextReference> _toolContextReferences = [];

    private readonly PlannerToolManifestCompiler _manifestCompiler = new();

    public PlanningSessionContext(TripSession session)
    {
        TripSession = session ?? throw new ArgumentNullException(nameof(session));
    }

    public TripSession TripSession { get; }

    public IReadOnlyList<PlannerAcceptedFact> AcceptedFacts => _acceptedFacts;

    public IReadOnlyList<PlannerPrimitiveAnswer> SubmittedAnswers => _submittedAnswers;

    public IReadOnlyList<PlannerValidatedTurnSummary> ValidatedTurnSummaries => _validatedTurns;

    public IReadOnlyList<PlannerToolContextReference> ToolContextReferences => _toolContextReferences;

    public void RecordValidatedTurn(ValidatedTurnView view)
    {
        if (view is null || string.IsNullOrWhiteSpace(view.TurnId) || view.Primitives.Count == 0)
        {
            return;
        }

        _validatedTurns.Add(new PlannerValidatedTurnSummary(
            SafeId(view.TurnId),
            SafeId(view.Code),
            SafeId(view.PrimitiveHash),
            view.RenderedPrimitiveIds.Select(SafeId).Take(MaxItems).ToArray(),
            view.TaskRailItemRefs.Select(SafeId).Take(MaxItems).ToArray(),
            view.AnswerIds.Select(SafeId).Take(MaxItems).ToArray()));
        Trim(_validatedTurns);

        foreach (var reference in view.ToolContextReferences.Where(reference => !Unsafe(reference)).Take(MaxItems))
        {
            if (_toolContextReferences.Any(existing => string.Equals(existing.ReferenceId, reference, StringComparison.Ordinal)))
            {
                continue;
            }

            _toolContextReferences.Add(new PlannerToolContextReference(
                SafeId(reference),
                "validated_primitive_context",
                "Validated primitive context reference.",
                []));
        }

        Trim(_toolContextReferences);
    }

    public PlannerAnswerApplicationResult ApplyAnswers(PlannerAnswerApplicationRequest request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, TripSession.SessionId, StringComparison.Ordinal)
            || request.Answers is null
            || request.Answers.Count == 0)
        {
            return Blocked(PlannerPrimitiveValidator.AnswerSchemaInvalidCode, "Planner primitive answer failed validation.");
        }

        var currentRevision = _manifestCompiler.CurrentGraphRevision(TripSession);
        if (!string.Equals(request.GraphRevision, currentRevision, StringComparison.Ordinal))
        {
            return Blocked(PlannerPrimitiveValidator.StaleGraphRevisionCode, "Planner primitive answer references stale graph state.");
        }

        var latestTurn = _validatedTurns.LastOrDefault();
        var latestViewLookup = LatestValidatedPrimitiveLookup();
        if (latestTurn is null || latestViewLookup.Count == 0)
        {
            return Blocked(PlannerPrimitiveValidator.AnswerSchemaInvalidCode, "Planner primitive answer failed validation.");
        }

        var pending = new List<PlannerPrimitiveAnswer>();
        foreach (var answer in request.Answers)
        {
            if (answer is null
                || string.IsNullOrWhiteSpace(answer.AnswerId)
                || string.IsNullOrWhiteSpace(answer.PrimitiveInstanceId)
                || string.IsNullOrWhiteSpace(answer.PrimitiveId)
                || answer.Values is null
                || answer.SelectedOptionIds is null
                || answer.EvidenceReferences is null
                || Unsafe(answer.AnswerId)
                || Unsafe(answer.PrimitiveInstanceId)
                || Unsafe(answer.PrimitiveId)
                || answer.Values.Any(pair => Unsafe(pair.Key) || Unsafe(pair.Value))
                || answer.SelectedOptionIds.Any(Unsafe)
                || answer.EvidenceReferences.Any(Unsafe))
            {
                return Blocked(PlannerPrimitiveValidator.AnswerSchemaInvalidCode, "Planner primitive answer failed validation.");
            }

            if (!latestViewLookup.TryGetValue(answer.PrimitiveInstanceId, out var primitive)
                || !string.Equals(primitive.PrimitiveId, answer.PrimitiveId, StringComparison.Ordinal))
            {
                return Blocked(PlannerPrimitiveValidator.AnswerSchemaInvalidCode, "Planner primitive answer failed validation.");
            }

            var allowedOptionIds = primitive.Options.Select(option => option.OptionId).ToHashSet(StringComparer.Ordinal);
            if (allowedOptionIds.Count > 0 && answer.SelectedOptionIds.Any(optionId => !allowedOptionIds.Contains(optionId)))
            {
                return Blocked(PlannerPrimitiveValidator.AnswerValueNotAllowedCode, "Planner primitive answer value is not allowed.");
            }

            pending.Add(new PlannerPrimitiveAnswer(
                SafeId(answer.AnswerId),
                SafeId(answer.PrimitiveInstanceId),
                SafeId(answer.PrimitiveId),
                answer.Values.ToDictionary(pair => SafeId(pair.Key), pair => SafeNullableText(pair.Value), StringComparer.Ordinal),
                answer.SelectedOptionIds.Select(SafeId).Distinct(StringComparer.Ordinal).Take(MaxItems).ToArray(),
                answer.EvidenceReferences.Select(SafeId).Distinct(StringComparer.Ordinal).Take(MaxItems).ToArray()));
        }

        foreach (var answer in pending)
        {
            _submittedAnswers.Add(answer);
            AddAcceptedFactForAnswer(answer, latestViewLookup[answer.PrimitiveInstanceId]);
        }

        Trim(_submittedAnswers);
        Trim(_acceptedFacts);

        return new PlannerAnswerApplicationResult(
            IsAccepted: true,
            IsBlocked: false,
            Code: "primitive_answer_applied",
            Summary: "Planner primitive answer applied to planning context.",
            AppliedAnswers: pending);
    }

    private Dictionary<string, ValidatedPrimitiveView> LatestValidatedPrimitiveLookup()
    {
        var latestTurnId = _validatedTurns.LastOrDefault()?.TurnId;
        if (latestTurnId is null)
        {
            return [];
        }

        return _lastValidatedPrimitives
            .ToDictionary(primitive => primitive.InstanceId, StringComparer.Ordinal);
    }

    private readonly List<ValidatedPrimitiveView> _lastValidatedPrimitives = [];

    public void ReplaceLastValidatedPrimitives(IReadOnlyList<ValidatedPrimitiveView> primitives)
    {
        _lastValidatedPrimitives.Clear();
        _lastValidatedPrimitives.AddRange(primitives.Take(MaxItems));
    }

    private void AddAcceptedFactForAnswer(PlannerPrimitiveAnswer answer, ValidatedPrimitiveView primitive)
    {
        var value = answer.SelectedOptionIds.FirstOrDefault()
            ?? answer.Values.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "submitted";
        var fieldPath = string.IsNullOrWhiteSpace(primitive.FieldPath)
            ? $"/answers/{answer.PrimitiveInstanceId}"
            : primitive.FieldPath;

        _acceptedFacts.Add(new PlannerAcceptedFact(
            $"fact-{SafeId(answer.AnswerId)}",
            SafeId(fieldPath),
            SafeNullableText(value) ?? "redacted",
            answer.EvidenceReferences));
    }

    private static PlannerAnswerApplicationResult Blocked(string code, string summary)
    {
        return new(false, true, code, summary, []);
    }

    private static void Trim<T>(List<T> items)
    {
        if (items.Count <= MaxItems)
        {
            return;
        }

        items.RemoveRange(0, items.Count - MaxItems);
    }

    private static string SafeId(string? value)
    {
        var text = SafeNullableText(value) ?? "redacted";
        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string? SafeNullableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Unsafe(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160];
    }

    private static bool Unsafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PlannerTurnContextBuilder
{
    private const int MaxItems = 16;

    private readonly PlannerToolManifestCompiler _manifestCompiler = new();

    public PlannerTurnContext Build(PlanningSessionContext context, PlannerTurnContextRequest request)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var session = context.TripSession;
        var prompt = request?.TransientRawPrompt ?? string.Empty;
        var manifest = _manifestCompiler.Compile(session);
        var acceptedFacts = MissionFacts(session)
            .Concat(context.AcceptedFacts)
            .Take(MaxItems)
            .ToArray();

        return new PlannerTurnContext(
            ContextId: $"planner-context-{SafeId(session.SessionId)}-{manifest.GraphRevision[..12]}",
            SessionId: SafeId(session.SessionId),
            GraphRevision: manifest.GraphRevision,
            Stage: session.Stage.ToString(),
            PromptCategory: PromptCategory(prompt, request?.ScenarioHints ?? []),
            PromptLength: string.IsNullOrEmpty(prompt) ? 0 : prompt.Length,
            PromptSha256: Sha256(prompt),
            AcceptedFacts: acceptedFacts,
            SubmittedAnswers: context.SubmittedAnswers.Take(MaxItems).ToArray(),
            ValidatedTurnSummaries: context.ValidatedTurnSummaries.Take(MaxItems).ToArray(),
            ToolContextReferences: BuildToolContextReferences(session, context).Take(MaxItems).ToArray(),
            Manifest: manifest);
    }

    public void RecordValidatedTurn(PlanningSessionContext context, ValidatedTurnView view)
    {
        context.RecordValidatedTurn(view);
        context.ReplaceLastValidatedPrimitives(view.Primitives);
    }

    private static IEnumerable<PlannerAcceptedFact> MissionFacts(TripSession session)
    {
        yield return Fact("mission-purpose", "/mission/purpose", session.Mission.Purpose, ["evidence-user-purpose"]);
        yield return Fact("mission-destination", "/mission/destination_country", session.Mission.DestinationCountry, ["evidence-user-purpose"]);
        yield return Fact("mission-start", "/mission/start_date", session.Mission.StartDate.ToString("yyyy-MM-dd"), ["evidence-user-purpose"]);
        yield return Fact("mission-end", "/mission/end_date", session.Mission.EndDate.ToString("yyyy-MM-dd"), ["evidence-user-purpose"]);

        foreach (var constraint in session.Mission.Constraints.Take(4))
        {
            yield return Fact($"constraint-{constraint.ConstraintId}", $"/constraints/{constraint.ConstraintId}", constraint.Value, []);
        }
    }

    private static PlannerAcceptedFact Fact(string factId, string fieldPath, string value, IReadOnlyList<string> evidence)
    {
        return new(SafeId(factId), SafeId(fieldPath), SafeNullableText(value) ?? "redacted", evidence.Select(SafeId).ToArray());
    }

    private static IEnumerable<PlannerToolContextReference> BuildToolContextReferences(TripSession session, PlanningSessionContext context)
    {
        foreach (var reference in context.ToolContextReferences)
        {
            yield return reference;
        }

        foreach (var item in session.EvidenceTrace.Items.Take(8))
        {
            yield return new PlannerToolContextReference(
                SafeId(item.EvidenceId),
                item.Kind is EvidenceKind.UserStatement ? "user_provided_context" : "mock_context_provider",
                SafeNullableText(item.Summary) ?? "Context reference available.",
                [SafeId(item.EvidenceId)]);
        }
    }

    private static string PromptCategory(string prompt, IReadOnlyList<string> hints)
    {
        var lower = prompt.ToLowerInvariant();
        if (lower.Contains("business", StringComparison.Ordinal) || hints.Contains("business", StringComparer.OrdinalIgnoreCase))
        {
            return "business";
        }

        if (lower.Contains("family", StringComparison.Ordinal) || lower.Contains("funeral", StringComparison.Ordinal))
        {
            return "family_support";
        }

        if (lower.Contains("food", StringComparison.Ordinal) || lower.Contains("market", StringComparison.Ordinal))
        {
            return "food_first";
        }

        return string.IsNullOrWhiteSpace(prompt) ? "unknown" : "trip_planning";
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
    }

    private static string SafeId(string? value)
    {
        var text = SafeNullableText(value) ?? "redacted";
        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string? SafeNullableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Unsafe(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160];
    }

    private static bool Unsafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            || value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            || value.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }
}
