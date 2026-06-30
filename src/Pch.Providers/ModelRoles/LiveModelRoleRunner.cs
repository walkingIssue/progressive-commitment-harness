using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.ModelRoles;

public sealed class LiveModelRoleRunner
{
    public const string RejectedRowName = "live_model_run_rejected";
    public const string RejectedRowPacketId = "live_model_packet_redacted";
    public const string OutcomeAccepted = "live_model_output_accepted";
    public const string OutcomeLiveModeDisabled = "live_model_disabled";
    public const string OutcomeKeyMissing = "live_model_key_missing";
    public const string OutcomeCreditExhausted = "live_model_credit_exhausted";
    public const string OutcomeFallbackDisabled = "live_model_fallback_disabled";
    public const string OutcomeTimeout = "live_model_timeout";
    public const string OutcomeEmptyContent = "live_model_empty_content";
    public const string OutcomeMalformedSchema = "live_model_malformed_schema";
    public const string OutcomePacketMismatch = "live_model_packet_mismatch";
    public const string OutcomeUnsupportedOutput = "live_model_unsupported_output";
    public const string OutcomeProviderUnavailable = "live_model_provider_unavailable";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IModelCompletionClient _completionClient;
    private readonly IProviderCreditClient? _creditClient;

    public LiveModelRoleRunner(
        IModelCompletionClient completionClient,
        IProviderCreditClient? creditClient = null)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
        _creditClient = creditClient;
    }

    public async Task<LiveModelRunResult> RunAsync(
        LiveModelRunPacket packet,
        LiveModelRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var malformed = ValidatePacket(packet);
        if (malformed is not null)
        {
            return malformed;
        }

        var runnerOptions = options.RunnerOptions ?? throw new ArgumentNullException(nameof(options));
        var registry = options.Registry ?? LiveModelRoleRegistry.FromOptions(runnerOptions);
        var role = registry.Resolve(packet.Role);
        if (role is null || !role.IsConfigured)
        {
            return Rejected(OutcomeMalformedSchema);
        }

        if (!runnerOptions.LiveModeEnabled)
        {
            return Rejected(OutcomeLiveModeDisabled, role.Role, role.ModelId);
        }

        if (!runnerOptions.ApiKeyAvailable)
        {
            return Rejected(OutcomeKeyMissing, role.Role, role.ModelId);
        }

        if (packet.RequiresFallback && runnerOptions.FallbackPolicy == LiveModelFallbackPolicy.Disabled)
        {
            return Rejected(OutcomeFallbackDisabled, role.Role, role.ModelId);
        }

        if (runnerOptions.CreditGuardEnabled)
        {
            try
            {
                var credits = _creditClient is null
                    ? new ProviderCreditStatus(null, null, null, IsExhausted: true)
                    : await _creditClient.GetCreditStatusAsync(cancellationToken).ConfigureAwait(false);
                if (credits.IsExhausted)
                {
                    return Rejected(OutcomeCreditExhausted, role.Role, role.ModelId, "credit_exhausted");
                }
            }
            catch (ProviderCreditExhaustedException)
            {
                return Rejected(OutcomeCreditExhausted, role.Role, role.ModelId, "credit_exhausted");
            }
            catch (ProviderException)
            {
                return Rejected(OutcomeProviderUnavailable, role.Role, role.ModelId, "provider_error");
            }
        }

        try
        {
            var completion = await _completionClient.CompleteAsync(
                new ModelCompletionRequest(
                    [
                        new ModelMessage(ModelMessageRole.System, "Return strict JSON for the provider-local live model role runner. Do not include raw provider payloads."),
                        new ModelMessage(ModelMessageRole.User, packet.Prompt)
                    ],
                    role.ModelId,
                    "live_model_role_output",
                    LiveModelRoleJsonSchema.Schema,
                    runnerOptions.Temperature,
                    runnerOptions.MaxTokens),
                cancellationToken).ConfigureAwait(false);

            return ParseCompletion(packet, role, completion);
        }
        catch (ProviderCreditExhaustedException)
        {
            return Rejected(OutcomeCreditExhausted, role.Role, role.ModelId, "credit_exhausted");
        }
        catch (ProviderEmptyResponseException)
        {
            return Rejected(OutcomeEmptyContent, role.Role, role.ModelId, "empty_content");
        }
        catch (ProviderMalformedResponseException)
        {
            return Rejected(OutcomeMalformedSchema, role.Role, role.ModelId, "malformed_schema");
        }
        catch (ProviderUnavailableException ex) when (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(OutcomeTimeout, role.Role, role.ModelId, "timeout");
        }
        catch (TimeoutException)
        {
            return Rejected(OutcomeTimeout, role.Role, role.ModelId, "timeout");
        }
        catch (ProviderException)
        {
            return Rejected(OutcomeProviderUnavailable, role.Role, role.ModelId, "provider_error");
        }
    }

    private static LiveModelRunResult? ValidatePacket(LiveModelRunPacket? packet)
    {
        if (packet is null ||
            string.IsNullOrWhiteSpace(packet.PacketId) ||
            !Enum.IsDefined(packet.Role) ||
            packet.AllowedOutputKinds is null ||
            packet.AllowedOutputKinds.Count == 0 ||
            packet.AllowedOutputKinds.Any(string.IsNullOrWhiteSpace))
        {
            return Rejected(OutcomeMalformedSchema);
        }

        return null;
    }

    private static LiveModelRunResult ParseCompletion(
        LiveModelRunPacket packet,
        LiveModelRoleRegistryEntry role,
        ModelCompletionResponse completion)
    {
        if (string.IsNullOrWhiteSpace(completion.Content))
        {
            return Rejected(OutcomeEmptyContent, role.Role, role.ModelId, "empty_content");
        }

        LiveModelOutputEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LiveModelOutputEnvelope>(completion.Content, JsonOptions);
        }
        catch (JsonException)
        {
            return Rejected(OutcomeMalformedSchema, role.Role, role.ModelId, "malformed_schema");
        }

        if (parsed?.Arguments is null || string.IsNullOrWhiteSpace(parsed.OutputKind))
        {
            return Rejected(OutcomeMalformedSchema, role.Role, role.ModelId, "malformed_schema");
        }

        if (!string.Equals(packet.PacketId, parsed.PacketId, StringComparison.Ordinal))
        {
            return Rejected(OutcomePacketMismatch, role.Role, role.ModelId);
        }

        if (!packet.AllowedOutputKinds.Contains(parsed.OutputKind, StringComparer.Ordinal))
        {
            return Rejected(OutcomeUnsupportedOutput, role.Role, role.ModelId);
        }

        return new LiveModelRunResult(
            true,
            OutcomeAccepted,
            null,
            "live_model_run",
            packet.PacketId,
            role.Role,
            role.ModelId,
            parsed.OutputKind,
            ToMood(parsed.UiMood),
            parsed.Arguments,
            completion.Content.Length,
            completion.Provider,
            completion.Model,
            completion.RequestId);
    }

    private static LiveModelRunResult Rejected(
        string outcomeCode,
        LiveModelRole? role = null,
        string? modelId = null,
        string? errorCode = null) =>
        new(
            false,
            outcomeCode,
            errorCode,
            RejectedRowName,
            RejectedRowPacketId,
            role,
            modelId,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static LiveModelUiMood ToMood(string? uiMood) =>
        uiMood switch
        {
            "calm_morning" => LiveModelUiMood.CalmMorning,
            "lively_food" => LiveModelUiMood.LivelyFood,
            "reflective_culture" => LiveModelUiMood.ReflectiveCulture,
            "soft_nature" => LiveModelUiMood.SoftNature,
            "restorative_downtime" => LiveModelUiMood.RestorativeDowntime,
            "logistics" => LiveModelUiMood.Logistics,
            _ => LiveModelUiMood.Unspecified
        };

    private sealed record LiveModelOutputEnvelope(
        string? PacketId,
        string? OutputKind,
        JsonElement? Arguments,
        string? UiMood,
        string? Summary);
}

public sealed class LiveModelRunEvaluator
{
    private readonly LiveModelRoleRunner _runner;

    public LiveModelRunEvaluator(LiveModelRoleRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedLiveModelRunEvalRow>> EvaluateAsync(
        IReadOnlyList<LiveModelRunEvalCase> cases,
        LiveModelRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedLiveModelRunEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            var result = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
            rows.Add(new SanitizedLiveModelRunEvalRow(
                result.IsAccepted ? evalCase.Name : result.Name,
                result.IsAccepted ? evalCase.Packet.PacketId : result.PacketId,
                result.IsAccepted,
                result.OutcomeCode,
                result.ErrorCode,
                result.Role,
                result.ModelId,
                result.OutputKind,
                result.UiMood,
                result.ResponseContentLength,
                result.Provider,
                result.Model,
                result.RequestId));
        }

        return rows;
    }
}

internal static class LiveModelRoleJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["packetId", "outputKind", "arguments", "summary"],
          "properties": {
            "packetId": { "type": "string" },
            "outputKind": { "type": "string" },
            "arguments": { "type": "object" },
            "uiMood": {
              "type": "string",
              "enum": ["unspecified", "calm_morning", "lively_food", "reflective_culture", "soft_nature", "restorative_downtime", "logistics"]
            },
            "summary": { "type": "string" }
          }
        }
        """;
}
