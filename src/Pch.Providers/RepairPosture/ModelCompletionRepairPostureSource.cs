using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.RepairPosture;

public sealed class ModelCompletionRepairPostureSource : IRepairPostureSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public ModelCompletionRepairPostureSource(
        IModelCompletionClient completionClient,
        IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<RepairPostureResult> SuggestAsync(
        RepairPosturePacket packet,
        RepairPostureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePacket(packet);
        var live = options?.Live ?? new RepairPostureLiveOptions();
        if (!live.Enabled)
        {
            throw new RepairPostureGuardException(RepairPostureEvaluator.OutcomeLiveDisabled, null);
        }

        if (!live.ApiKeyAvailable)
        {
            throw new RepairPostureGuardException(RepairPostureEvaluator.OutcomeKeyMissing, null);
        }

        if (live.AllowPaidProviderFallback)
        {
            throw new RepairPostureGuardException(RepairPostureEvaluator.OutcomeProviderUnavailable, "fallback_disabled");
        }

        using var timeout = CreateTimeoutSource(live.Timeout, cancellationToken, out var operationToken);

        if (live.CreditGuardEnabled)
        {
            try
            {
                var credits = _creditClient is null
                    ? new ProviderCreditStatus(null, null, null, IsExhausted: true)
                    : await _creditClient.GetCreditStatusAsync(operationToken).ConfigureAwait(false);
                if (credits.IsExhausted)
                {
                    throw new ProviderCreditExhaustedException(live.Provider, "Repair posture provider credits are exhausted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderUnavailableException(live.Provider, "Repair posture request timed out.");
            }
        }

        try
        {
            var completion = await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for provider-local repair posture suggestions. Do not include raw prompts, provider payloads, display names, approval tokens, hold references, or secrets."),
                        new ModelMessage(ModelMessageRole.User, CreateSanitizedPrompt(packet))
                    ],
                    live.Model,
                    "repair_posture_suggestions",
                    RepairPostureJsonSchema.Schema,
                    Temperature: 0,
                    MaxTokens: 1_000),
                operationToken).ConfigureAwait(false);

            return ParseCompletion(packet, completion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderUnavailableException(live.Provider, "Repair posture request timed out.");
        }
    }

    private static void ValidatePacket(RepairPosturePacket packet)
    {
        if (packet is null ||
            string.IsNullOrWhiteSpace(packet.PacketId) ||
            packet.Nodes is null ||
            packet.Nodes.Count == 0)
        {
            throw new ProviderMalformedResponseException("repair-posture", "Repair posture packet is malformed.");
        }
    }

    private static RepairPostureResult ParseCompletion(
        RepairPosturePacket packet,
        ModelCompletionResponse completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Repair posture provider returned empty content.");
        }

        RepairPostureCompletionEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RepairPostureCompletionEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Repair posture provider returned malformed JSON.", ex);
        }

        if (parsed?.Suggestions is null || string.IsNullOrWhiteSpace(parsed.PacketId))
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Repair posture provider returned malformed schema.");
        }

        return new RepairPostureResult(
            parsed.PacketId,
            parsed.Suggestions.Select(suggestion => new RepairSuggestion(
                suggestion.NodeId ?? string.Empty,
                ParseMode(suggestion.Mode),
                ParseReason(suggestion.ReasonCode),
                suggestion.AffectedNodeCount ?? 0)).ToArray(),
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static RepairMode ParseMode(string? mode) =>
        mode switch
        {
            "keep" => RepairMode.Keep,
            "replan_day" => RepairMode.ReplanDay,
            "reselect_candidate" => RepairMode.ReselectCandidate,
            "ask_user" => RepairMode.AskUser,
            "blocked_review" => RepairMode.BlockedReview,
            _ => (RepairMode)999
        };

    private static RepairReasonCode ParseReason(string? reasonCode) =>
        reasonCode switch
        {
            "no_repair_needed" => RepairReasonCode.NoRepairNeeded,
            "downstream_day_impact" => RepairReasonCode.DownstreamDayImpact,
            "candidate_invalidated" => RepairReasonCode.CandidateInvalidated,
            "needs_user_confirmation" => RepairReasonCode.NeedsUserConfirmation,
            "availability_or_hold_risk" => RepairReasonCode.AvailabilityOrHoldRisk,
            "blocked_dependency" => RepairReasonCode.BlockedDependency,
            _ => RepairReasonCode.UnsupportedOrMalformed
        };

    private static string CreateSanitizedPrompt(RepairPosturePacket packet)
    {
        var nodes = packet.Nodes.Select(node => new
        {
            node.NodeId,
            node.NodeKind,
            node.Status,
            node.DownstreamDependencyCount,
            node.UserConfirmationRequired,
            node.HasAvailabilityOrHold,
            EvidenceCount = node.EvidenceIds?.Count ?? 0
        });

        return JsonSerializer.Serialize(new
        {
            packet.PacketId,
            packet.Locale,
            Nodes = nodes
        }, JsonOptions);
    }

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

    private sealed record RepairPostureCompletionEnvelope(
        string? PacketId,
        IReadOnlyList<RepairPostureCompletionSuggestion>? Suggestions);

    private sealed record RepairPostureCompletionSuggestion(
        string? NodeId,
        string? Mode,
        string? ReasonCode,
        int? AffectedNodeCount);
}

public sealed class RepairPostureGuardException : ProviderException
{
    public RepairPostureGuardException(string outcomeCode, string? errorCode)
        : base("repair-posture", "Repair posture live provider guard blocked execution.")
    {
        OutcomeCode = outcomeCode;
        ErrorCode = errorCode;
    }

    public string OutcomeCode { get; }

    public string? ErrorCode { get; }
}

internal static class RepairPostureJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["packetId", "suggestions"],
          "properties": {
            "packetId": { "type": "string" },
            "suggestions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["nodeId", "mode", "reasonCode", "affectedNodeCount"],
                "properties": {
                  "nodeId": { "type": "string" },
                  "mode": { "type": "string", "enum": ["keep", "replan_day", "reselect_candidate", "ask_user", "blocked_review"] },
                  "reasonCode": { "type": "string", "enum": ["no_repair_needed", "downstream_day_impact", "candidate_invalidated", "needs_user_confirmation", "availability_or_hold_risk", "blocked_dependency"] },
                  "affectedNodeCount": { "type": "integer", "minimum": 0 }
                }
              }
            }
          }
        }
        """;
}
