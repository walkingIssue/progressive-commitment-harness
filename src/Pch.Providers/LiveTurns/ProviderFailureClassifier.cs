using System.Net.Http;
using Pch.Providers.Errors;

namespace Pch.Providers.LiveTurns;

public static class ProviderFailureClassifier
{
    public static ProviderFailureClass Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            ProviderCreditExhaustedException => ProviderFailureClass.ProviderCreditExhausted,
            ProviderEmptyResponseException => ProviderFailureClass.ProviderEmptyContent,
            ProviderMalformedResponseException ex when ex.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) => ProviderFailureClass.ProviderSchemaInvalid,
            ProviderMalformedResponseException => ProviderFailureClass.ProviderMalformedJson,
            ProviderUnavailableException ex when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase) => ProviderFailureClass.ProviderTimeout,
            ProviderUnavailableException ex when ex.StatusCode == 429 => ProviderFailureClass.ProviderRateLimited,
            ProviderUnavailableException ex when ex.StatusCode is >= 400 and < 500 => ProviderFailureClass.ProviderHttp4xx,
            ProviderUnavailableException ex when ex.StatusCode is >= 500 and < 600 && IsUpstreamModelUnavailable(ex) => ProviderFailureClass.ProviderUpstreamModelUnavailable,
            ProviderUnavailableException ex when ex.StatusCode is >= 500 and < 600 => ProviderFailureClass.ProviderHttp5xx,
            ProviderUnavailableException ex when ex.InnerException is HttpRequestException => ProviderFailureClass.ProviderNetworkError,
            HttpRequestException => ProviderFailureClass.ProviderNetworkError,
            TimeoutException => ProviderFailureClass.ProviderTimeout,
            _ => ProviderFailureClass.ProviderUnknownError
        };
    }

    public static string OutcomeFor(ProviderFailureClass failureClass) =>
        failureClass switch
        {
            ProviderFailureClass.ProviderHttp4xx => LiveTurnRunner.OutcomeProviderHttp4xx,
            ProviderFailureClass.ProviderHttp5xx => LiveTurnRunner.OutcomeProviderHttp5xx,
            ProviderFailureClass.ProviderRateLimited => LiveTurnRunner.OutcomeProviderRateLimited,
            ProviderFailureClass.ProviderTimeout => LiveTurnRunner.OutcomeProviderTimeout,
            ProviderFailureClass.ProviderEmptyContent => LiveTurnRunner.OutcomeProviderEmptyContent,
            ProviderFailureClass.ProviderMalformedJson => LiveTurnRunner.OutcomeProviderMalformedJson,
            ProviderFailureClass.ProviderSchemaInvalid => LiveTurnRunner.OutcomeProviderSchemaInvalid,
            ProviderFailureClass.ProviderUpstreamModelUnavailable => LiveTurnRunner.OutcomeProviderUpstreamModelUnavailable,
            ProviderFailureClass.ProviderNetworkError => LiveTurnRunner.OutcomeProviderNetworkError,
            ProviderFailureClass.ProviderCreditExhausted => LiveTurnRunner.OutcomeCreditExhausted,
            ProviderFailureClass.ProviderDisabled => LiveTurnRunner.OutcomeDisabled,
            ProviderFailureClass.ProviderKeyMissing => LiveTurnRunner.OutcomeKeyMissing,
            ProviderFailureClass.ProviderFallbackDisabled => LiveTurnRunner.OutcomeFallbackDisabled,
            _ => LiveTurnRunner.OutcomeProviderUnknownError
        };

    public static string CodeFor(ProviderFailureClass failureClass) =>
        failureClass switch
        {
            ProviderFailureClass.ProviderHttp4xx => "provider_http_4xx",
            ProviderFailureClass.ProviderHttp5xx => "provider_http_5xx",
            ProviderFailureClass.ProviderRateLimited => "provider_rate_limited",
            ProviderFailureClass.ProviderTimeout => "provider_timeout",
            ProviderFailureClass.ProviderEmptyContent => "provider_empty_content",
            ProviderFailureClass.ProviderMalformedJson => "provider_malformed_json",
            ProviderFailureClass.ProviderSchemaInvalid => "provider_schema_invalid",
            ProviderFailureClass.ProviderUpstreamModelUnavailable => "provider_upstream_model_unavailable",
            ProviderFailureClass.ProviderNetworkError => "provider_network_error",
            ProviderFailureClass.ProviderCreditExhausted => "provider_credit_exhausted",
            ProviderFailureClass.ProviderDisabled => "provider_disabled",
            ProviderFailureClass.ProviderKeyMissing => "provider_key_missing",
            ProviderFailureClass.ProviderFallbackDisabled => "provider_fallback_disabled",
            _ => "provider_unknown_error"
        };

    private static bool IsUpstreamModelUnavailable(ProviderUnavailableException exception) =>
        exception.Message.Contains("model", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("upstream", StringComparison.OrdinalIgnoreCase);
}
