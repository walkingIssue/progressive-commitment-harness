namespace Pch.Providers.OpenRouter;

public sealed class OpenRouterOptions
{
    public const string DefaultModel = "qwen/qwen3-14b";
    public const string DefaultApiKeyEnvironmentVariable = "OPENROUTER_API_KEY";

    public Uri BaseUri { get; init; } = new("https://openrouter.ai");
    public string Model { get; init; } = DefaultModel;
    public string ApiKeyEnvironmentVariable { get; init; } = DefaultApiKeyEnvironmentVariable;
    public string? ApiKeyFilePath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool CheckCreditsBeforeCompletion { get; init; } = true;
    public decimal MinimumRemainingCredits { get; init; } = 0.01m;
    public string? Referer { get; init; }
    public string? Title { get; init; }
}
