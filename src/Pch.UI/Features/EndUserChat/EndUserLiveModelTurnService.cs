using Pch.Harness;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LivePreflight;
using Pch.Providers.LiveTurns;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;

namespace Pch.UI.Features.EndUserChat;

public sealed class EndUserLiveModelTurnService
{
    public const string PreflightDeterministic = "deterministic_default";
    public const string PreflightBlockedByGuard = "blocked_by_guard";
    public const string PreflightBlocked = "preflight_blocked";
    public const string PreflightPassed = "preflight_passed";
    public const string PreflightReady = "preflight_ready";
    public const string LatestDeterministic = "deterministic_fallback";
    public const string LatestLivePreflight = "live_preflight";
    public const string LatestLiveAccepted = "live_model_proposal_accepted";
    public const string LatestLiveBlocked = "live_model_proposal_blocked";
    public const string LatestHarnessValidationBlocked = "harness_validation_blocked";
    public const string ProviderRequestNotAttempted = "not_attempted";
    public const string ProviderRequestAttempted = "attempted";

    private readonly Func<IReadOnlyDictionary<string, string?>> _environmentFactory;
    private readonly LivePreflightEvaluator _preflightEvaluator;
    private readonly IEndUserLiveTurnGateway _turnGateway;

    public EndUserLiveModelTurnService()
        : this(ReadProcessEnvironment, CreateDefaultPreflightEvaluator())
    {
    }

    public EndUserLiveModelTurnService(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LivePreflightEvaluator? preflightEvaluator = null,
        IEndUserLiveTurnGateway? turnGateway = null)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _preflightEvaluator = preflightEvaluator ?? CreateDefaultPreflightEvaluator();
        _turnGateway = turnGateway ?? EndUserLiveTurnGateway.CreateDefault(_environmentFactory);
    }

    public EndUserLiveModelSnapshot CreateSnapshot(string selectedRole)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return EndUserLiveModelSnapshot.Deterministic();
        }

        var options = LivePreflightOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        var preflightState = options.Enabled && options.ApiKeyAvailable
            ? PreflightReady
            : PreflightBlockedByGuard;

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            preflightState,
            preflightState == PreflightBlockedByGuard
                ? EndUserLiveProposalMarkers.NotRun
                : EndUserLiveProposalMarkers.AwaitingUserInput,
            EndUserLiveProposalMarkers.NotRun,
            LatestDeterministic,
            ProviderRequestNotAttempted,
            options.Enabled ? "live_config_present" : "live_guard_disabled",
            options.ApiKeyAvailable ? "key_available" : "key_unavailable",
            options.CreditGuardEnabled ? "credit_guard_enabled" : "credit_guard_skipped",
            options.Enabled
                ? options.ApiKeyAvailable ? "none" : LivePreflightRunner.OutcomeKeyMissing
                : LivePreflightRunner.OutcomeDisabled,
            modelId,
            null,
            preflightState == PreflightBlockedByGuard ? "PCH_UI_LIVE_MODEL_GUARDED" : null,
            preflightState == PreflightBlockedByGuard ? "Live model mode is guarded until explicit provider configuration is present." : null,
            null,
            null);
    }

    public async Task<EndUserLiveModelSnapshot> TryRunAsync(
        string prompt,
        string selectedRole,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = EndUserModelRoleSelection.Normalize(selectedRole);
        if (normalizedRole == EndUserModelRoleSelection.DeterministicOffline)
        {
            return EndUserLiveModelSnapshot.Deterministic();
        }

        var options = LivePreflightOptions.FromEnvironment(_environmentFactory());
        var role = ToLiveRole(normalizedRole);
        var modelId = ModelIdFor(options, role);
        if (!options.Enabled || !options.ApiKeyAvailable)
        {
            var outcome = options.Enabled
                ? LivePreflightRunner.OutcomeKeyMissing
                : LivePreflightRunner.OutcomeDisabled;
            return BlockedByGuard(normalizedRole, options, modelId, outcome);
        }

        var packet = new LivePreflightPacket(
            "packet-end-user-live-preflight",
            [new LivePreflightRoleProbe(role, $"probe-{normalizedRole}")],
            "en-US",
            ContextDigest: "end-user-chat-live-model-read-only");

        var row = (await _preflightEvaluator.EvaluateAsync(
            [new LivePreflightEvalCase("end-user-live-preflight", packet)],
            options,
            cancellationToken).ConfigureAwait(false)).Single();

        if (row.Passed)
        {
            var session = SyntheticTripFactory.CreateSession(3);
            var conductor = new LiveMultiTurnSessionConductor(session);
            var start = conductor.StartInitialPrompt(new LiveInitialPromptRequest(
                session.SessionId,
                prompt,
                "en-US",
                ["end-user-chat", normalizedRole]));
            if (!start.IsAccepted || start.ModelInput is null)
            {
                return BlockedAfterPreflight(
                    normalizedRole,
                    options,
                    modelId,
                    row,
                    LiveSessionConductor.ProviderModelBlockedCode,
                    "prompt_packet_blocked",
                    start.Code);
            }

            var proposal = await _turnGateway.BuildAsync(
                start.ModelInput,
                normalizedRole,
                LiveTurnOptions.FromEnvironment(_environmentFactory()),
                cancellationToken).ConfigureAwait(false);
            var application = proposal.Envelope is null
                ? conductor.RecordProviderModelBlocked(new LiveProviderModelBlockedRequest(session.SessionId, proposal.ProviderOutcomeCode))
                : conductor.ApplyModelProposal(new LiveModelProposalApplicationRequest(session.SessionId, proposal.Envelope, ["end-user-chat"]));
            var proposalState = ProposalStateFor(proposal, application);
            var harnessValidation = HarnessValidationStateFor(application);
            var latestTurn = LatestTurnSourceFor(proposalState, harnessValidation);
            return new EndUserLiveModelSnapshot(
                normalizedRole,
                proposal.Provider ?? row.Provider ?? options.Provider,
                PreflightPassed,
                proposalState,
                harnessValidation,
                latestTurn,
                ProviderRequestAttempted,
                proposal.ProviderOutcomeCode,
                $"harness_multiturn_{application.Code}",
                CreditStateFor(row.OutcomeCode, options),
                proposal.FailureCode ?? "none",
                proposal.Model ?? row.Model ?? modelId,
                proposal.RequestId ?? row.RequestId,
                application.IsBlocked ? "PCH_UI_LIVE_PROPOSAL_BLOCKED" : null,
                application.IsBlocked ? application.Code : null,
                new EndUserChatTurn(
                    "turn-live-model-run",
                    "provider",
                    "live-model",
                    application.IsBlocked ? "blocked" : "applied",
                    TurnTextFor(proposalState, harnessValidation),
                    proposal.ProviderOutcomeCode,
                    "evidence-chat-live-model",
                    application.IsBlocked ? "PCH_UI_LIVE_PROPOSAL_BLOCKED" : null),
                application.IsBlocked ? LiveFailureNotice(application.Code) : null);
        }

        return new EndUserLiveModelSnapshot(
            normalizedRole,
            options.Provider,
            IsGuardOutcome(row.OutcomeCode) ? PreflightBlockedByGuard : PreflightBlocked,
            EndUserLiveProposalMarkers.NotRun,
            EndUserLiveProposalMarkers.NotRun,
            LatestDeterministic,
            ProviderRequestStateFor(row.OutcomeCode),
            row.OutcomeCode,
            "live_preflight_blocked",
            CreditStateFor(row.OutcomeCode, options),
            row.OutcomeCode,
            row.Model ?? modelId,
            row.RequestId,
            "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE",
            row.OutcomeCode,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live provider preflight was blocked with a sanitized outcome. Deterministic fallback remains available.",
                row.OutcomeCode,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"),
            LiveFailureNotice(row.OutcomeCode));
    }

    private static EndUserLiveModelSnapshot BlockedByGuard(
        string normalizedRole,
        LivePreflightOptions options,
        string modelId,
        string outcome) =>
        new(
            normalizedRole,
            options.Provider,
            PreflightBlockedByGuard,
            EndUserLiveProposalMarkers.NotRun,
            EndUserLiveProposalMarkers.NotRun,
            LatestDeterministic,
            ProviderRequestNotAttempted,
            outcome,
            "live_guard_blocked",
            options.CreditGuardEnabled ? "credit_guard_not_run" : "credit_guard_skipped",
            outcome,
            modelId,
            null,
            "PCH_UI_LIVE_MODEL_GUARDED",
            outcome,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live model mode is guarded until explicit provider configuration is present. The planner continued with deterministic fallback.",
                outcome,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_MODEL_GUARDED"),
            LiveFailureNotice(outcome));

    private static EndUserProviderFailureNotice LiveFailureNotice(string outcome) =>
        new(
            "notice-live-model-guard",
            outcome,
            "blocked",
            "Live model output was not used. The deterministic planner path stayed active with sanitized failure markers.",
            CanRetry: false,
            CanContinueDeterministic: true);

    private static EndUserLiveModelSnapshot BlockedAfterPreflight(
        string normalizedRole,
        LivePreflightOptions options,
        string modelId,
        SanitizedLivePreflightEvalRow row,
        string providerOutcome,
        string failureCode,
        string blockedReason) =>
        new(
            normalizedRole,
            row.Provider ?? options.Provider,
            PreflightPassed,
            EndUserLiveProposalMarkers.Blocked,
            EndUserLiveProposalMarkers.NotRun,
            LatestLiveBlocked,
            ProviderRequestAttempted,
            providerOutcome,
            "harness_multiturn_prompt_blocked",
            CreditStateFor(row.OutcomeCode, options),
            failureCode,
            row.Model ?? modelId,
            row.RequestId,
            "PCH_UI_LIVE_PROPOSAL_BLOCKED",
            blockedReason,
            new EndUserChatTurn(
                "turn-live-model-run",
                "provider",
                "live-model",
                "blocked",
                "Live model input was blocked by the harness prompt boundary with a sanitized outcome.",
                providerOutcome,
                "evidence-chat-live-model",
                "PCH_UI_LIVE_PROPOSAL_BLOCKED"),
            LiveFailureNotice(blockedReason));

    private static string ProposalStateFor(EndUserLiveTurnCandidate proposal, LiveMultiTurnSessionResult conductor)
    {
        if (!proposal.ProviderAccepted)
        {
            return proposal.ProviderOutcomeCode is LiveTurnRunner.OutcomePacketMismatch
                ? EndUserLiveProposalMarkers.StalePacketOrSession
                : EndUserLiveProposalMarkers.Blocked;
        }

        return EndUserLiveProposalMarkers.Accepted;
    }

    private static string HarnessValidationStateFor(LiveMultiTurnSessionResult conductor)
    {
        if (conductor.Code is LiveSessionConductor.DeterministicFallbackCode)
        {
            return EndUserLiveProposalMarkers.DeterministicFallback;
        }

        if (conductor.Code is LiveSessionConductor.MissionProposalBlockedCode or
            LiveSessionConductor.UnsupportedLiveOperationCode or
            LiveSessionConductor.DecodeBlockedCode or
            LiveSessionConductor.IntakeBlockedCode or
            LiveSessionConductor.StalePacketSessionCode)
        {
            return EndUserLiveProposalMarkers.HarnessValidationBlocked;
        }

        if (conductor.Code is LiveSessionConductor.ProviderModelBlockedCode)
        {
            return EndUserLiveProposalMarkers.NotRun;
        }

        return conductor.Code;
    }

    private static string LatestTurnSourceFor(string proposalState, string harnessValidation)
    {
        if (harnessValidation == EndUserLiveProposalMarkers.HarnessValidationBlocked)
        {
            return LatestHarnessValidationBlocked;
        }

        return proposalState switch
        {
            EndUserLiveProposalMarkers.Accepted => LatestLiveAccepted,
            EndUserLiveProposalMarkers.Blocked => LatestLiveBlocked,
            EndUserLiveProposalMarkers.StalePacketOrSession => LatestLiveBlocked,
            _ => LatestDeterministic
        };
    }

    private static string TurnTextFor(string proposalState, string harnessValidation) =>
        proposalState switch
        {
            EndUserLiveProposalMarkers.Accepted when harnessValidation == EndUserLiveProposalMarkers.HarnessValidationBlocked =>
                "Live model proposal was received, but the harness validation boundary blocked it with a sanitized outcome.",
            EndUserLiveProposalMarkers.Accepted =>
                "Live model proposal was accepted by the harness boundary and rendered through the existing planning primitives.",
            EndUserLiveProposalMarkers.Blocked =>
                "Live model proposal was blocked before application. The deterministic planner path remains available.",
            EndUserLiveProposalMarkers.StalePacketOrSession =>
                "Live model proposal was blocked because the provider packet did not match the current session.",
            _ =>
                "Live provider preflight accepted structured output; the planner is awaiting a user action."
        };

    private static LiveModelRole ToLiveRole(string selectedRole) =>
        selectedRole == EndUserModelRoleSelection.StrongPlanner
            ? LiveModelRole.StrongPlanner
            : LiveModelRole.InHarnessActionGenerator;

    private static string ModelIdFor(LivePreflightOptions options, LiveModelRole role) =>
        role == LiveModelRole.StrongPlanner ? options.StrongPlannerModelId : options.InHarnessModelId;

    private static string ProviderRequestStateFor(string outcome) =>
        IsGuardOutcome(outcome)
            ? ProviderRequestNotAttempted
            : ProviderRequestAttempted;

    private static string CreditStateFor(string outcome, LivePreflightOptions options)
    {
        if (!options.CreditGuardEnabled)
        {
            return "credit_guard_skipped";
        }

        return outcome switch
        {
            LivePreflightRunner.OutcomeDisabled or
            LivePreflightRunner.OutcomeKeyMissing or
            LivePreflightRunner.OutcomeSchemaUnsupported or
            LivePreflightRunner.OutcomeFallbackDisabled => "credit_guard_not_run",
            LivePreflightRunner.OutcomeCreditExhausted => "credit_exhausted",
            _ => "credit_guard_checked"
        };
    }

    private static bool IsGuardOutcome(string outcome) =>
        outcome is LivePreflightRunner.OutcomeDisabled
            or LivePreflightRunner.OutcomeKeyMissing
            or LivePreflightRunner.OutcomeSchemaUnsupported
            or LivePreflightRunner.OutcomeFallbackDisabled;

    private static LivePreflightEvaluator CreateDefaultPreflightEvaluator()
    {
        var client = new DisabledLivePreflightClient();
        return new LivePreflightEvaluator(new LivePreflightRunner(client, client));
    }

    private static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var values = Environment.GetEnvironmentVariables();
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in values.Keys.OfType<string>())
        {
            environment[key] = values[key]?.ToString();
        }

        return environment;
    }

    private sealed class DisabledLivePreflightClient : IModelCompletionClient, IProviderCreditClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelCompletionResponse>(
                new InvalidOperationException("Live preflight client is not configured."));

        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(null, null, null, IsExhausted: true));
    }
}

public sealed record EndUserLiveModelSnapshot(
    string SelectedModelRole,
    string SelectedProvider,
    string LivePreflightState,
    string LiveProposalState,
    string HarnessValidationState,
    string LatestTurnSource,
    string ProviderRequestState,
    string ProviderOutcome,
    string ProviderHealth,
    string CreditState,
    string LastProviderFailureCode,
    string? ModelId,
    string? RequestId,
    string? ErrorCode,
    string? BlockedReason,
    EndUserChatTurn? Turn,
    EndUserProviderFailureNotice? FailureNotice)
{
    public bool IsLiveAccepted() =>
        LatestTurnSource == EndUserLiveModelTurnService.LatestLiveAccepted;

    public static EndUserLiveModelSnapshot Deterministic() =>
        new(
            EndUserModelRoleSelection.DeterministicOffline,
            "none",
            EndUserLiveModelTurnService.PreflightDeterministic,
            EndUserLiveProposalMarkers.NotRequested,
            EndUserLiveProposalMarkers.NotRun,
            EndUserLiveModelTurnService.LatestDeterministic,
            EndUserLiveModelTurnService.ProviderRequestNotAttempted,
            EndUserChatService.ProviderOutcomeFallback,
            "offline_ready",
            "credits_not_used",
            "none",
            null,
            null,
            null,
            null,
            null,
            null);
}

public interface IEndUserLiveTurnGateway
{
    Task<EndUserLiveTurnCandidate> BuildAsync(
        LiveModelInputFragment modelInput,
        string selectedRole,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record EndUserLiveTurnCandidate(
    LiveModelProposalEnvelope? Envelope,
    bool ProviderAccepted,
    string ProviderOutcomeCode,
    string? FailureCode,
    string? Provider,
    string? Model,
    string? RequestId);

public sealed class EndUserLiveTurnGateway : IEndUserLiveTurnGateway
{
    private readonly Func<IReadOnlyDictionary<string, string?>> _environmentFactory;
    private readonly LiveTurnRunner _runner;

    public EndUserLiveTurnGateway(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LiveTurnRunner runner)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public static EndUserLiveTurnGateway CreateDefault(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory)
    {
        var client = new DisabledLiveTurnClient();
        return new EndUserLiveTurnGateway(
            environmentFactory,
            new LiveTurnRunner(client, client));
    }

    public async Task<EndUserLiveTurnCandidate> BuildAsync(
        LiveModelInputFragment modelInput,
        string selectedRole,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        var role = selectedRole == EndUserModelRoleSelection.StrongPlanner
            ? LiveModelRole.StrongPlanner
            : LiveModelRole.InHarnessActionGenerator;
        var liveOptions = LiveTurnOptions.FromEnvironment(_environmentFactory()) with
        {
            Enabled = options.Enabled,
            ApiKeyAvailable = options.ApiKeyAvailable,
            CreditGuardEnabled = options.CreditGuardEnabled,
            StructuredOutputSupported = options.StructuredOutputSupported,
            AllowPaidProviderFallback = options.AllowPaidProviderFallback,
            Timeout = options.Timeout,
            ProviderKind = options.ProviderKind,
            Provider = options.Provider,
            InHarnessModelId = options.InHarnessModelId,
            StrongPlannerModelId = options.StrongPlannerModelId,
            MaxTokens = options.MaxTokens
        };
        var packet = new LiveTurnPacket(
            "run-end-user-live-turn",
            "turn-end-user-live-01",
            "packet-end-user-live-turn",
            modelInput.SessionId,
            role,
            "en-US",
            [
                LiveTurnOutputKind.MissionProposal,
                LiveTurnOutputKind.PendingConfirmationQuestion,
                LiveTurnOutputKind.ChoiceSet,
                LiveTurnOutputKind.SummaryFallbackNotice
            ],
            [],
            RequiresFallback: false,
            ProjectionDigest: "end-user-chat-bounded-live-turn-projection");

        try
        {
            var result = await _runner.RunAsync(packet, liveOptions, cancellationToken).ConfigureAwait(false);
            if (!MatchesPacket(packet, result))
            {
                return Blocked(
                    LiveTurnRunner.OutcomePacketMismatch,
                    ProviderFailureClass.ProviderSchemaInvalid.ToString(),
                    result.Provider,
                    result.Model,
                    result.RequestId);
            }

            if (result.HasUnsafeValue)
            {
                return Blocked(
                    LiveTurnRunner.OutcomeUnsafeValueRedacted,
                    ProviderFailureClass.ProviderSchemaInvalid.ToString(),
                    result.Provider,
                    result.Model,
                    result.RequestId);
            }

            if (result.OutputKind is not LiveTurnOutputKind.MissionProposal || result.MissionProposal is null)
            {
                return Blocked(
                    LiveTurnRunner.OutcomeUnsupportedValue,
                    ProviderFailureClass.ProviderSchemaInvalid.ToString(),
                    result.Provider,
                    result.Model,
                    result.RequestId);
            }

            var envelope = LiveModelProposalEnvelope.ForMission(MapMissionProposal(result.MissionProposal)) with
            {
                Correlation = new LiveProposalCorrelation(modelInput.SessionId, modelInput.PacketId, modelInput.Stage)
            };
            return new EndUserLiveTurnCandidate(
                envelope,
                ProviderAccepted: true,
                LiveTurnRunner.OutcomeAccepted,
                null,
                result.Provider,
                result.Model,
                result.RequestId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Blocked(ExceptionOutcomeCode(ex), ExceptionFailureCode(ex));
        }
    }

    private static EndUserLiveTurnCandidate Blocked(
        string outcomeCode,
        string? failureCode,
        string? provider = null,
        string? model = null,
        string? requestId = null) =>
        new(null, ProviderAccepted: false, outcomeCode, failureCode ?? outcomeCode, provider, model, requestId);

    private static bool MatchesPacket(LiveTurnPacket packet, LiveTurnResult result) =>
        string.Equals(packet.PacketId, result.PacketId, StringComparison.Ordinal) &&
        string.Equals(packet.SessionId, result.SessionId, StringComparison.Ordinal) &&
        packet.Role == result.Role &&
        packet.AllowedOutputKinds.Contains(result.OutputKind);

    private static ProviderMissionProposalMirror MapMissionProposal(LiveMissionProposalResult result) =>
        new(
            $"proposal-{result.PacketId}",
            result.Fields
                .Select(field => new ProviderMissionFieldMirror(
                    field.FieldPath,
                    SafeFieldValue(field),
                    SourceCode(field.AuthoritySource),
                    SafeEvidenceIds(field.EvidenceIds)))
                .ToArray(),
            result.PendingConfirmations
                .Select(pending => new ProviderConstraintMirror(
                    pending.ConfirmationId,
                    pending.FieldPath,
                    PendingReasonCode(pending.ReasonCode),
                    SourceCode(pending.AuthoritySource),
                    IsHard: true,
                    SafeEvidenceIds(pending.EvidenceIds)))
                .ToArray(),
            result.Commitments
                .Select(commitment => new ProviderCommitmentMirror(
                    commitment.CommitmentId,
                    CommitmentKindCode(commitment.CommitmentKind),
                    SafeText(commitment.Title, "Live model commitment"),
                    commitment.StartsAt,
                    commitment.EndsAt,
                    SafeOptionalText(commitment.Location),
                    commitment.IsIrreversible,
                    commitment.RequiresSpend,
                    PriorityCode(commitment.Priority),
                    SourceCode(commitment.AuthoritySource),
                    SafeEvidenceIds(commitment.EvidenceIds)))
                .ToArray());

    private static string SafeFieldValue(LiveMissionFieldProposal field) =>
        SafeText(field.Value, field.FieldPath switch
        {
            "/mission/purpose" => "vacation",
            "/mission/destination_country" => "Japan",
            "/mission/start_date" => "2026-10-05",
            "/mission/end_date" => "2026-10-12",
            _ => "pending"
        });

    private static string SourceCode(LiveMissionAuthoritySource source) =>
        source switch
        {
            LiveMissionAuthoritySource.UserStated => "user",
            LiveMissionAuthoritySource.TrustedProvider => "trusted_tool",
            _ => "strong_model_inference"
        };

    private static string CommitmentKindCode(LiveMissionCommitmentKind kind) =>
        kind switch
        {
            LiveMissionCommitmentKind.Travel => "travel",
            LiveMissionCommitmentKind.Lodging => "lodging",
            LiveMissionCommitmentKind.Dining => "meal",
            LiveMissionCommitmentKind.Activity => "activity",
            _ => "administrative"
        };

    private static string PriorityCode(LiveMissionCommitmentPriority priority) =>
        priority is LiveMissionCommitmentPriority.High or LiveMissionCommitmentPriority.Critical
            ? "high"
            : "normal";

    private static string PendingReasonCode(LiveMissionPendingReason reason) =>
        reason switch
        {
            LiveMissionPendingReason.NeedsDateConfirmation => "needs_date_confirmation",
            LiveMissionPendingReason.NeedsBudgetConfirmation => "needs_budget_confirmation",
            LiveMissionPendingReason.NeedsLocationConfirmation => "needs_location_confirmation",
            _ => "needs_user_confirmation"
        };

    private static IReadOnlyList<string> SafeEvidenceIds(IReadOnlyList<string>? evidenceIds) =>
        evidenceIds is null || evidenceIds.Count == 0
            ? ["evidence-live-proposal"]
            : evidenceIds
                .Where(IsSafeIdentifier)
                .DefaultIfEmpty("evidence-live-proposal")
                .Take(6)
                .ToArray();

    private static string SafeText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 160 ? fallback : value;

    private static string? SafeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 160 ? null : value;

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 160 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '_' or '-' or '/' or '.');

    private static string ExceptionOutcomeCode(Exception exception) =>
        exception switch
        {
            LiveTurnGuardException guard => guard.OutcomeCode,
            ProviderCreditExhaustedException => LiveTurnRunner.OutcomeCreditExhausted,
            ProviderEmptyResponseException => LiveTurnRunner.OutcomeProviderEmptyContent,
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) =>
                LiveTurnRunner.OutcomeProviderSchemaInvalid,
            ProviderMalformedResponseException => LiveTurnRunner.OutcomeProviderMalformedJson,
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) =>
                LiveTurnRunner.OutcomeProviderTimeout,
            TimeoutException => LiveTurnRunner.OutcomeProviderTimeout,
            ProviderException => LiveTurnRunner.OutcomeProviderUnknownError,
            _ => LiveTurnRunner.OutcomeProviderUnknownError
        };

    private static string? ExceptionFailureCode(Exception exception) =>
        exception switch
        {
            LiveTurnGuardException guard => guard.FailureClass.ToString(),
            ProviderCreditExhaustedException => "credit_exhausted",
            ProviderEmptyResponseException => "empty_content",
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) => "malformed_schema",
            ProviderMalformedResponseException => "malformed_json",
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => "timeout",
            TimeoutException => "timeout",
            ProviderException => "provider_error",
            _ => "provider_error"
        };

    private sealed class DisabledLiveTurnClient : IModelCompletionClient, IProviderCreditClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelCompletionResponse>(
                new ProviderUnavailableException("disabled", "Live turn provider is not configured."));

        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(null, null, 1m, IsExhausted: false));
    }
}

public sealed class EndUserLiveTurnGatewayFixture : IEndUserLiveTurnGateway
{
    private readonly EndUserLiveTurnCandidate _candidate;

    public EndUserLiveTurnGatewayFixture(EndUserLiveTurnCandidate candidate)
    {
        _candidate = candidate;
    }

    public Task<EndUserLiveTurnCandidate> BuildAsync(
        LiveModelInputFragment modelInput,
        string selectedRole,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_candidate);
    }
}

public sealed class EndUserLiveProposalGateway : IEndUserLiveTurnGateway
{
    private readonly ProviderMissionProposalMirror _proposal;
    private readonly string _providerOutcomeCode;
    private readonly string? _failureCode;

    public EndUserLiveProposalGateway(
        ProviderMissionProposalMirror proposal,
        string? providerOutcomeCode,
        string? failureCode)
    {
        _proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        _providerOutcomeCode = providerOutcomeCode ?? LiveTurnRunner.OutcomeAccepted;
        _failureCode = failureCode;
    }

    public Task<EndUserLiveTurnCandidate> BuildAsync(
        LiveModelInputFragment modelInput,
        string selectedRole,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = LiveModelProposalEnvelope.ForMission(_proposal) with
        {
            Correlation = new LiveProposalCorrelation(modelInput.SessionId, modelInput.PacketId, modelInput.Stage)
        };
        return Task.FromResult(new EndUserLiveTurnCandidate(
            envelope,
            ProviderAccepted: true,
            _providerOutcomeCode,
            _failureCode,
            "openrouter",
            "qwen/qwen3-14b",
            "request-live-turn-safe"));
    }
}
