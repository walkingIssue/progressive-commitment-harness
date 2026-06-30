using Pch.Harness;
using Pch.Providers.LivePreflight;
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
    private readonly LiveSessionConductor _conductor;
    private readonly IEndUserLiveProposalGateway _proposalGateway;

    public EndUserLiveModelTurnService()
        : this(ReadProcessEnvironment, CreateDefaultPreflightEvaluator(), new LiveSessionConductor())
    {
    }

    public EndUserLiveModelTurnService(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LivePreflightEvaluator? preflightEvaluator = null,
        LiveSessionConductor? conductor = null,
        IEndUserLiveProposalGateway? proposalGateway = null)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _preflightEvaluator = preflightEvaluator ?? CreateDefaultPreflightEvaluator();
        _conductor = conductor ?? new LiveSessionConductor();
        _proposalGateway = proposalGateway ?? EndUserLiveProposalGateway.Deferred;
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
                : EndUserLiveProposalMarkers.DeferredUntilRunner,
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
            var proposal = await _proposalGateway.BuildAsync(
                prompt,
                normalizedRole,
                options,
                cancellationToken).ConfigureAwait(false);
            var conductor = RunConductor(prompt, proposal.Envelope);
            var proposalState = ProposalStateFor(proposal, conductor);
            var harnessValidation = HarnessValidationStateFor(conductor);
            var latestTurn = LatestTurnSourceFor(proposalState, harnessValidation);
            return new EndUserLiveModelSnapshot(
                normalizedRole,
                row.Provider ?? options.Provider,
                PreflightPassed,
                proposalState,
                harnessValidation,
                latestTurn,
                ProviderRequestAttempted,
                proposal.ProviderOutcomeCode ?? row.OutcomeCode,
                $"harness_conductor_{conductor.Code}",
                CreditStateFor(row.OutcomeCode, options),
                proposal.FailureCode ?? "none",
                row.Model ?? modelId,
                row.RequestId,
                conductor.IsBlocked ? "PCH_UI_LIVE_PROPOSAL_BLOCKED" : null,
                conductor.IsBlocked ? conductor.Code : null,
                new EndUserChatTurn(
                    "turn-live-model-run",
                    "provider",
                    "live-model",
                    conductor.IsBlocked ? "blocked" : "applied",
                    TurnTextFor(proposalState, harnessValidation),
                    proposal.ProviderOutcomeCode ?? row.OutcomeCode,
                    "evidence-chat-live-model",
                    conductor.IsBlocked ? "PCH_UI_LIVE_PROPOSAL_BLOCKED" : null),
                conductor.IsBlocked ? LiveFailureNotice(conductor.Code) : null);
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

    private LivePlanningTurnResult RunConductor(string prompt, LiveModelProposalEnvelope envelope)
    {
        var session = SyntheticTripFactory.CreateSession(3);
        return _conductor.RunTurn(session, new LivePlanningTurnRequest(
            session.SessionId,
            prompt,
            "en-US",
            ["end-user-chat"],
            envelope));
    }

    private static string ProposalStateFor(EndUserLiveProposalCandidate proposal, LivePlanningTurnResult conductor)
    {
        if (proposal.Envelope.Kind is LiveModelProposalKind.DeterministicFallback)
        {
            return EndUserLiveProposalMarkers.DeferredUntilRunner;
        }

        if (proposal.Envelope.Kind is LiveModelProposalKind.ModelBlocked)
        {
            return EndUserLiveProposalMarkers.Blocked;
        }

        return conductor.Code is LiveSessionConductor.MissionProposalBlockedCode
            ? EndUserLiveProposalMarkers.Accepted
            : conductor.IsBlocked
                ? EndUserLiveProposalMarkers.Blocked
                : EndUserLiveProposalMarkers.Accepted;
    }

    private static string HarnessValidationStateFor(LivePlanningTurnResult conductor)
    {
        if (conductor.Code is LiveSessionConductor.DeterministicFallbackCode)
        {
            return EndUserLiveProposalMarkers.DeterministicFallback;
        }

        if (conductor.Code is LiveSessionConductor.MissionProposalBlockedCode or
            LiveSessionConductor.UnsupportedLiveOperationCode or
            LiveSessionConductor.DecodeBlockedCode or
            LiveSessionConductor.IntakeBlockedCode)
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
            EndUserLiveProposalMarkers.DeferredUntilRunner => LatestLivePreflight,
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
            _ =>
                "Live provider preflight accepted structured output; the mission proposal runner is not integrated in this UI lane yet."
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

public interface IEndUserLiveProposalGateway
{
    Task<EndUserLiveProposalCandidate> BuildAsync(
        string prompt,
        string selectedRole,
        LivePreflightOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record EndUserLiveProposalCandidate(
    LiveModelProposalEnvelope Envelope,
    string? ProviderOutcomeCode,
    string? FailureCode);

public sealed class EndUserLiveProposalGateway : IEndUserLiveProposalGateway
{
    public static EndUserLiveProposalGateway Deferred { get; } =
        new(LiveModelProposalEnvelope.Fallback(), "live_model_proposal_deferred", null);

    private readonly LiveModelProposalEnvelope _envelope;
    private readonly string? _providerOutcomeCode;
    private readonly string? _failureCode;

    public EndUserLiveProposalGateway(
        LiveModelProposalEnvelope envelope,
        string? providerOutcomeCode,
        string? failureCode)
    {
        _envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        _providerOutcomeCode = providerOutcomeCode;
        _failureCode = failureCode;
    }

    public Task<EndUserLiveProposalCandidate> BuildAsync(
        string prompt,
        string selectedRole,
        LivePreflightOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EndUserLiveProposalCandidate(_envelope, _providerOutcomeCode, _failureCode));
    }
}
