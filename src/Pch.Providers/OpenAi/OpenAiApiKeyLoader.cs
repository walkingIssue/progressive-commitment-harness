namespace Pch.Providers.OpenAi;

public static class OpenAiApiKeyLoader
{
    public static string LoadRequiredApiKey(OpenAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fromEnvironment = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var configuredFilePath = Environment.GetEnvironmentVariable(options.ApiKeyFileEnvironmentVariable);
        var filePath = string.IsNullOrWhiteSpace(configuredFilePath)
            ? options.ApiKeyFilePath
            : configuredFilePath;
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            var fromFile = File.ReadAllText(filePath).Trim();
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return fromFile;
            }
        }

        throw new InvalidOperationException(
            $"No API key was found in environment variable {options.ApiKeyEnvironmentVariable} or the configured key file.");
    }
}
