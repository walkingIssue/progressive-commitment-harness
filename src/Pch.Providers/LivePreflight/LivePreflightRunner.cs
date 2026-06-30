using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LivePreflight;

public sealed class LivePreflightRunner
{
    public const string RejectedRowName = "live_preflight_rejected";
    public const string RejectedRowPacketId = "live_preflight_packet_redacted";
    public const string OutcomeAccepted = "live_preflight_accepted";
    public const string OutcomeDisabled = "live_preflight_disabled";
    public const string OutcomeKeyMissing = "live_preflight_key_missing";
    public const string OutcomeCreditExhausted = "live_preflight_credit_exhausted";
    public const string OutcomeTimeout = "live_preflight_timeout";
    public const string OutcomeEmptyContent = "live_preflight_empty_content";
    public const string OutcomeMalformedJson = "live_preflight_malformed_json";
    public const string OutcomeSchemaUnsupported = "live_preflight_schema_unsupported";
    public const string OutcomePacketMismatch = "live_preflight_packet_mismatch";
    public const string OutcomeFallbackDisabled = "live_preflight_fallback_disabled";
    public const string OutcomeProviderUnavailable = "live_preflight_provider_unavailable";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public LivePreflightRunner(
        IModelCompletionClient completionClient,
        IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<LivePreflightResult> RunAsync(
        LivePreflightPacket packet,
        LivePreflightOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidatePacket(packet);
        if (!options.Enabled)
        {
            throw new LivePreflightGuardException(OutcomeDisabled, null);
        }

        if (!options.ApiKeyAvailable)
        {
            throw new LivePreflightGuardException(OutcomeKeyMissing, null);
        }

        if (!options.StructuredOutputSupported)
        {
            throw new LivePreflightGuardException(OutcomeSchemaUnsupported, null);
        }

        if (options.AllowPaidProviderFallback || packet.Roles.Any(role => role.RequiresFallback))
        {
            throw new LivePreflightGuardException(OutcomeFallbackDisabled, "fallback_disabled");
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
                    throw new ProviderCreditExhaustedException(options.Provider, "Live preflight credits are exhausted.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProviderUnavailableException(options.Provider, "Live preflight request timed out.");
            }
        }

        try
        {
            var completion = await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for a provider live preflight. Do not include raw prompts, provider payloads, secrets, approval tokens, hold references, or candidate display text."),
                        new ModelMessage(ModelMessageRole.User, CreateSanitizedProbe(packet, options))
                    ],
                    options.ModelFor(LiveModelRole.InHarnessActionGenerator),
                    "live_preflight_probe",
                    LivePreflightJsonSchema.Schema,
                    Temperature: 0,
                    MaxTokens: options.MaxTokens),
                operationToken).ConfigureAwait(false);

            return ParseCompletion(packet, options, completion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderUnavailableException(options.Provider, "Live preflight request timed out.");
        }
    }

    private static void ValidatePacket(LivePreflightPacket packet)
    {
        if (packet is null ||
            string.IsNullOrWhiteSpace(packet.PacketId) ||
            packet.Roles is null ||
            packet.Roles.Count == 0 ||
            packet.Roles.Any(role => role is null || string.IsNullOrWhiteSpace(role.ProbeId) || !Enum.IsDefined(role.Role)) ||
            packet.Roles.Select(role => role.Role).Distinct().Count() != packet.Roles.Count)
        {
            throw new ProviderMalformedResponseException("live-preflight", "Live preflight packet is malformed.");
        }
    }

    private static LivePreflightResult ParseCompletion(
        LivePreflightPacket packet,
        LivePreflightOptions options,
        ModelCompletionResponse completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            throw new ProviderEmptyResponseException(completion.Provider, "Live preflight provider returned empty content.");
        }

        LivePreflightEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LivePreflightEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live preflight provider returned malformed JSON.", ex);
        }

        if (parsed?.Roles is null || string.IsNullOrWhiteSpace(parsed.PacketId))
        {
            throw new ProviderMalformedResponseException(completion.Provider, "Live preflight provider returned malformed schema.");
        }

        var roles = parsed.Roles.Select(role => new LivePreflightRoleResult(
            ParseRole(role.Role),
            role.ProbeId ?? string.Empty,
            role.ModelId ?? string.Empty,
            options.ProviderKind,
            role.OutputKind ?? string.Empty)).ToArray();

        return new LivePreflightResult(
            parsed.PacketId,
            roles,
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static string CreateSanitizedProbe(LivePreflightPacket packet, LivePreflightOptions options)
    {
        var roles = packet.Roles.Select(role => new
        {
            Role = RoleName(role.Role),
            role.ProbeId,
            ModelId = options.ModelFor(role.Role),
            options.ProviderKind
        });

        return JsonSerializer.Serialize(new
        {
            packet.PacketId,
            packet.Locale,
            Roles = roles
        }, JsonOptions);
    }

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

    private sealed record LivePreflightEnvelope(
        string? PacketId,
        IReadOnlyList<LivePreflightRoleEnvelope>? Roles);

    private sealed record LivePreflightRoleEnvelope(
        string? Role,
        string? ProbeId,
        string? ModelId,
        string? OutputKind);
}

public sealed class LivePreflightGuardException : ProviderException
{
    public LivePreflightGuardException(string outcomeCode, string? errorCode)
        : base("live-preflight", "Live preflight guard blocked execution.")
    {
        OutcomeCode = outcomeCode;
        ErrorCode = errorCode;
    }

    public string OutcomeCode { get; }

    public string? ErrorCode { get; }
}

internal static class LivePreflightJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["packetId", "roles"],
          "properties": {
            "packetId": { "type": "string" },
            "roles": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["role", "probeId", "modelId", "outputKind"],
                "properties": {
                  "role": { "type": "string", "enum": ["in_harness_action_generator", "strong_planner"] },
                  "probeId": { "type": "string" },
                  "modelId": { "type": "string" },
                  "outputKind": { "type": "string", "enum": ["structured_output_ready"] }
                }
              }
            }
          }
        }
        """;
}
