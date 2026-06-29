using System.Text.Json;
using Xunit;

namespace Pch.Providers.Tests;

internal static class SanitizedEvalArtifactAssert
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(object artifact) =>
        JsonSerializer.Serialize(artifact, JsonOptions);

    public static void DoesNotContainSensitiveValues(object artifact, params string[] sensitiveValues)
    {
        var serialized = Serialize(artifact);
        foreach (var sensitiveValue in sensitiveValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal))
        {
            Assert.DoesNotContain(sensitiveValue, serialized);
        }
    }
}
