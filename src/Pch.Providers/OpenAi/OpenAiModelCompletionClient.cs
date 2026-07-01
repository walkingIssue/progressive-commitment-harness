using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.OpenAi;

public sealed class OpenAiModelCompletionClient : IModelCompletionClient, IProviderCreditClient
{
    public const string ProviderName = "openai";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly Func<string> _apiKeyFactory;

    public OpenAiModelCompletionClient(HttpClient httpClient, OpenAiOptions options)
        : this(httpClient, options, () => OpenAiApiKeyLoader.LoadRequiredApiKey(options))
    {
    }

    public OpenAiModelCompletionClient(HttpClient httpClient, OpenAiOptions options, Func<string> apiKeyFactory)
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

        using var timeout = CreateTimeout(cancellationToken);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
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
            throw new ProviderEmptyResponseException(ProviderName, "OpenAI returned an empty response body.");
        }

        OpenAiCompletionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OpenAiCompletionResponse>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(ProviderName, "OpenAI returned malformed JSON.", ex);
        }

        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ProviderEmptyResponseException(ProviderName, "OpenAI returned no message content.");
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

    public Task<ProviderCreditStatus> GetCreditStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderCreditStatus(null, null, null, IsExhausted: false));

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
            throw new ProviderUnavailableException(ProviderName, "OpenAI request timed out.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderUnavailableException(ProviderName, "OpenAI request failed before receiving a response.", null, ex);
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
            throw new ProviderUnavailableException(ProviderName, "OpenAI request timed out.", null, ex);
        }
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKeyFactory());
    }

    private void AddOptionalHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.Organization))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", _options.Organization);
        }

        if (!string.IsNullOrWhiteSpace(_options.Project))
        {
            request.Headers.TryAddWithoutValidation("OpenAI-Project", _options.Project);
        }
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode == 429)
        {
            throw new ProviderUnavailableException(
                ProviderName,
                $"OpenAI returned HTTP 429. Response body length: {body.Length}.",
                429);
        }

        if ((int)statusCode is < 200 or > 299)
        {
            throw new ProviderUnavailableException(
                ProviderName,
                $"OpenAI returned HTTP {(int)statusCode}. Response body length: {body.Length}.",
                (int)statusCode);
        }
    }

    private sealed record OpenAiCompletionResponse(
        string? Id,
        string? Model,
        IReadOnlyList<OpenAiChoice>? Choices,
        OpenAiUsage? Usage);

    private sealed record OpenAiChoice(OpenAiMessage? Message);

    private sealed record OpenAiMessage(string? Content);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
}
