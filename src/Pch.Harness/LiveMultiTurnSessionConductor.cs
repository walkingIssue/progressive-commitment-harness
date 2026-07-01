using Pch.Core;

namespace Pch.Harness;

public sealed class LiveMultiTurnSessionConductor
{
    public const string AwaitingModelProposalCode = "awaiting_model_proposal";
    public const string ConfirmationAppliedCode = "confirmation_applied";
    public const string NoMutationCode = "no_mutation";

    public const string InitialPromptOperation = "initial_prompt";
    public const string ApplyModelProposalOperation = "apply_model_proposal";
    public const string SubmitConfirmationOperation = "submit_confirmation";
    public const string SelectOptionOperation = "select_option";
    public const string DeferOptionOperation = "defer_option";
    public const string PreviewAvailabilityOperation = "preview_availability";
    public const string RecordProviderBlockedOperation = "record_provider_model_blocked";
    public const string BuildFreshModelInputOperation = "build_fresh_model_input";

    private const int MaxEvidenceRefs = 8;
    private const int MaxTimelineRefs = 12;
    private const int MaxTextLength = 120;
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TripSession _session;
    private readonly PromptPacketBuilder _promptPacketBuilder;
    private readonly LiveSessionConductor _singleTurnConductor;
    private readonly MissionIntakeApplication _missionIntake;
    private readonly ItinerarySlotCompiler _itinerarySlotCompiler;
    private readonly ItineraryCandidateApplication _itineraryCandidateApplication;
    private readonly AvailabilityQuotePreviewApplication _availabilityQuotePreviewApplication;
    private readonly LiveTurnProjector _liveTurnProjector;
    private readonly List<LiveMultiTurnTimelineItem> _timeline = [];

    private PromptIntakeResult? _currentModelInput;
    private int _turnIndex;

    public LiveMultiTurnSessionConductor(
        TripSession session,
        PromptPacketBuilder? promptPacketBuilder = null,
        LiveSessionConductor? singleTurnConductor = null,
        MissionIntakeApplication? missionIntake = null,
        ItinerarySlotCompiler? itinerarySlotCompiler = null,
        ItineraryCandidateApplication? itineraryCandidateApplication = null,
        AvailabilityQuotePreviewApplication? availabilityQuotePreviewApplication = null,
        LiveTurnProjector? liveTurnProjector = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _promptPacketBuilder = promptPacketBuilder ?? new PromptPacketBuilder();
        _singleTurnConductor = singleTurnConductor ?? new LiveSessionConductor(promptPacketBuilder: _promptPacketBuilder);
        _missionIntake = missionIntake ?? new MissionIntakeApplication();
        _itinerarySlotCompiler = itinerarySlotCompiler ?? new ItinerarySlotCompiler();
        _itineraryCandidateApplication = itineraryCandidateApplication ?? new ItineraryCandidateApplication();
        _availabilityQuotePreviewApplication = availabilityQuotePreviewApplication ?? new AvailabilityQuotePreviewApplication();
        _liveTurnProjector = liveTurnProjector ?? new LiveTurnProjector();
    }

    public TripSession Session => _session;

    public PromptIntakeResult? CurrentModelInput => _currentModelInput;

    public IReadOnlyList<LiveMultiTurnTimelineItem> Timeline => _timeline;

    public LiveMultiTurnSessionResult StartInitialPrompt(LiveInitialPromptRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId))
        {
            return BuildBlocked(InitialPromptOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live session request failed validation.", before);
        }

        var prompt = _promptPacketBuilder.Build(_session, new PromptIntakeRequest(
            request!.SessionId,
            request.TransientRawPrompt,
            _session.MemoryDigest,
            request.Locale,
            request.ScenarioHints ?? []));
        if (!prompt.IsAccepted || prompt.Packet is null)
        {
            return BuildBlocked(InitialPromptOperation, LiveSessionConductor.ProviderModelBlockedCode, "Prompt packet construction was blocked.", before);
        }

        _currentModelInput = prompt;
        var projection = _liveTurnProjector.FromPromptIntake(prompt);
        return BuildResult(
            InitialPromptOperation,
            IsAccepted: true,
            IsBlocked: false,
            AwaitingModelProposalCode,
            "Fresh model input is ready.",
            before,
            projection,
            [ApplyModelProposalOperation, RecordProviderBlockedOperation],
            ModelInputFragment(prompt));
    }

    public LiveMultiTurnSessionResult BuildFreshModelInput(LiveModelInputRefreshRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId))
        {
            return BuildBlocked(BuildFreshModelInputOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live session request failed validation.", before);
        }

        var prompt = _promptPacketBuilder.Build(_session, new PromptIntakeRequest(
            request!.SessionId,
            "harness fresh projection",
            _session.MemoryDigest,
            request.Locale,
            request.ScenarioHints ?? []));
        if (!prompt.IsAccepted || prompt.Packet is null)
        {
            return BuildBlocked(BuildFreshModelInputOperation, LiveSessionConductor.ProviderModelBlockedCode, "Prompt packet construction was blocked.", before);
        }

        _currentModelInput = prompt;
        var projection = _liveTurnProjector.FromPromptIntake(prompt);
        return BuildResult(
            BuildFreshModelInputOperation,
            IsAccepted: true,
            IsBlocked: false,
            AwaitingModelProposalCode,
            "Fresh model input is ready.",
            before,
            projection,
            [ApplyModelProposalOperation, RecordProviderBlockedOperation],
            ModelInputFragment(prompt));
    }

    public LiveMultiTurnSessionResult ApplyModelProposal(LiveModelProposalApplicationRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId) || request!.Proposal is null)
        {
            return BuildBlocked(ApplyModelProposalOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live session request failed validation.", before);
        }

        if (_currentModelInput?.Packet is null)
        {
            return BuildBlocked(ApplyModelProposalOperation, LiveSessionConductor.StalePacketSessionCode, "Live proposal references stale packet or session context.", before);
        }

        var result = _singleTurnConductor.RunPreparedTurn(_session, _currentModelInput, request.Proposal, request.ScenarioHints ?? []);
        var allowed = AllowedAfterProjection(result.TurnProjection);
        if (result.IsAccepted && result.Code is LiveSessionConductor.AcceptedCode or LiveSessionConductor.AwaitingUserInputCode)
        {
            _currentModelInput = null;
        }

        return BuildResult(
            ApplyModelProposalOperation,
            result.IsAccepted,
            result.IsBlocked,
            result.Code,
            result.Summary,
            before,
            result.TurnProjection,
            allowed,
            ModelInputFragment(_currentModelInput));
    }

    public LiveMultiTurnSessionResult SubmitConfirmation(LiveUserConfirmationRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId) || request!.Answers is null || request.Answers.Count == 0)
        {
            return BuildBlocked(SubmitConfirmationOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live confirmation request failed validation.", before);
        }

        var pending = _session.MemoryDigest?.PendingConfirmations ?? [];
        var acceptedFields = new List<MissionFieldProposal>();
        foreach (var answer in request.Answers)
        {
            var pendingItem = pending.FirstOrDefault(item => string.Equals(item.FieldPath, answer.FieldPath, StringComparison.Ordinal));
            if (pendingItem is null || answer.Decision is not LiveConfirmationDecision.Confirm and not LiveConfirmationDecision.Correct)
            {
                return BuildBlocked(SubmitConfirmationOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live confirmation request failed validation.", before);
            }

            var value = answer.Decision is LiveConfirmationDecision.Correct ? answer.CorrectedValue : pendingItem.ProposedValue;
            if (string.IsNullOrWhiteSpace(value))
            {
                return BuildBlocked(SubmitConfirmationOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live confirmation request failed validation.", before);
            }

            acceptedFields.Add(new MissionFieldProposal(pendingItem.FieldPath, value, AuthoritySource.User, pendingItem.EvidenceIds));
        }

        var intake = _missionIntake.Apply(_session, new MissionIntakeProposal(
            $"confirmation-{_timeline.Count + 1}",
            acceptedFields,
            [],
            []));
        var compilation = _itinerarySlotCompiler.Compile(_session, new ItineraryCompilationRequest(
            _session.SessionId,
            null,
            null,
            _session.MemoryDigest,
            []));
        var choiceProjection = BuildChoiceProjection(compilation);
        return BuildResult(
            SubmitConfirmationOperation,
            IsAccepted: true,
            IsBlocked: false,
            ConfirmationAppliedCode,
            intake.Summary,
            before,
            choiceProjection,
            [SelectOptionOperation, DeferOptionOperation, BuildFreshModelInputOperation],
            ModelInputFragment(_currentModelInput));
    }

    public LiveMultiTurnSessionResult ApplyOptionDecision(LiveOptionDecisionRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId))
        {
            return BuildBlocked(SelectOptionOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live option decision request failed validation.", before);
        }

        var operation = request!.Kind is ItinerarySlotDecisionKind.Deferred ? DeferOptionOperation : SelectOptionOperation;
        var result = _itineraryCandidateApplication.Apply(_session, new ItinerarySlotDecisionRequest(
            request.SessionId,
            request.SlotId,
            request.Kind,
            request.SlotKind,
            request.CandidateId,
            request.CandidateKind,
            request.DecidedAt ?? FixedNow));
        var projection = _liveTurnProjector.FromItineraryDecision(result, new CardMediaProvenanceBoundary().BuildJapanMoodMediaManifest());
        return BuildResult(
            operation,
            result.IsAccepted,
            result.IsBlocked,
            result.Code,
            result.Summary,
            before,
            projection,
            result.IsAccepted ? [PreviewAvailabilityOperation, BuildFreshModelInputOperation] : [SelectOptionOperation, DeferOptionOperation],
            ModelInputFragment(_currentModelInput));
    }

    public LiveMultiTurnSessionResult PreviewAvailability(LiveAvailabilityPreviewTurnRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId))
        {
            return BuildBlocked(PreviewAvailabilityOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live availability preview request failed validation.", before);
        }

        var result = _availabilityQuotePreviewApplication.Preview(_session, request!.Preview);
        var projection = _liveTurnProjector.FromAvailabilityPreview(result);
        return BuildResult(
            PreviewAvailabilityOperation,
            result.IsAccepted,
            result.IsBlocked,
            result.Code,
            result.Summary,
            before,
            projection,
            result.Code == AvailabilityQuotePreviewApplication.ApprovalRequiredCode
                ? ["await_user_approval", BuildFreshModelInputOperation]
                : [BuildFreshModelInputOperation],
            ModelInputFragment(_currentModelInput));
    }

    public LiveMultiTurnSessionResult RecordProviderModelBlocked(LiveProviderModelBlockedRequest request)
    {
        var before = SnapshotCounts();
        if (!ValidSession(request?.SessionId))
        {
            return BuildBlocked(RecordProviderBlockedOperation, LiveSessionConductor.UnsupportedLiveOperationCode, "Live provider result failed validation.", before);
        }

        var projection = _liveTurnProjector.ProviderModelFailure(request!.Code);
        return BuildResult(
            RecordProviderBlockedOperation,
            IsAccepted: false,
            IsBlocked: true,
            LiveSessionConductor.ProviderModelBlockedCode,
            "Provider or model output was blocked.",
            before,
            projection,
            [BuildFreshModelInputOperation, RecordProviderBlockedOperation],
            ModelInputFragment(_currentModelInput));
    }

    private LiveTurnProjectionResult BuildChoiceProjection(ItineraryCompilationResult compilation)
    {
        if (!compilation.IsCompiled)
        {
            return _liveTurnProjector.ProviderModelFailure(compilation.Code);
        }

        var slot = compilation.Days
            .SelectMany(day => day.Slots)
            .FirstOrDefault(slot => slot.Kind is ItinerarySlotKind.Activity or ItinerarySlotKind.Meal);
        if (slot is null)
        {
            return _liveTurnProjector.DeterministicFallback();
        }

        if (slot.Kind is ItinerarySlotKind.Activity)
        {
            _session.AssociateItineraryCandidatePool(slot.SlotId, "pool-logistics");
        }

        var choices = _session.CandidatePools
            .SelectMany(pool => pool.Candidates)
            .Where(candidate => CandidateMatchesSlot(slot.Kind, candidate.Kind))
            .Take(4)
            .Select(candidate => new CandidateSummary(
                candidate.CandidateId,
                candidate.Kind.ToString(),
                candidate.Title,
                candidate.Summary,
                candidate.EvidenceIds))
            .ToArray();

        return _liveTurnProjector.FromSessionTurn(SessionTurnResult.Continued(
            _session.Stage,
            new ProjectionService().Project(_session, _session.Stage),
            new EmitChoiceSetAction(
                $"choice-{slot.SlotId}",
                "Trusted itinerary options",
                choices,
                1)));
    }

    private LiveMultiTurnSessionResult BuildBlocked(
        string operation,
        string code,
        string summary,
        LiveMutationSnapshot before)
    {
        var projection = _liveTurnProjector.ProviderModelFailure(code);
        return BuildResult(
            operation,
            IsAccepted: false,
            IsBlocked: true,
            code,
            summary,
            before,
            projection,
            [BuildFreshModelInputOperation, RecordProviderBlockedOperation],
            ModelInputFragment(_currentModelInput));
    }

    private LiveMultiTurnSessionResult BuildResult(
        string operation,
        bool IsAccepted,
        bool IsBlocked,
        string code,
        string summary,
        LiveMutationSnapshot before,
        LiveTurnProjectionResult projection,
        IReadOnlyList<string> allowedNextOperations,
        LiveModelInputFragment? modelInput)
    {
        var turn = projection.Transcript.Turns.LastOrDefault();
        var didMutate = before != SnapshotCounts();
        var turnId = $"live-session-turn-{++_turnIndex:00}";
        var evidenceRefs = projection.Transcript.EvidenceReferences.Take(MaxEvidenceRefs).Select(SafeId).ToArray();
        _timeline.Add(new LiveMultiTurnTimelineItem(
            turnId,
            SafeText(operation),
            SafeText(code),
            SafeText(turn?.WorkItem?.Kind ?? "summary"),
            evidenceRefs));

        return new LiveMultiTurnSessionResult(
            IsAccepted,
            IsBlocked,
            didMutate,
            SafeText(code),
            SafeText(summary),
            turnId,
            _session.Stage.ToString(),
            allowedNextOperations.Select(SafeText).Distinct(StringComparer.Ordinal).ToArray(),
            SafeText(turn?.WorkItem?.Kind ?? "summary"),
            evidenceRefs,
            _timeline.Select(item => item.TurnId).TakeLast(MaxTimelineRefs).ToArray(),
            modelInput,
            projection);
    }

    private LiveModelInputFragment? ModelInputFragment(PromptIntakeResult? prompt)
    {
        if (prompt?.Packet is null)
        {
            return null;
        }

        return new(
            SafeId(prompt.Packet.SessionId),
            SafeId(prompt.Packet.PacketId),
            SafeText(prompt.Packet.Stage),
            prompt.Packet.CurrentMissionFacts.Count,
            prompt.Packet.PendingConfirmations.Count,
            prompt.Packet.KnownConstraints.Count,
            prompt.Packet.EvidenceReferences.Take(MaxEvidenceRefs).Select(SafeId).ToArray());
    }

    private bool ValidSession(string? sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId)
            && string.Equals(sessionId, _session.SessionId, StringComparison.Ordinal);
    }

    private LiveMutationSnapshot SnapshotCounts()
    {
        return new(
            _session.Actions.Count,
            _session.DecisionLedger.Records.Count,
            _session.ItineraryDecisions.Count,
            _session.ApprovalTokens.Count,
            _session.DeferredSlots.Count,
            _session.MemoryDigest?.DigestId,
            _session.LastItineraryCompilation?.Code,
            _session.Mission);
    }

    private static bool CandidateMatchesSlot(ItinerarySlotKind slotKind, CandidateKind candidateKind)
    {
        return slotKind switch
        {
            ItinerarySlotKind.Activity => candidateKind is CandidateKind.Activity,
            ItinerarySlotKind.Transit => candidateKind is CandidateKind.Transit or CandidateKind.Flight,
            ItinerarySlotKind.Meal => candidateKind is CandidateKind.Restaurant,
            ItinerarySlotKind.Sleep => candidateKind is CandidateKind.Hotel,
            ItinerarySlotKind.Downtime => candidateKind is CandidateKind.Activity or CandidateKind.ScheduleBlock,
            ItinerarySlotKind.FixedCommitment => candidateKind is CandidateKind.ScheduleBlock,
            ItinerarySlotKind.UnresolvedConfirmation => false,
            _ => false
        };
    }

    private static IReadOnlyList<string> AllowedAfterProjection(LiveTurnProjectionResult projection)
    {
        var workKind = projection.Transcript.Turns.LastOrDefault()?.WorkItem?.Kind;
        return workKind switch
        {
            "form" => [SubmitConfirmationOperation, RecordProviderBlockedOperation],
            "choice" => [SelectOptionOperation, DeferOptionOperation, RecordProviderBlockedOperation],
            "approval" => ["await_user_approval", RecordProviderBlockedOperation],
            "provider_failure" or "blocked" => [BuildFreshModelInputOperation, RecordProviderBlockedOperation],
            _ => [BuildFreshModelInputOperation, ApplyModelProposalOperation, RecordProviderBlockedOperation]
        };
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        if (trimmed.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "redacted";
        }

        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}

public sealed record LiveInitialPromptRequest(
    string SessionId,
    string TransientRawPrompt,
    string? Locale,
    IReadOnlyList<string> ScenarioHints);

public sealed record LiveModelInputRefreshRequest(
    string SessionId,
    string? Locale,
    IReadOnlyList<string> ScenarioHints);

public sealed record LiveModelProposalApplicationRequest(
    string SessionId,
    LiveModelProposalEnvelope Proposal,
    IReadOnlyList<string> ScenarioHints);

public sealed record LiveUserConfirmationRequest(
    string SessionId,
    IReadOnlyList<LiveConfirmationAnswer> Answers);

public sealed record LiveConfirmationAnswer(
    string FieldPath,
    LiveConfirmationDecision Decision,
    string? CorrectedValue);

public enum LiveConfirmationDecision
{
    Confirm,
    Correct,
    Defer
}

public sealed record LiveOptionDecisionRequest(
    string SessionId,
    string SlotId,
    ItinerarySlotDecisionKind Kind,
    ItinerarySlotKind SlotKind,
    string? CandidateId,
    CandidateKind? CandidateKind,
    DateTimeOffset? DecidedAt);

public sealed record LiveAvailabilityPreviewTurnRequest(
    string SessionId,
    AvailabilityQuotePreviewRequest Preview);

public sealed record LiveProviderModelBlockedRequest(
    string SessionId,
    string Code);

public sealed record LiveMultiTurnSessionResult(
    bool IsAccepted,
    bool IsBlocked,
    bool DidMutate,
    string Code,
    string Summary,
    string TurnId,
    string Stage,
    IReadOnlyList<string> AllowedNextOperationKinds,
    string AssistantWorkItemKind,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> TimelineItemReferences,
    LiveModelInputFragment? ModelInput,
    LiveTurnProjectionResult Projection);

public sealed record LiveModelInputFragment(
    string SessionId,
    string PacketId,
    string Stage,
    int MissionFactCount,
    int PendingConfirmationCount,
    int ConstraintCount,
    IReadOnlyList<string> EvidenceReferences);

public sealed record LiveMultiTurnTimelineItem(
    string TurnId,
    string OperationKind,
    string Code,
    string AssistantWorkItemKind,
    IReadOnlyList<string> EvidenceReferences);

internal sealed record LiveMutationSnapshot(
    int Actions,
    int Decisions,
    int ItineraryDecisions,
    int Approvals,
    int DeferredSlots,
    string? DigestId,
    string? CompilationCode,
    TripMission Mission);
