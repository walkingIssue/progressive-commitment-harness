using System.Text.Json;

namespace Pch.Providers.ModelActions;

public static class GoldenPacketEvalLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<ModelActionEvalCase>> LoadCasesAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Golden packet path is required.", nameof(path));
        }

        await using var stream = File.OpenRead(path);
        return await LoadCasesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<ModelActionEvalCase>> LoadCasesAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        GoldenPacketEvalDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<GoldenPacketEvalDocument>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new ModelActionRunnerException("Golden packet eval JSON is malformed.", ex);
        }

        if (document?.Cases is null || document.Cases.Count == 0)
        {
            throw new ModelActionRunnerException("Golden packet eval document must contain at least one case.");
        }

        foreach (var evalCase in document.Cases)
        {
            ValidateCase(evalCase);
        }

        return document.Cases;
    }

    private static void ValidateCase(ModelActionEvalCase evalCase)
    {
        if (string.IsNullOrWhiteSpace(evalCase.Name))
        {
            throw new ModelActionRunnerException("Golden packet eval case is missing a name.");
        }

        if (string.IsNullOrWhiteSpace(evalCase.ExpectedActionName))
        {
            throw new ModelActionRunnerException("Golden packet eval case is missing an expected action name.");
        }

        if (string.IsNullOrWhiteSpace(evalCase.Packet.PacketId))
        {
            throw new ModelActionRunnerException("Golden packet eval case is missing a packet id.");
        }

        if (evalCase.Packet.AllowedActions.Count == 0)
        {
            throw new ModelActionRunnerException("Golden packet eval case must include at least one allowed action.");
        }

        if (!evalCase.Packet.AllowedActions.Any(action =>
            string.Equals(action.Name, evalCase.ExpectedActionName, StringComparison.Ordinal)))
        {
            throw new ModelActionRunnerException("Golden packet eval expected action is outside the allowed set.");
        }
    }
}
