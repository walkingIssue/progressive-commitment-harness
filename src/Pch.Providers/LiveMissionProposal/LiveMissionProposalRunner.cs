using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveMissionProposal;

public sealed class LiveMissionProposalRunner
{
    public const string RejectedRowName = "live_mission_proposal_rejected";
    public const string RejectedRowPacketId = "live_mission_proposal_packet_redacted";
    public const string OutcomeAccepted = "live_mission_proposal_accepted";
    public const string OutcomeDisabled = "live_mission_proposal_disabled";
    public const string OutcomeKeyMissing = "live_mission_proposal_key_missing";
    public const string OutcomeCreditExhausted = "live_mission_proposal_credit_exhausted";
    public const string OutcomeTimeout = "live_mission_proposal_timeout";
    public const string OutcomeEmptyContent = "live_mission_proposal_empty_content";
    public const string OutcomeMalformedJson = "live_mission_proposal_malformed_json";
    public const string OutcomeSchemaInvalid = "live_mission_proposal_schema_invalid";
    public const string OutcomePacketMismatch = "live_mission_proposal_packet_mismatch";
    public const string OutcomeUnsupportedValue = "live_mission_proposal_unsupported_value";
    public const string OutcomeUnsafeValueRedacted = "live_mission_proposal_unsafe_value_redacted";
    public const string OutcomeFallbackDisabled = "live_mission_proposal_fallback_disabled";
    public const string OutcomeProviderUnavailable = "live_mission_proposal_provider_unavailable";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public LiveMissionProposalRunner(
        IModelCompletionClient completionClient,
        IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<LiveMissionProposalResult> RunAsync(
        LiveMissionProposalPacket packet,
        LiveMissionProposalOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidatePacket(packet);
        ValidateOptions(options);

        if (!options.Enabled)
        {
            throw new LiveMissionProposalGuardException(OutcomeDisabled, null);
        }

        if (!options.ApiKeyAvailable)
        {
            throw new LiveMissionProposalGuardException(OutcomeKeyMissing, null);
        }

        if (!options.StructuredOutputSupported)
        {
            throw new LiveMissionProposalGuardException(OutcomeSchemaInvalid, "schema_unsupported");
        }

        if (options.AllowPaidProviderFallback || packet.RequiresFallback)
        {
            throw new LiveMissionProposalGuardException(OutcomeFallbackDisabled, "fallback_disabled");
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
                    throw new ProviderCreditExhaustedException(options.Provider, "Live mission proposal credits are exhausted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderUnavailableException(options.Provider, "Live mission proposal request timed out.");
            }
        }

        try
        {
            var completion = await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for a provider-local live mission proposal. Do not include raw provider payloads, secrets, approval tokens, hold references, booking refs, payment data, or candidate display text."),
                        new ModelMessage(ModelMessageRole.User, CreateSanitizedProbe(packet, options))
                    ],
                    options.ModelFor(packet.Role),
                    "live_mission_proposal",
                    LiveMissionProposalJsonSchema.Schema,
                    Temperature: 0,
                    MaxTokens: options.MaxTokens),
                operationToken).ConfigureAwait(false);

            return ParseCompletion(packet, completion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderUnavailableException(options.Provider, "Live mission proposal request timed out.");
        }
    }

    private static void ValidatePacket(LiveMissionProposalPacket packet)
    {
        if (packet is null ||
            string.IsNullOrWhiteSpace(packet.PacketId) ||
            string.IsNullOrWhiteSpace(packet.SessionId) ||
            !Enum.IsDefined(packet.Role) ||
            packet.AllowedOutputKinds is null ||
            packet.AllowedOutputKinds.Count == 0 ||
            packet.AllowedOutputKinds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ProviderMalformedResponseException("live-mission-proposal", "Live mission proposal packet schema is malformed.");
        }
    }

    private static void ValidateOptions(LiveMissionProposalOptions options)
    {
        if (options is null ||
            string.IsNullOrWhiteSpace(options.InHarnessModelId) ||
            string.IsNullOrWhiteSpace(options.StrongPlannerModelId))
        {
            throw new ProviderMalformedResponseException("live-mission-proposal", "Live mission proposal options schema is malformed.");
        }
    }

    private static LiveMissionProposalResult ParseCompletion(
        LiveMissionProposalPacket packet,
        ModelCompletionResponse completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Live mission proposal provider returned empty content.");
        }

        LiveMissionProposalEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LiveMissionProposalEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live mission proposal provider returned malformed JSON.", ex);
        }

        if (parsed is null ||
            string.IsNullOrWhiteSpace(parsed.PacketId) ||
            string.IsNullOrWhiteSpace(parsed.SessionId) ||
            string.IsNullOrWhiteSpace(parsed.Role) ||
            string.IsNullOrWhiteSpace(parsed.OutputKind) ||
            string.IsNullOrWhiteSpace(parsed.MissionKind) ||
            parsed.Fields is null ||
            parsed.Commitments is null ||
            parsed.PendingConfirmations is null)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live mission proposal provider returned malformed schema.");
        }

        return new LiveMissionProposalResult(
            parsed.PacketId,
            parsed.SessionId,
            ParseRole(parsed.Role),
            parsed.OutputKind,
            ParseMissionKind(parsed.MissionKind),
            parsed.Fields.Select(ToField).ToArray(),
            parsed.Commitments.Select(ToCommitment).ToArray(),
            parsed.PendingConfirmations.Select(ToPendingConfirmation).ToArray(),
            HasUnsafeValues(parsed),
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static string CreateSanitizedProbe(
        LiveMissionProposalPacket packet,
        LiveMissionProposalOptions options) =>
        JsonSerializer.Serialize(new
        {
            packet.PacketId,
            packet.SessionId,
            Role = RoleName(packet.Role),
            packet.Locale,
            packet.AllowedOutputKinds,
            ProviderKind = options.ProviderKind,
            ModelId = options.ModelFor(packet.Role)
        }, JsonOptions);

    private static LiveMissionFieldProposal ToField(LiveMissionFieldEnvelope field) =>
        new(
            field.FieldPath ?? string.Empty,
            field.Value,
            ParseAuthority(field.AuthoritySource),
            field.EvidenceIds ?? []);

    private static LiveMissionCommitmentProposal ToCommitment(LiveMissionCommitmentEnvelope commitment) =>
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

    private static LiveMissionPendingConfirmation ToPendingConfirmation(LiveMissionPendingEnvelope pending) =>
        new(
            pending.ConfirmationId ?? string.Empty,
            pending.FieldPath ?? string.Empty,
            ParsePendingReason(pending.ReasonCode),
            ParseAuthority(pending.AuthoritySource),
            pending.EvidenceIds ?? []);

    internal static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 120 &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '/' or ':' or '.') &&
        !ContainsUnsafeMarker(value);

    internal static bool ContainsUnsafeMarker(string? value)
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
            upper.Contains("TOKEN", StringComparison.Ordinal) ||
            upper.Contains("PROMPT", StringComparison.Ordinal) ||
            upper.Contains("PAYLOAD", StringComparison.Ordinal) ||
            upper.Contains("COMPLETION", StringComparison.Ordinal) ||
            upper.Contains("APPROVAL", StringComparison.Ordinal) ||
            upper.Contains("HOLD", StringComparison.Ordinal) ||
            upper.Contains("BOOKING", StringComparison.Ordinal) ||
            upper.Contains("PAYMENT", StringComparison.Ordinal) ||
            upper.Contains("CANDIDATE_DISPLAY", StringComparison.Ordinal) ||
            upper.Contains("SK-", StringComparison.Ordinal);
    }

    private static bool HasUnsafeValues(LiveMissionProposalEnvelope parsed) =>
        ContainsUnsafeMarker(parsed.PacketId) ||
        ContainsUnsafeMarker(parsed.SessionId) ||
        ContainsUnsafeMarker(parsed.OutputKind) ||
        ContainsUnsafeMarker(parsed.MissionKind) ||
        ContainsUnsafeMarker(parsed.Role) ||
        parsed.Fields.Any(field => ContainsUnsafeMarker(field.Value) || HasUnsafeEvidence(field.EvidenceIds)) ||
        parsed.Commitments.Any(commitment =>
            ContainsUnsafeMarker(commitment.Title) ||
            ContainsUnsafeMarker(commitment.Location) ||
            HasUnsafeEvidence(commitment.EvidenceIds)) ||
        parsed.PendingConfirmations.Any(pending =>
            HasUnsafeEvidence(pending.EvidenceIds));

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

    private sealed record LiveMissionProposalEnvelope(
        string? PacketId,
        string? SessionId,
        string? Role,
        string? OutputKind,
        string? MissionKind,
        IReadOnlyList<LiveMissionFieldEnvelope> Fields,
        IReadOnlyList<LiveMissionCommitmentEnvelope> Commitments,
        IReadOnlyList<LiveMissionPendingEnvelope> PendingConfirmations);

    private sealed record LiveMissionFieldEnvelope(
        string? FieldPath,
        string? Value,
        string? AuthoritySource,
        IReadOnlyList<string>? EvidenceIds);

    private sealed record LiveMissionCommitmentEnvelope(
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

    private sealed record LiveMissionPendingEnvelope(
        string? ConfirmationId,
        string? FieldPath,
        string? ReasonCode,
        string? AuthoritySource,
        IReadOnlyList<string>? EvidenceIds);
}

public sealed class LiveMissionProposalGuardException : ProviderException
{
    public LiveMissionProposalGuardException(string outcomeCode, string? errorCode)
        : base("live-mission-proposal", "Live mission proposal guard blocked execution.")
    {
        OutcomeCode = outcomeCode;
        ErrorCode = errorCode;
    }

    public string OutcomeCode { get; }

    public string? ErrorCode { get; }
}

internal static class LiveMissionProposalJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["packetId", "sessionId", "role", "outputKind", "missionKind", "fields", "commitments", "pendingConfirmations"],
          "properties": {
            "packetId": { "type": "string" },
            "sessionId": { "type": "string" },
            "role": { "type": "string", "enum": ["in_harness_action_generator", "strong_planner"] },
            "outputKind": { "type": "string", "enum": ["mission_proposal"] },
            "missionKind": { "type": "string", "enum": ["vacation", "business", "funeral", "helping_family"] },
            "fields": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["fieldPath", "value", "authoritySource", "evidenceIds"],
                "properties": {
                  "fieldPath": { "type": "string" },
                  "value": { "type": "string" },
                  "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                  "evidenceIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "commitments": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["commitmentId", "commitmentKind", "title", "startsAt", "endsAt", "location", "isIrreversible", "requiresSpend", "priority", "authoritySource", "evidenceIds"],
                "properties": {
                  "commitmentId": { "type": "string" },
                  "commitmentKind": { "type": "string", "enum": ["travel", "lodging", "dining", "activity", "family_support", "work"] },
                  "title": { "type": "string" },
                  "startsAt": { "type": ["string", "null"] },
                  "endsAt": { "type": ["string", "null"] },
                  "location": { "type": ["string", "null"] },
                  "isIrreversible": { "type": "boolean" },
                  "requiresSpend": { "type": "boolean" },
                  "priority": { "type": "string", "enum": ["normal", "high", "critical"] },
                  "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                  "evidenceIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "pendingConfirmations": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["confirmationId", "fieldPath", "reasonCode", "authoritySource", "evidenceIds"],
                "properties": {
                  "confirmationId": { "type": "string" },
                  "fieldPath": { "type": "string" },
                  "reasonCode": { "type": "string", "enum": ["needs_user_confirmation", "needs_date_confirmation", "needs_budget_confirmation", "needs_location_confirmation"] },
                  "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                  "evidenceIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            }
          }
        }
        """;
}
