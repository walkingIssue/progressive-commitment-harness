using Pch.Harness;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
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
        _proposalGateway = proposalGateway ?? EndUserLiveMissionProposalGateway.CreateDefault(_environmentFactory);
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
            return EndUserLiveProposalMarkers.DeterministicFallback;
        }

        if (proposal.Envelope.Kind is LiveModelProposalKind.ModelBlocked)
        {
            return proposal.ProviderOutcomeCode is LiveMissionProposalRunner.OutcomePacketMismatch
                ? EndUserLiveProposalMarkers.StalePacketOrSession
                : EndUserLiveProposalMarkers.Blocked;
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

public sealed class EndUserLiveMissionProposalGateway : IEndUserLiveProposalGateway
{
    private const string PacketId = "packet-end-user-live-proposal";
    private const string SessionId = "session-end-user-live-proposal";
    private const string OutputKind = "mission_proposal";
    private readonly Func<IReadOnlyDictionary<string, string?>> _environmentFactory;
    private readonly LiveMissionProposalRunner _runner;

    public EndUserLiveMissionProposalGateway(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory,
        LiveMissionProposalRunner runner)
    {
        _environmentFactory = environmentFactory ?? throw new ArgumentNullException(nameof(environmentFactory));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public static EndUserLiveMissionProposalGateway CreateDefault(
        Func<IReadOnlyDictionary<string, string?>> environmentFactory)
    {
        var client = new DisabledLiveMissionProposalClient();
        return new EndUserLiveMissionProposalGateway(
            environmentFactory,
            new LiveMissionProposalRunner(client, client));
    }

    public async Task<EndUserLiveProposalCandidate> BuildAsync(
        string prompt,
        string selectedRole,
        LivePreflightOptions options,
        CancellationToken cancellationToken = default)
    {
        _ = prompt;
        _ = options;
        var role = selectedRole == EndUserModelRoleSelection.StrongPlanner
            ? LiveModelRole.StrongPlanner
            : LiveModelRole.InHarnessActionGenerator;
        var proposalOptions = LiveMissionProposalOptions.FromEnvironment(_environmentFactory());
        var packet = new LiveMissionProposalPacket(
            PacketId,
            SessionId,
            role,
            "en-US",
            [OutputKind],
            RequiresFallback: false,
            ContextDigest: "end-user-chat-bounded-live-proposal-projection");

        try
        {
            var result = await _runner.RunAsync(packet, proposalOptions, cancellationToken).ConfigureAwait(false);
            if (!MatchesPacket(packet, result))
            {
                return Blocked(
                    LiveMissionProposalRunner.OutcomePacketMismatch,
                    LiveMissionProposalRunner.OutcomePacketMismatch);
            }

            if (result.HasUnsafeValue)
            {
                return Blocked(
                    LiveMissionProposalRunner.OutcomeUnsafeValueRedacted,
                    "unsafe_value_redacted");
            }

            return new EndUserLiveProposalCandidate(
                LiveModelProposalEnvelope.ForMission(MapMissionProposal(result)),
                EndUserLiveProposalMarkers.Accepted,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Blocked(ExceptionOutcomeCode(ex), ExceptionFailureCode(ex));
        }
    }

    private static EndUserLiveProposalCandidate Blocked(string outcomeCode, string? failureCode) =>
        new(LiveModelProposalEnvelope.Blocked(outcomeCode), outcomeCode, failureCode ?? outcomeCode);

    private static bool MatchesPacket(LiveMissionProposalPacket packet, LiveMissionProposalResult result) =>
        string.Equals(packet.PacketId, result.PacketId, StringComparison.Ordinal) &&
        string.Equals(packet.SessionId, result.SessionId, StringComparison.Ordinal) &&
        packet.Role == result.Role &&
        packet.AllowedOutputKinds.Contains(result.OutputKind, StringComparer.Ordinal);

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
            LiveMissionProposalGuardException guard => guard.OutcomeCode,
            ProviderCreditExhaustedException => LiveMissionProposalRunner.OutcomeCreditExhausted,
            ProviderEmptyResponseException => LiveMissionProposalRunner.OutcomeEmptyContent,
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) =>
                LiveMissionProposalRunner.OutcomeSchemaInvalid,
            ProviderMalformedResponseException => LiveMissionProposalRunner.OutcomeMalformedJson,
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) =>
                LiveMissionProposalRunner.OutcomeTimeout,
            TimeoutException => LiveMissionProposalRunner.OutcomeTimeout,
            ProviderException => LiveMissionProposalRunner.OutcomeProviderUnavailable,
            _ => LiveMissionProposalRunner.OutcomeProviderUnavailable
        };

    private static string? ExceptionFailureCode(Exception exception) =>
        exception switch
        {
            LiveMissionProposalGuardException guard => guard.ErrorCode,
            ProviderCreditExhaustedException => "credit_exhausted",
            ProviderEmptyResponseException => "empty_content",
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) => "malformed_schema",
            ProviderMalformedResponseException => "malformed_json",
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => "timeout",
            TimeoutException => "timeout",
            ProviderException => "provider_error",
            _ => "provider_error"
        };

    private sealed class DisabledLiveMissionProposalClient : IModelCompletionClient, IProviderCreditClient
    {
        public Task<ModelCompletionResponse> CompleteAsync(
            ModelCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ModelCompletionResponse>(
                new ProviderUnavailableException("disabled", "Live mission proposal provider is not configured."));

        public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProviderCreditStatus(null, null, 1m, IsExhausted: false));
    }
}

public sealed class EndUserLiveProposalGateway : IEndUserLiveProposalGateway
{
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
