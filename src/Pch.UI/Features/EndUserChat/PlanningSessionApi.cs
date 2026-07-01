using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pch.Harness;
using Pch.Providers.PlannerPrimitives;

namespace Pch.UI.Features.EndUserChat;

public sealed record PlanningSessionStartRequest(
    string? Prompt,
    string? SelectedModelRole);

public sealed record PlanningSessionAnswerRequest(
    string? PrimitiveInstanceId,
    IReadOnlyDictionary<string, string>? FieldValues);

public sealed record PlanningSessionApiResponse(
    string SessionId,
    string Status,
    PlanningSessionApiState State,
    EndUserValidatedTurnView? Turn,
    PrimitiveAnswerDto? LastAnswer,
    IReadOnlyList<PrimitiveAnswerDto> Answers,
    IReadOnlyList<PlanningSessionTraceEntry> Trace,
    IReadOnlyList<PrimitiveValidationError> ValidationErrors,
    string RawAbsenceState)
{
    public static PlanningSessionApiResponse Unknown(string sessionId) =>
        new(
            sessionId,
            "planning_session_unknown",
            PlanningSessionApiState.Blocked("PCH_UI_PLANNING_SESSION_UNKNOWN", "planning_session_unknown"),
            null,
            null,
            [],
            [],
            [new(sessionId, "planning_session_unknown", "Planning session was not found.")],
            EndUserChatService.RawAbsenceState);
}

public sealed record PlanningSessionProviderPrimitiveTrace(
    string PrimitiveId,
    string PrimitiveKind,
    string InstanceId,
    string RendererKey,
    string? FieldPath,
    string? MoodToken,
    string? MediaToken,
    int OptionCount,
    IReadOnlyList<string> OptionIds,
    IReadOnlyList<string> CandidateIds,
    IReadOnlyList<string> TaskRefs,
    IReadOnlyList<string> EvidenceRefs,
    IReadOnlyList<string> ToolContextRefs);

public sealed record PlanningSessionProviderTaskTrace(
    string TaskId,
    IReadOnlyList<string> PrimitiveRefs);

public sealed record PlanningSessionTraceEntry(
    string TraceId,
    string ProviderRequestState,
    string ProviderOutcome,
    string? Provider,
    string? Model,
    string? RequestId,
    string ValidatedTurnId,
    string ValidationCode,
    string PrimitiveHash,
    IReadOnlyList<string> PrimitiveInstanceIds,
    IReadOnlyList<string> TaskIds,
    IReadOnlyList<string> AnswerIds,
    IReadOnlyList<PlanningSessionProviderPrimitiveTrace> ProviderPrimitiveTraces,
    IReadOnlyList<PlanningSessionProviderTaskTrace> ProviderTaskTraces)
{
    public static PlanningSessionTraceEntry FromModelRun(
        PlannerModelRun model,
        EndUserValidatedTurnView turn,
        PrimitiveAnswerDto? answer)
    {
        var primitiveIds = turn.Primitives.Select(primitive => primitive.InstanceId).ToArray();
        var taskIds = turn.Tasks.Select(task => task.TaskId).ToArray();
        var answerIds = answer is null ? [] : answer.FieldValues.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        var hashInput = JsonSerializer.Serialize(new
        {
            turn.TurnId,
            turn.OutcomeCode,
            Primitives = turn.Primitives.Select(primitive => new
            {
                primitive.InstanceId,
                primitive.PrimitiveId,
                primitive.RendererKey,
                primitive.Title,
                primitive.Prompt,
                Fields = primitive.Fields.Select(field => new
                {
                    field.FieldId,
                    field.Label,
                    field.Value,
                    field.AllowedValues
                }),
                Candidates = primitive.Candidates.Select(candidate => new
                {
                    candidate.CandidateId,
                    candidate.Title,
                    candidate.Mood
                })
            })
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..16].ToLowerInvariant();

        return new(
            $"trace-{turn.TurnId}",
            model.ProviderRequestState,
            model.ProviderOutcome,
            model.Result?.Provider,
            model.Result?.Model,
            model.Result?.RequestId,
            turn.TurnId,
            turn.OutcomeCode,
            hash,
            primitiveIds,
            taskIds,
            answerIds,
            BuildProviderPrimitiveTraces(model.Result),
            BuildProviderTaskTraces(model.Result));
    }

    private static IReadOnlyList<PlanningSessionProviderPrimitiveTrace> BuildProviderPrimitiveTraces(PlannerModelResult? result) =>
        result?.Primitives
            .Where(primitive => primitive is not null)
            .Take(24)
            .Select(primitive => new PlanningSessionProviderPrimitiveTrace(
                SafeTraceId(primitive.PrimitiveId),
                SafeTraceId(primitive.PrimitiveKind),
                SafeTraceId(primitive.InstanceId),
                SafeTraceId(primitive.RendererKey),
                SafeTraceNullableId(primitive.FieldPath),
                SafeTraceNullableId(primitive.MoodToken),
                SafeTraceNullableId(primitive.MediaToken),
                primitive.Options.Count,
                SafeTraceIds(primitive.Options.Select(option => option.OptionId)),
                SafeTraceIds(primitive.CandidateIds),
                SafeTraceIds(primitive.TaskRefs),
                SafeTraceIds(primitive.EvidenceRefs),
                SafeTraceIds(primitive.ToolContextRefs)))
            .ToArray() ?? [];

    private static IReadOnlyList<PlanningSessionProviderTaskTrace> BuildProviderTaskTraces(PlannerModelResult? result) =>
        result?.Tasks
            .Where(task => task is not null)
            .Take(24)
            .Select(task => new PlanningSessionProviderTaskTrace(
                SafeTraceId(task.TaskId),
                SafeTraceIds(task.PrimitiveRefs)))
            .ToArray() ?? [];

    private static IReadOnlyList<string> SafeTraceIds(IEnumerable<string?> values) =>
        values
            .Select(SafeTraceNullableId)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToArray();

    private static string SafeTraceId(string? value) =>
        SafeTraceNullableId(value) ?? "redacted";

    private static string? SafeTraceNullableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || ContainsUnsafeTraceMarker(value))
        {
            return null;
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '_' and not '-' and not '/' and not ':' and not '.')
            {
                return null;
            }
        }

        return value;
    }

    private static bool ContainsUnsafeTraceMarker(string value)
    {
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
}

public sealed class PlanningSessionContext
{
    private readonly List<PrimitiveAnswerDto> _answers = [];
    private readonly List<PlanningSessionTraceEntry> _trace = [];

    public PlanningSessionContext(TripSession session)
    {
        HarnessContext = new Pch.Harness.PlanningSessionContext(session ?? throw new ArgumentNullException(nameof(session)));
    }

    public Pch.Harness.PlanningSessionContext HarnessContext { get; }

    public TripSession Session => HarnessContext.TripSession;

    public EndUserValidatedTurnView? LastTurn { get; set; }

    public IReadOnlyList<PrimitiveAnswerDto> Answers => _answers;

    public IReadOnlyList<PlanningSessionTraceEntry> Trace => _trace;

    public void RecordAnswer(PrimitiveAnswerDto answer) => _answers.Add(answer);

    public void AddTrace(PlanningSessionTraceEntry entry) => _trace.Add(entry);
}

public sealed record PlanningSessionApiState(
    string ModeState,
    string SelectedModelRole,
    string SelectedProvider,
    string LivePreflightState,
    string LiveProposalState,
    string HarnessValidationState,
    string LatestTurnSource,
    string ProviderRequestState,
    string ProviderOutcome,
    string ProviderHealth,
    string FinalState,
    string? ErrorCode,
    string? BlockedReason,
    IReadOnlyList<EndUserChatTurn> Turns,
    IReadOnlyList<EndUserTask> Tasks,
    IReadOnlyList<EndUserEvidenceItem> Evidence,
    IReadOnlyList<EndUserPlanningTimelineItem> PlanningTimeline)
{
    public static PlanningSessionApiState FromState(EndUserChatState state) =>
        new(
            state.ModeState,
            state.SelectedModelRole,
            state.SelectedProvider,
            state.LivePreflightState,
            state.LiveProposalState,
            state.HarnessValidationState,
            state.LatestTurnSource,
            state.ProviderRequestState,
            state.ProviderOutcome,
            state.ProviderHealth,
            state.FinalState,
            state.ErrorCode,
            state.BlockedReason,
            state.Turns,
            state.Tasks,
            state.Evidence,
            state.PlanningTimeline);

    public static PlanningSessionApiState Blocked(string errorCode, string blockedReason) =>
        new(
            "live-model-blocked",
            EndUserModelRoleSelection.InHarnessActionGenerator,
            "server",
            "not_run",
            "provider_blocked",
            "not_run",
            "provider_blocked",
            "not_attempted",
            blockedReason,
            "blocked",
            "validation_blocked",
            errorCode,
            blockedReason,
            [],
            [],
            [],
            []);
}

public sealed class PlanningSessionStore
{
    private readonly ConcurrentDictionary<string, StoredPlanningSession> _sessions = new(StringComparer.Ordinal);

    public async Task<PlanningSessionApiResponse> StartAsync(
        PlanningSessionService service,
        PlanningSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = $"planning-http-session-{Guid.NewGuid():N}";
        var context = service.CreateContext(sessionId);
        var result = await service
            .StartAsync(
                context,
                request.Prompt ?? string.Empty,
                EndUserModelRoleSelection.Normalize(request.SelectedModelRole),
                cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = new StoredPlanningSession(context, result.State, result.Turn, result.LastAnswer);
        return ToResponse(sessionId, "started", context, result);
    }

    public async Task<PlanningSessionApiResponse> AnswerAsync(
        PlanningSessionService service,
        string sessionId,
        PlanningSessionAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryGetValue(sessionId, out var stored) || stored.Turn is null)
        {
            return PlanningSessionApiResponse.Unknown(sessionId);
        }

        var primitiveInstanceId = string.IsNullOrWhiteSpace(request.PrimitiveInstanceId)
            ? stored.Turn.Primitives.FirstOrDefault(primitive => primitive.RendererKey == "form")?.InstanceId
            : request.PrimitiveInstanceId;
        if (string.IsNullOrWhiteSpace(primitiveInstanceId))
        {
            return PlanningSessionApiResponse.Unknown(sessionId);
        }

        var answer = new PrimitiveAnswerDto(
            stored.Turn.SessionId,
            stored.Turn.TurnId,
            stored.Turn.GraphRevision,
            primitiveInstanceId,
            request.FieldValues?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
        var result = await service
            .SubmitAnswer(stored.Context, stored.State, stored.Turn, answer, cancellationToken)
            .ConfigureAwait(false);

        _sessions[sessionId] = stored with { State = result.State, Turn = result.Turn, LastAnswer = result.LastAnswer };
        return ToResponse(sessionId, "answered", stored.Context, result);
    }

    public PlanningSessionApiResponse Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var stored)
            ? ToResponse(sessionId, "current", stored.Context, new(stored.State, stored.Turn, stored.LastAnswer, []))
            : PlanningSessionApiResponse.Unknown(sessionId);

    private static PlanningSessionApiResponse ToResponse(
        string sessionId,
        string status,
        PlanningSessionContext context,
        PlanningSessionUiResult result) =>
        new(
            sessionId,
            status,
            PlanningSessionApiState.FromState(result.State),
            SanitizeTurn(result.Turn),
            result.LastAnswer,
            context.Answers,
            context.Trace,
            result.ValidationErrors,
            EndUserChatService.RawAbsenceState);

    private static EndUserValidatedTurnView? SanitizeTurn(EndUserValidatedTurnView? turn) =>
        turn is null ? null : turn with { CanonicalTurn = null };

    private sealed record StoredPlanningSession(
        PlanningSessionContext Context,
        EndUserChatState State,
        EndUserValidatedTurnView? Turn,
        PrimitiveAnswerDto? LastAnswer);
}
