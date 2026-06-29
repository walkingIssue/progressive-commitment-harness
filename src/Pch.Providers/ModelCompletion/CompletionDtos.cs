namespace Pch.Providers.ModelCompletion;

public sealed record ModelCompletionRequest(
    IReadOnlyList<ModelMessage> Messages,
    string? Model = null,
    string? JsonSchemaName = null,
    string? JsonSchema = null,
    double? Temperature = null,
    int? MaxTokens = null);

public sealed record ModelMessage(ModelMessageRole Role, string Content);

public enum ModelMessageRole
{
    System,
    User,
    Assistant
}

public sealed record ModelCompletionResponse(
    string Model,
    string Content,
    string Provider,
    ModelUsage? Usage = null,
    string? RequestId = null);

public sealed record ModelUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

public sealed record ProviderCreditStatus(
    decimal? TotalCredits,
    decimal? TotalUsage,
    decimal? RemainingCredits,
    bool IsExhausted);
