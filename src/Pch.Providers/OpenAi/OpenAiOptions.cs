namespace Pch.Providers.OpenAi;

public sealed class OpenAiOptions
{
    public const string DefaultModel = "gpt-4.1-mini";
    public const string DefaultApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string DefaultApiKeyFileEnvironmentVariable = "OPENAI_API_KEY_FILE";

    public Uri BaseUri { get; init; } = new("https://api.openai.com");
    public string Model { get; init; } = DefaultModel;
    public string ApiKeyEnvironmentVariable { get; init; } = DefaultApiKeyEnvironmentVariable;
    public string ApiKeyFileEnvironmentVariable { get; init; } = DefaultApiKeyFileEnvironmentVariable;
    public string? ApiKeyFilePath { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public string? Organization { get; init; }
    public string? Project { get; init; }
}
