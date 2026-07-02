using System.Text.RegularExpressions;

namespace Pch.UI.Features.EndUserChat;

public static partial class PlanningPrimitiveValueParser
{
    public static string? FirstIsoDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IsoDateRegex().Match(value).Value is { Length: > 0 } match ? match : null;

    public static (string Start, string End) DateRange(string? value)
    {
        var matches = IsoDateRegex().Matches(value ?? string.Empty).Select(match => match.Value).ToArray();
        return matches.Length switch
        {
            >= 2 => (matches[0], matches[1]),
            1 => (matches[0], matches[0]),
            _ => (string.Empty, string.Empty)
        };
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateRegex();
}
