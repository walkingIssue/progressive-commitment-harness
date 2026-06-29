using System.Text.Json;
using Pch.Providers.Errors;
using Pch.Providers.ModelActions;
using Pch.Providers.ModelCompletion;

namespace Pch.Providers.Mock;

public sealed class MockModelActionCompletionClient : IModelCompletionClient
{
    public const string ProviderName = "mock-action";

    public Task<ModelCompletionResponse> CompleteAsync(
        ModelCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var userContent = request.Messages.LastOrDefault(message => message.Role == ModelMessageRole.User)?.Content;
        if (string.IsNullOrWhiteSpace(userContent))
        {
            throw new ModelActionRunnerException("Mock action completion requires a packet-shaped user message.");
        }

        using var document = ParsePacket(userContent);
        var root = document.RootElement;
        var packetId = ReadRequiredString(root, "packetId");
        var allowedActions = root.GetProperty("allowedActions");
        if (allowedActions.ValueKind != JsonValueKind.Array || allowedActions.GetArrayLength() == 0)
        {
            throw new ModelActionRunnerException("Mock action completion requires at least one allowed action.");
        }

        var firstAction = ReadRequiredString(allowedActions[0], "name");

        var content = JsonSerializer.Serialize(new
        {
            action = firstAction,
            arguments = new
            {
                packetId,
                deterministic = true
            },
            summary = "deterministic mock action"
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return Task.FromResult(new ModelCompletionResponse(
            request.Model ?? "mock-action-deterministic",
            content,
            ProviderName,
            null,
            $"mock-action-{packetId}"));
    }

    private static JsonDocument ParsePacket(string userContent)
    {
        try
        {
            return JsonDocument.Parse(userContent);
        }
        catch (JsonException ex)
        {
            throw new ProviderMalformedResponseException(
                ProviderName,
                "Mock action completion received malformed packet JSON.",
                ex);
        }
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ModelActionRunnerException($"Mock action completion packet is missing string property '{propertyName}'.");
        }

        return property.GetString()!;
    }
}
