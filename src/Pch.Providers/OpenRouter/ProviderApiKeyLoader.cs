namespace Pch.Providers.OpenRouter;

public static class ProviderApiKeyLoader
{
    public static string LoadRequiredApiKey(OpenRouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fromEnvironment = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKeyFilePath) && File.Exists(options.ApiKeyFilePath))
        {
            var fromFile = File.ReadAllText(options.ApiKeyFilePath).Trim();
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return fromFile;
            }
        }

        throw new InvalidOperationException(
            $"No API key was found in environment variable {options.ApiKeyEnvironmentVariable} or the configured key file.");
    }
}
