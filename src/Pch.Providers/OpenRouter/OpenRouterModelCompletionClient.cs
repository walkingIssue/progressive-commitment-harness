using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.OpenRouter;

public sealed class OpenRouterModelCompletionClient : IModelCompletionClient, IProviderCreditClient
{
    public const string ProviderName = "openrouter";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;
    private readonly Func<string> _apiKeyFactory;

    public OpenRouterModelCompletionClient(HttpClient httpClient, OpenRouterOptions options)
        : this(httpClient, options, () => ProviderApiKeyLoader.LoadRequiredApiKey(options))
    {
    }

    public OpenRouterModelCompletionClient(HttpClient httpClient, OpenRouterOptions options, Func<string> apiKeyFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _apiKeyFactory = apiKeyFactory ?? throw new ArgumentNullException(nameof(apiKeyFactory));
        _httpClient.BaseAddress ??= options.BaseUri;
    }

    public async Task<ModelCompletionResponse> CompleteAsync(
        ModelCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(request));
        }

        if (_options.CheckCreditsBeforeCompletion)
        {
            var credits = await GetCreditStatusAsync(cancellationToken).ConfigureAwait(false);
            if (credits.IsExhausted || credits.RemainingCredits <= _options.MinimumRemainingCredits)
            {
                throw new ProviderCreditExhaustedException(
                    ProviderName,
                    "OpenRouter credits are exhausted or below the configured safety threshold.",
                    credits.RemainingCredits);
            }
        }

        using var timeout = CreateTimeout(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        AddAuthorization(httpRequest);
        AddOptionalHeaders(httpRequest);

        var payload = CreateCompletionPayload(request);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await SendAsync(httpRequest, timeout.Token, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, timeout.Token, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.StatusCode, body);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ProviderEmptyResponseException(ProviderName, "OpenRouter returned an empty response body.");
        }

        OpenRouterCompletionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OpenRouterCompletionResponse>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(ProviderName, "OpenRouter returned malformed JSON.", ex);
        }

        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ProviderEmptyResponseException(ProviderName, "OpenRouter returned no message content.");
        }

        return new ModelCompletionResponse(
            parsed?.Model ?? request.Model ?? _options.Model,
            content,
            ProviderName,
            parsed?.Usage is null
                ? null
                : new ModelUsage(parsed.Usage.PromptTokens, parsed.Usage.CompletionTokens, parsed.Usage.TotalTokens),
            parsed?.Id);
    }

    public async Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CreateTimeout(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/credits");
        AddAuthorization(httpRequest);
        AddOptionalHeaders(httpRequest);

        using var response = await SendAsync(httpRequest, timeout.Token, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, timeout.Token, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.StatusCode, body);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ProviderEmptyResponseException(ProviderName, "OpenRouter returned an empty credits response.");
        }

        OpenRouterCreditsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OpenRouterCreditsResponse>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(ProviderName, "OpenRouter returned malformed credits JSON.", ex);
        }

        var remaining = parsed?.Data?.TotalCredits is null || parsed.Data.TotalUsage is null
            ? null
            : parsed.Data.TotalCredits - parsed.Data.TotalUsage;

        return new ProviderCreditStatus(
            parsed?.Data?.TotalCredits,
            parsed?.Data?.TotalUsage,
            remaining,
            remaining <= _options.MinimumRemainingCredits);
    }

    private object CreateCompletionPayload(ModelCompletionRequest request)
    {
        var messages = request.Messages.Select(message => new
        {
            role = ToWireRole(message.Role),
            content = message.Content
        });

        object? responseFormat = null;
        if (!string.IsNullOrWhiteSpace(request.JsonSchemaName) && !string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            responseFormat = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = request.JsonSchemaName,
                    strict = true,
                    schema = JsonSerializer.Deserialize<JsonElement>(request.JsonSchema)
                }
            };
        }

        return new
        {
            model = request.Model ?? _options.Model,
            messages,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            response_format = responseFormat
        };
    }

    private static string ToWireRole(ModelMessageRole role) =>
        role switch
        {
            ModelMessageRole.System => "system",
            ModelMessageRole.User => "user",
            ModelMessageRole.Assistant => "assistant",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        return timeout;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken timeoutToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ProviderUnavailableException(ProviderName, "OpenRouter request timed out.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderUnavailableException(ProviderName, "OpenRouter request failed before receiving a response.", null, ex);
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken timeoutToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ProviderUnavailableException(ProviderName, "OpenRouter request timed out.", null, ex);
        }
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKeyFactory());
    }

    private void AddOptionalHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.Referer))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", _options.Referer);
        }

        if (!string.IsNullOrWhiteSpace(_options.Title))
        {
            request.Headers.TryAddWithoutValidation("X-Title", _options.Title);
        }
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is 402 or 429)
        {
            throw new ProviderCreditExhaustedException(ProviderName, "OpenRouter reported exhausted credits or rate limit.");
        }

        if ((int)statusCode is < 200 or > 299)
        {
            throw new ProviderUnavailableException(
                ProviderName,
                $"OpenRouter returned HTTP {(int)statusCode}. Response body length: {body.Length}.",
                (int)statusCode);
        }
    }

    private sealed record OpenRouterCompletionResponse(
        string? Id,
        string? Model,
        IReadOnlyList<OpenRouterChoice>? Choices,
        OpenRouterUsage? Usage);

    private sealed record OpenRouterChoice(OpenRouterMessage? Message);

    private sealed record OpenRouterMessage(string? Content);

    private sealed record OpenRouterUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    private sealed record OpenRouterCreditsResponse(OpenRouterCreditsData? Data);

    private sealed record OpenRouterCreditsData(
        [property: JsonPropertyName("total_credits")] decimal? TotalCredits,
        [property: JsonPropertyName("total_usage")] decimal? TotalUsage);
}
