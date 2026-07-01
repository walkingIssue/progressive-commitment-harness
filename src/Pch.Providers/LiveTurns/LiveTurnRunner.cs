using System.Diagnostics;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveTurns;

public sealed class LiveTurnRunner
{
    public const string RejectedRowName = "live_turn_rejected";
    public const string RejectedPacketId = "live_turn_packet_redacted";
    public const string RejectedRunId = "live_turn_run_redacted";
    public const string RejectedTurnId = "live_turn_turn_redacted";

    public const string OutcomeAccepted = "live_turn_accepted";
    public const string OutcomeDisabled = "live_turn_disabled";
    public const string OutcomeKeyMissing = "live_turn_key_missing";
    public const string OutcomeCreditExhausted = "live_turn_credit_exhausted";
    public const string OutcomeFallbackDisabled = "live_turn_fallback_disabled";
    public const string OutcomePacketMismatch = "live_turn_packet_mismatch";
    public const string OutcomeUnsupportedValue = "live_turn_unsupported_value";
    public const string OutcomeUnsafeValueRedacted = "live_turn_unsafe_value_redacted";
    public const string OutcomeProviderHttp4xx = "live_turn_provider_http_4xx";
    public const string OutcomeProviderHttp5xx = "live_turn_provider_http_5xx";
    public const string OutcomeProviderRateLimited = "live_turn_provider_rate_limited";
    public const string OutcomeProviderTimeout = "live_turn_provider_timeout";
    public const string OutcomeProviderEmptyContent = "live_turn_provider_empty_content";
    public const string OutcomeProviderMalformedJson = "live_turn_provider_malformed_json";
    public const string OutcomeProviderSchemaInvalid = "live_turn_provider_schema_invalid";
    public const string OutcomeProviderUpstreamModelUnavailable = "live_turn_provider_upstream_model_unavailable";
    public const string OutcomeProviderNetworkError = "live_turn_provider_network_error";
    public const string OutcomeProviderUnknownError = "live_turn_provider_unknown_error";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public LiveTurnRunner(IModelCompletionClient completionClient, IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<LiveTurnResult> RunAsync(
        LiveTurnPacket packet,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidatePacket(packet);
        ValidateOptions(options);

        if (!options.Enabled)
        {
            throw new LiveTurnGuardException(OutcomeDisabled, ProviderFailureClass.ProviderDisabled);
        }

        if (!options.ApiKeyAvailable)
        {
            throw new LiveTurnGuardException(OutcomeKeyMissing, ProviderFailureClass.ProviderKeyMissing);
        }

        if (!options.StructuredOutputSupported)
        {
            throw new ProviderMalformedResponseException(options.Provider, "Live turn provider schema is unsupported.");
        }

        if (options.AllowPaidProviderFallback || packet.RequiresFallback)
        {
            throw new LiveTurnGuardException(OutcomeFallbackDisabled, ProviderFailureClass.ProviderFallbackDisabled);
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
                    throw new ProviderCreditExhaustedException(options.Provider, "Live turn credits are exhausted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderUnavailableException(options.Provider, "Live turn request timed out.");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var completion = await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for a provider-local live turn. Do not include raw provider payloads, secrets, approval tokens, hold references, booking refs, payment data, or candidate display text."),
                        new ModelMessage(ModelMessageRole.User, CreateSanitizedProbe(packet, options))
                    ],
                    options.ModelFor(packet.Role),
                    "live_turn_output",
                    LiveTurnJsonSchema.Schema,
                    Temperature: 0,
                    MaxTokens: options.MaxTokens),
                operationToken).ConfigureAwait(false);

            stopwatch.Stop();
            return ParseCompletion(packet, completion, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderUnavailableException(options.Provider, "Live turn request timed out.");
        }
    }

    private static void ValidatePacket(LiveTurnPacket packet)
    {
        if (packet is null ||
            string.IsNullOrWhiteSpace(packet.RunId) ||
            string.IsNullOrWhiteSpace(packet.TurnId) ||
            string.IsNullOrWhiteSpace(packet.PacketId) ||
            string.IsNullOrWhiteSpace(packet.SessionId) ||
            !Enum.IsDefined(packet.Role) ||
            packet.AllowedOutputKinds is null ||
            packet.AllowedOutputKinds.Count == 0 ||
            packet.AllowedOutputKinds.Any(kind => !Enum.IsDefined(kind)) ||
            packet.TrustedCandidates is null ||
            packet.TrustedCandidates.Any(candidate =>
                candidate is null ||
                string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                string.IsNullOrWhiteSpace(candidate.SlotId) ||
                !Enum.IsDefined(candidate.Category)))
        {
            throw new ProviderMalformedResponseException("live-turn", "Live turn packet schema is malformed.");
        }
    }

    private static void ValidateOptions(LiveTurnOptions options)
    {
        if (options is null ||
            string.IsNullOrWhiteSpace(options.InHarnessModelId) ||
            string.IsNullOrWhiteSpace(options.StrongPlannerModelId))
        {
            throw new ProviderMalformedResponseException("live-turn", "Live turn options schema is malformed.");
        }
    }

    private static LiveTurnResult ParseCompletion(
        LiveTurnPacket packet,
        ModelCompletionResponse completion,
        TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Live turn provider returned empty content.");
        }

        LiveTurnEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LiveTurnEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live turn provider returned malformed JSON.", ex);
        }

        if (parsed is null ||
            string.IsNullOrWhiteSpace(parsed.RunId) ||
            string.IsNullOrWhiteSpace(parsed.TurnId) ||
            string.IsNullOrWhiteSpace(parsed.PacketId) ||
            string.IsNullOrWhiteSpace(parsed.SessionId) ||
            string.IsNullOrWhiteSpace(parsed.Role) ||
            string.IsNullOrWhiteSpace(parsed.OutputKind))
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live turn provider returned malformed schema.");
        }

        var outputKind = ParseOutputKind(parsed.OutputKind);
        var missionProposal = parsed.MissionProposal is null ? null : ToMissionProposal(parsed, packet, completion);
        var pendingQuestion = parsed.PendingQuestion is null ? null : ToPendingQuestion(parsed.PendingQuestion);
        var choiceSet = parsed.ChoiceSet is null ? null : ToChoiceSet(parsed.ChoiceSet);
        var summaryNotice = parsed.SummaryNotice is null ? null : ToSummaryNotice(parsed.SummaryNotice);

        return new LiveTurnResult(
            parsed.RunId,
            parsed.TurnId,
            parsed.PacketId,
            parsed.SessionId,
            ParseRole(parsed.Role),
            outputKind,
            missionProposal,
            pendingQuestion,
            choiceSet,
            summaryNotice,
            HasUnsafeValues(parsed),
            duration,
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static string CreateSanitizedProbe(LiveTurnPacket packet, LiveTurnOptions options)
    {
        var candidates = packet.TrustedCandidates.Select(candidate => new
        {
            candidate.CandidateId,
            candidate.SlotId,
            Category = CategoryName(candidate.Category)
        });

        return JsonSerializer.Serialize(new
        {
            packet.RunId,
            packet.TurnId,
            packet.PacketId,
            packet.SessionId,
            Role = RoleName(packet.Role),
            packet.Locale,
            AllowedOutputKinds = packet.AllowedOutputKinds.Select(OutputKindName).ToArray(),
            TrustedCandidates = candidates,
            ProviderKind = options.ProviderKind,
            ModelId = options.ModelFor(packet.Role)
        }, JsonOptions);
    }

    private static LiveMissionProposalResult ToMissionProposal(
        LiveTurnEnvelope parsed,
        LiveTurnPacket packet,
        ModelCompletionResponse completion)
    {
        var proposal = parsed.MissionProposal!;
        return new LiveMissionProposalResult(
            parsed.PacketId ?? string.Empty,
            parsed.SessionId ?? string.Empty,
            ParseRole(parsed.Role),
            OutputKindName(LiveTurnOutputKind.MissionProposal),
            ParseMissionKind(proposal.MissionKind),
            (proposal.Fields ?? []).Select(ToField).ToArray(),
            (proposal.Commitments ?? []).Select(ToCommitment).ToArray(),
            (proposal.PendingConfirmations ?? []).Select(ToPendingConfirmation).ToArray(),
            HasUnsafeValues(parsed),
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static LiveMissionFieldProposal ToField(LiveTurnFieldEnvelope field) =>
        new(
            field.FieldPath ?? string.Empty,
            field.Value,
            ParseAuthority(field.AuthoritySource),
            field.EvidenceIds ?? []);

    private static LiveMissionCommitmentProposal ToCommitment(LiveTurnCommitmentEnvelope commitment) =>
        new(
            commitment.CommitmentId ?? string.Empty,
            ParseCommitmentKind(commitment.CommitmentKind),
            commitment.Title,
            ParseDate(commitment.StartsAt),
            ParseDate(commitment.EndsAt),
            commitment.Location,
            commitment.IsIrreversible,
            commitment.RequiresSpend,
            ParsePriority(commitment.Priority),
            ParseAuthority(commitment.AuthoritySource),
            commitment.EvidenceIds ?? []);

    private static LiveMissionPendingConfirmation ToPendingConfirmation(LiveTurnPendingConfirmationEnvelope pending) =>
        new(
            pending.ConfirmationId ?? string.Empty,
            pending.FieldPath ?? string.Empty,
            ParsePendingReason(pending.ReasonCode),
            ParseAuthority(pending.AuthoritySource),
            pending.EvidenceIds ?? []);

    private static LiveTurnPendingQuestion ToPendingQuestion(LiveTurnPendingQuestionEnvelope pending) =>
        new(
            pending.QuestionId ?? string.Empty,
            pending.FieldPath ?? string.Empty,
            ParsePendingReason(pending.ReasonCode),
            pending.PromptText);

    private static LiveTurnChoiceSet ToChoiceSet(LiveTurnChoiceSetEnvelope choiceSet) =>
        new(
            choiceSet.ChoiceSetId ?? string.Empty,
            (choiceSet.Options ?? []).Select(ToChoiceOption).ToArray(),
            ParseMood(choiceSet.UiMood),
            choiceSet.FramingText);

    private static LiveTurnChoiceOption ToChoiceOption(LiveTurnChoiceOptionEnvelope option) =>
        new(
            option.CandidateId ?? string.Empty,
            option.SlotId ?? string.Empty,
            ParseCategory(option.Category),
            option.Label,
            option.Rationale);

    private static LiveTurnSummaryNotice ToSummaryNotice(LiveTurnSummaryNoticeEnvelope notice) =>
        new(ParseNoticeKind(notice.NoticeKind), notice.SummaryText);

    internal static bool IsSafeIdentifier(string? value) =>
        LiveMissionProposalRunner.IsSafeIdentifier(value);

    internal static bool ContainsUnsafeMarker(string? value) =>
        LiveMissionProposalRunner.ContainsUnsafeMarker(value);

    private static bool HasUnsafeValues(LiveTurnEnvelope parsed) =>
        ContainsUnsafeMarker(parsed.RunId) ||
        ContainsUnsafeMarker(parsed.TurnId) ||
        ContainsUnsafeMarker(parsed.PacketId) ||
        ContainsUnsafeMarker(parsed.SessionId) ||
        ContainsUnsafeMarker(parsed.Role) ||
        ContainsUnsafeMarker(parsed.OutputKind) ||
        HasUnsafeMissionValues(parsed.MissionProposal) ||
        ContainsUnsafeMarker(parsed.PendingQuestion?.PromptText) ||
        ContainsUnsafeMarker(parsed.PendingQuestion?.QuestionId) ||
        ContainsUnsafeMarker(parsed.PendingQuestion?.FieldPath) ||
        ContainsUnsafeMarker(parsed.ChoiceSet?.ChoiceSetId) ||
        ContainsUnsafeMarker(parsed.ChoiceSet?.FramingText) ||
        parsed.ChoiceSet?.Options?.Any(option =>
            ContainsUnsafeMarker(option.CandidateId) ||
            ContainsUnsafeMarker(option.SlotId) ||
            ContainsUnsafeMarker(option.Label) ||
            ContainsUnsafeMarker(option.Rationale)) == true ||
        ContainsUnsafeMarker(parsed.SummaryNotice?.SummaryText);

    private static bool HasUnsafeMissionValues(LiveTurnMissionProposalEnvelope? proposal) =>
        proposal is not null &&
        (ContainsUnsafeMarker(proposal.MissionKind) ||
        proposal.Fields?.Any(field => ContainsUnsafeMarker(field.Value) || HasUnsafeEvidence(field.EvidenceIds)) == true ||
        proposal.Commitments?.Any(commitment =>
            ContainsUnsafeMarker(commitment.Title) ||
            ContainsUnsafeMarker(commitment.Location) ||
            HasUnsafeEvidence(commitment.EvidenceIds)) == true ||
        proposal.PendingConfirmations?.Any(pending => HasUnsafeEvidence(pending.EvidenceIds)) == true);

    private static bool HasUnsafeEvidence(IReadOnlyList<string>? evidenceIds) =>
        evidenceIds?.Any(id => !IsSafeIdentifier(id)) == true;

    private static LiveModelRole ParseRole(string? role) =>
        role switch
        {
            "in_harness_action_generator" => LiveModelRole.InHarnessActionGenerator,
            "strong_planner" => LiveModelRole.StrongPlanner,
            _ => (LiveModelRole)999
        };

    private static string RoleName(LiveModelRole role) =>
        role switch
        {
            LiveModelRole.InHarnessActionGenerator => "in_harness_action_generator",
            LiveModelRole.StrongPlanner => "strong_planner",
            _ => "unknown"
        };

    internal static LiveTurnOutputKind ParseOutputKind(string? outputKind) =>
        outputKind switch
        {
            "mission_proposal" => LiveTurnOutputKind.MissionProposal,
            "pending_confirmation_question" => LiveTurnOutputKind.PendingConfirmationQuestion,
            "choice_set" => LiveTurnOutputKind.ChoiceSet,
            "summary_fallback_notice" => LiveTurnOutputKind.SummaryFallbackNotice,
            _ => (LiveTurnOutputKind)999
        };

    internal static string OutputKindName(LiveTurnOutputKind outputKind) =>
        outputKind switch
        {
            LiveTurnOutputKind.MissionProposal => "mission_proposal",
            LiveTurnOutputKind.PendingConfirmationQuestion => "pending_confirmation_question",
            LiveTurnOutputKind.ChoiceSet => "choice_set",
            LiveTurnOutputKind.SummaryFallbackNotice => "summary_fallback_notice",
            _ => "unknown"
        };

    internal static string CategoryName(LiveTurnCandidateCategory category) =>
        category switch
        {
            LiveTurnCandidateCategory.Dining => "dining",
            LiveTurnCandidateCategory.Activity => "activity",
            LiveTurnCandidateCategory.Transit => "transit",
            LiveTurnCandidateCategory.Downtime => "downtime",
            LiveTurnCandidateCategory.Lodging => "lodging",
            _ => "unknown"
        };

    private static LiveTurnCandidateCategory ParseCategory(string? category) =>
        category switch
        {
            "dining" => LiveTurnCandidateCategory.Dining,
            "activity" => LiveTurnCandidateCategory.Activity,
            "transit" => LiveTurnCandidateCategory.Transit,
            "downtime" => LiveTurnCandidateCategory.Downtime,
            "lodging" => LiveTurnCandidateCategory.Lodging,
            _ => (LiveTurnCandidateCategory)999
        };

    private static LiveTurnUiMood ParseMood(string? mood) =>
        mood switch
        {
            "calm_morning" => LiveTurnUiMood.CalmMorning,
            "lively_food" => LiveTurnUiMood.LivelyFood,
            "reflective_culture" => LiveTurnUiMood.ReflectiveCulture,
            "soft_nature" => LiveTurnUiMood.SoftNature,
            "restorative_downtime" => LiveTurnUiMood.RestorativeDowntime,
            "logistics" => LiveTurnUiMood.Logistics,
            _ => LiveTurnUiMood.Unspecified
        };

    private static LiveTurnNoticeKind ParseNoticeKind(string? noticeKind) =>
        noticeKind switch
        {
            "summary" => LiveTurnNoticeKind.Summary,
            "fallback" => LiveTurnNoticeKind.Fallback,
            "provider_blocked" => LiveTurnNoticeKind.ProviderBlocked,
            _ => (LiveTurnNoticeKind)999
        };

    private static LiveMissionKind ParseMissionKind(string? missionKind) =>
        missionKind switch
        {
            "vacation" => LiveMissionKind.Vacation,
            "business" => LiveMissionKind.Business,
            "funeral" => LiveMissionKind.Funeral,
            "helping_family" or "family_support" => LiveMissionKind.HelpingFamily,
            _ => (LiveMissionKind)999
        };

    private static LiveMissionAuthoritySource ParseAuthority(string? authoritySource) =>
        authoritySource switch
        {
            "user_stated" => LiveMissionAuthoritySource.UserStated,
            "model_inference_pending_confirmation" => LiveMissionAuthoritySource.ModelInferencePendingConfirmation,
            "trusted_provider" => LiveMissionAuthoritySource.TrustedProvider,
            _ => (LiveMissionAuthoritySource)999
        };

    private static LiveMissionCommitmentKind ParseCommitmentKind(string? commitmentKind) =>
        commitmentKind switch
        {
            "travel" => LiveMissionCommitmentKind.Travel,
            "lodging" => LiveMissionCommitmentKind.Lodging,
            "dining" => LiveMissionCommitmentKind.Dining,
            "activity" => LiveMissionCommitmentKind.Activity,
            "family_support" => LiveMissionCommitmentKind.FamilySupport,
            "work" => LiveMissionCommitmentKind.Work,
            _ => (LiveMissionCommitmentKind)999
        };

    private static LiveMissionCommitmentPriority ParsePriority(string? priority) =>
        priority switch
        {
            "normal" => LiveMissionCommitmentPriority.Normal,
            "high" => LiveMissionCommitmentPriority.High,
            "critical" => LiveMissionCommitmentPriority.Critical,
            _ => (LiveMissionCommitmentPriority)999
        };

    private static LiveMissionPendingReason ParsePendingReason(string? reasonCode) =>
        reasonCode switch
        {
            "needs_user_confirmation" => LiveMissionPendingReason.NeedsUserConfirmation,
            "needs_date_confirmation" => LiveMissionPendingReason.NeedsDateConfirmation,
            "needs_budget_confirmation" => LiveMissionPendingReason.NeedsBudgetConfirmation,
            "needs_location_confirmation" => LiveMissionPendingReason.NeedsLocationConfirmation,
            _ => (LiveMissionPendingReason)999
        };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

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

    private sealed record LiveTurnEnvelope(
        string? RunId,
        string? TurnId,
        string? PacketId,
        string? SessionId,
        string? Role,
        string? OutputKind,
        LiveTurnMissionProposalEnvelope? MissionProposal,
        LiveTurnPendingQuestionEnvelope? PendingQuestion,
        LiveTurnChoiceSetEnvelope? ChoiceSet,
        LiveTurnSummaryNoticeEnvelope? SummaryNotice);

    private sealed record LiveTurnMissionProposalEnvelope(
        string? MissionKind,
        IReadOnlyList<LiveTurnFieldEnvelope>? Fields,
        IReadOnlyList<LiveTurnCommitmentEnvelope>? Commitments,
        IReadOnlyList<LiveTurnPendingConfirmationEnvelope>? PendingConfirmations);

    private sealed record LiveTurnFieldEnvelope(
        string? FieldPath,
        string? Value,
        string? AuthoritySource,
        IReadOnlyList<string>? EvidenceIds);

    private sealed record LiveTurnCommitmentEnvelope(
        string? CommitmentId,
        string? CommitmentKind,
        string? Title,
        string? StartsAt,
        string? EndsAt,
        string? Location,
        bool IsIrreversible,
        bool RequiresSpend,
        string? Priority,
        string? AuthoritySource,
        IReadOnlyList<string>? EvidenceIds);

    private sealed record LiveTurnPendingConfirmationEnvelope(
        string? ConfirmationId,
        string? FieldPath,
        string? ReasonCode,
        string? AuthoritySource,
        IReadOnlyList<string>? EvidenceIds);

    private sealed record LiveTurnPendingQuestionEnvelope(
        string? QuestionId,
        string? FieldPath,
        string? ReasonCode,
        string? PromptText);

    private sealed record LiveTurnChoiceSetEnvelope(
        string? ChoiceSetId,
        IReadOnlyList<LiveTurnChoiceOptionEnvelope>? Options,
        string? UiMood,
        string? FramingText);

    private sealed record LiveTurnChoiceOptionEnvelope(
        string? CandidateId,
        string? SlotId,
        string? Category,
        string? Label,
        string? Rationale);

    private sealed record LiveTurnSummaryNoticeEnvelope(
        string? NoticeKind,
        string? SummaryText);
}

public sealed class LiveTurnGuardException : ProviderException
{
    public LiveTurnGuardException(string outcomeCode, ProviderFailureClass failureClass)
        : base("live-turn", "Live turn guard blocked execution.")
    {
        OutcomeCode = outcomeCode;
        FailureClass = failureClass;
    }

    public string OutcomeCode { get; }

    public ProviderFailureClass FailureClass { get; }
}
