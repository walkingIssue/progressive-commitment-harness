using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.ModelActions;

public sealed class ModelActionRunner : IModelActionRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IModelCompletionClient _completionClient;

    public ModelActionRunner(IModelCompletionClient completionClient)
    {
        _completionClient = completionClient ?? throw new ArgumentNullException(nameof(completionClient));
    }

    public async Task<ModelActionRunResult> RunAsync(
        ModelActionPacket packet,
        ModelActionRunnerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.AllowedActions.Count == 0)
        {
            throw new ArgumentException("At least one allowed action is required.", nameof(packet));
        }

        options ??= new ModelActionRunnerOptions();

        var response = await _completionClient.CompleteAsync(
            CreateCompletionRequest(packet, options),
            cancellationToken).ConfigureAwait(false);

        return ParseResponse(packet, response);
    }

    private static ModelCompletionRequest CreateCompletionRequest(
        ModelActionPacket packet,
        ModelActionRunnerOptions options)
    {
        var actionNames = packet.AllowedActions.Select(action => action.Name).ToArray();
        var envelope = new
        {
            packet.PacketId,
            packet.Stage,
            packet.Goal,
            packet.Instructions,
            packet.Inputs,
            allowedActions = packet.AllowedActions
        };

        var system = """
            You are a provider-local model action runner. Return one strict JSON object only.
            The JSON shape is {"action":"<allowed action name>","arguments":{},"summary":"optional short note"}.
            Choose exactly one allowed action and do not emit markdown.
            """;
        var user = JsonSerializer.Serialize(envelope, SerializerOptions);

        return new ModelCompletionRequest(
            [
                new ModelMessage(ModelMessageRole.System, system),
                new ModelMessage(ModelMessageRole.User, user)
            ],
            options.Model,
            "model_action",
            CreateResponseSchema(actionNames),
            options.Temperature,
            options.MaxTokens);
    }

    private static string CreateResponseSchema(IReadOnlyList<string> actionNames)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["action"] = new { type = "string", @enum = actionNames },
                ["arguments"] = new { type = "object" },
                ["summary"] = new { type = "string" }
            },
            required = new[] { "action", "arguments" },
            additionalProperties = false
        };

        return JsonSerializer.Serialize(schema, SerializerOptions);
    }

    private static ModelActionRunResult ParseResponse(
        ModelActionPacket packet,
        ModelCompletionResponse response)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.Content);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(
                response.Provider,
                "Model action runner received malformed JSON content.",
                ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("action", out var actionElement) ||
                actionElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(actionElement.GetString()))
            {
                throw new ModelActionRunnerException("Model action response is missing a string action.");
            }

            var actionName = actionElement.GetString()!;
            if (!packet.AllowedActions.Any(action => string.Equals(action.Name, actionName, StringComparison.Ordinal)))
            {
                throw new ModelActionRunnerException("Model action response selected an action outside the allowed set.");
            }

            if (!root.TryGetProperty("arguments", out var argumentsElement) ||
                argumentsElement.ValueKind != JsonValueKind.Object)
            {
                throw new ModelActionRunnerException("Model action response is missing object arguments.");
            }

            var summary = root.TryGetProperty("summary", out var summaryElement) &&
                summaryElement.ValueKind == JsonValueKind.String
                    ? summaryElement.GetString()
                    : null;

            return new ModelActionRunResult(
                packet.PacketId,
                actionName,
                argumentsElement.Clone(),
                summary,
                response.Content.Length,
                response.Provider,
                response.Model,
                response.RequestId);
        }
    }
}
