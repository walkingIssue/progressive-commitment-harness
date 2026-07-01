using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.LiveTurns;
using Pch.Providers.ModelCompletion;
using Pch.Providers.OpenAi;
using Xunit;

namespace Pch.Providers.Tests;

public sealed class OpenAiClientTests
{
    private const string ApiKey = "sk-api-key-should-not-persist";

    [Fact]
    public async Task CompleteUsesDefaultModelStructuredResponseFormatAndAuthorization()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"id":"abc","model":"gpt-4.1-mini-2025-04-14","choices":[{"message":{"content":"{\"ok\":true}"}}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}"""));
        var client = CreateClient(handler);

        var response = await client.CompleteAsync(new ModelCompletionRequest(
            [new ModelMessage(ModelMessageRole.User, "return json")],
            JsonSchemaName: "test_schema",
            JsonSchema: """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}"""));

        Assert.Equal("gpt-4.1-mini-2025-04-14", response.Model);
        Assert.Equal("""{"ok":true}""", response.Content);
        Assert.Equal(OpenAiModelCompletionClient.ProviderName, response.Provider);
        Assert.Equal(1, response.Usage?.PromptTokens);
        Assert.Equal(2, response.Usage?.CompletionTokens);
        Assert.Equal(3, response.Usage?.TotalTokens);
        Assert.Equal("abc", response.RequestId);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/v1/chat/completions", handler.Requests[0].RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.False(string.IsNullOrWhiteSpace(handler.Requests[0].Headers.Authorization?.Parameter));

        var payload = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal(OpenAiOptions.DefaultModel, payload.GetProperty("model").GetString());
        Assert.Equal("json_schema", payload.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("test_schema", payload.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString());

        var serializedResponse = JsonSerializer.Serialize(response);
        Assert.DoesNotContain(ApiKey, serializedResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCreditStatusReturnsUnknownButNotExhausted()
    {
        var client = CreateClient(new QueueHandler());

        var credits = await client.GetCreditStatusAsync();

        Assert.Null(credits.TotalCredits);
        Assert.Null(credits.TotalUsage);
        Assert.Null(credits.RemainingCredits);
        Assert.False(credits.IsExhausted);
    }

    [Fact]
    public async Task CompleteUsesConfiguredModelWhenRequestDoesNotOverrideIt()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}"""));
        var options = new OpenAiOptions
        {
            BaseUri = new Uri("https://openai.test"),
            Model = "custom-openai-model",
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new OpenAiModelCompletionClient(new HttpClient(handler), options, () => ApiKey);

        await client.CompleteAsync(new ModelCompletionRequest(
            [new ModelMessage(ModelMessageRole.User, "hello")]));

        var payload = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("custom-openai-model", payload.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteThrowsEmptyResponseWhenMessageContentIsMissing()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"choices":[{"message":{"content":""}}]}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ProviderEmptyResponseException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));
    }

    [Fact]
    public async Task CompleteThrowsMalformedResponseForInvalidJson()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{not-json"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ProviderMalformedResponseException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureClass.ProviderRateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureClass.ProviderHttp4xx)]
    [InlineData(HttpStatusCode.BadGateway, ProviderFailureClass.ProviderHttp5xx)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderFailureClass.ProviderHttp5xx)]
    public async Task NonSuccessMapsToTypedProviderFailureClasses(
        HttpStatusCode statusCode,
        ProviderFailureClass expectedFailureClass)
    {
        var client = CreateClient(new QueueHandler(Text(statusCode, "provider failure body")));

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));

        Assert.Equal((int)statusCode, ex.StatusCode);
        Assert.Equal(expectedFailureClass, ProviderFailureClassifier.Classify(ex));
    }

    [Fact]
    public async Task TimeoutDuringCompletionBodyReadMapsToProviderUnavailable()
    {
        var options = new OpenAiOptions
        {
            BaseUri = new Uri("https://openai.test"),
            Timeout = TimeSpan.FromMilliseconds(1)
        };
        var client = new OpenAiModelCompletionClient(
            new HttpClient(new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new BlockingContent()
            })),
            options,
            () => ApiKey);

        var ex = await Assert.ThrowsAsync<ProviderUnavailableException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")])));
        Assert.Equal(ProviderFailureClass.ProviderTimeout, ProviderFailureClassifier.Classify(ex));
    }

    [Fact]
    public async Task CallerCancellationDuringCompletionIsNotMappedToProviderUnavailable()
    {
        var options = new OpenAiOptions
        {
            BaseUri = new Uri("https://openai.test"),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new OpenAiModelCompletionClient(
            new HttpClient(new CancellationObservingHandler()),
            options,
            () => ApiKey);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CompleteAsync(
            new ModelCompletionRequest([new ModelMessage(ModelMessageRole.User, "hello")]),
            cancellation.Token));
    }

    private static OpenAiModelCompletionClient CreateClient(QueueHandler handler)
    {
        var options = new OpenAiOptions
        {
            BaseUri = new Uri("https://openai.test"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        return new OpenAiModelCompletionClient(new HttpClient(handler), options, () => ApiKey);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Text(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return _responses.Dequeue();
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class CancellationObservingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
        }
    }

    private sealed class BlockingContent : HttpContent
    {
        public BlockingContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
