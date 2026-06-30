using System.Text.RegularExpressions;
using Pch.Core;

namespace Pch.Harness;

public sealed class CardMediaProvenanceBoundary
{
    public const string AcceptedCode = "card_media_accepted";
    public const string InvalidReferenceCode = "card_media_invalid_reference";
    public const string UnsupportedSourceClassCode = "card_media_unsupported_source_class";
    public const string UnsupportedLicenseClassCode = "card_media_unsupported_license_class";
    public const string InvalidDimensionsCode = "card_media_invalid_dimensions";
    public const string InvalidDominantColorCode = "card_media_invalid_color_token";

    private const int MaxMediaItems = 12;
    private const int MaxTextLength = 120;
    private const int MaxUriLength = 256;
    private const int MaxEvidenceIds = 8;
    private const int MinImagePixels = 16;
    private const int MaxImagePixels = 4096;
    private const string RedactedMediaUri = "local://card-media/redacted";

    private static readonly Regex ColorTokenPattern = new("^[a-z][a-z0-9-]{1,31}$", RegexOptions.Compiled);

    private static readonly string[] UnsafeFragments =
    [
        "RAW_",
        "PROVIDER_PAYLOAD",
        "RAW_PROMPT",
        "APPROVAL_TOKEN",
        "HOLD_REFERENCE",
        "PAYMENT",
        "BOOKING_REF",
        "CANDIDATE_DISPLAY",
        "SECRET",
        "CREDENTIAL",
        "PASSWORD",
        "API_KEY"
    ];

    public CardMediaValidationResult Validate(CardMediaReference? media)
    {
        if (media is null
            || string.IsNullOrWhiteSpace(media.MediaId)
            || string.IsNullOrWhiteSpace(media.Uri)
            || media.Attribution is null
            || media.EvidenceIds is null)
        {
            return Reject(InvalidReferenceCode, "Card media reference failed validation.");
        }

        if (!Enum.IsDefined(media.SourceClass))
        {
            return Reject(UnsupportedSourceClassCode, "Card media source class is not supported.");
        }

        if (!Enum.IsDefined(media.LicenseClass))
        {
            return Reject(UnsupportedLicenseClassCode, "Card media license class is not supported.");
        }

        if (media.Width is < MinImagePixels or > MaxImagePixels || media.Height is < MinImagePixels or > MaxImagePixels)
        {
            return Reject(InvalidDimensionsCode, "Card media dimensions failed validation.");
        }

        var color = SafeText(media.DominantColorToken, MaxTextLength);
        if (!ColorTokenPattern.IsMatch(color))
        {
            return Reject(InvalidDominantColorCode, "Card media color token failed validation.");
        }

        var sanitized = media with
        {
            MediaId = SafeId(media.MediaId),
            Uri = SafeUri(media.Uri),
            AltText = SafeText(media.AltText, MaxTextLength),
            DominantColorToken = color,
            Attribution = new CardMediaAttribution(
                SafeText(media.Attribution.SourceName, MaxTextLength),
                SafeOptionalText(media.Attribution.AuthorName, MaxTextLength),
                SafeOptionalUri(media.Attribution.SourceUri),
                SafeOptionalText(media.Attribution.LicenseName, MaxTextLength),
                SafeOptionalUri(media.Attribution.LicenseUri)),
            EvidenceIds = CleanIds(media.EvidenceIds)
        };

        return new(true, AcceptedCode, "Card media reference accepted.", sanitized);
    }

    public CardMediaManifestValidationResult ValidateManifest(CardMediaManifest? manifest)
    {
        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.ManifestId)
            || string.IsNullOrWhiteSpace(manifest.Locale)
            || manifest.Items is null
            || manifest.Items.Count == 0
            || manifest.Items.Count > MaxMediaItems)
        {
            return new(false, InvalidReferenceCode, "Card media manifest failed validation.", null);
        }

        var items = new List<CardMediaManifestItem>();
        foreach (var item in manifest.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Mood) || string.IsNullOrWhiteSpace(item.CandidateKind))
            {
                return new(false, InvalidReferenceCode, "Card media manifest failed validation.", null);
            }

            var media = Validate(item.Media);
            if (!media.IsAccepted || media.Media is null)
            {
                return new(false, media.Code, media.Summary, null);
            }

            items.Add(new CardMediaManifestItem(
                SafeToken(item.Mood),
                SafeText(item.CandidateKind, MaxTextLength),
                media.Media));
        }

        return new(
            true,
            AcceptedCode,
            "Card media manifest accepted.",
            new CardMediaManifest(
                SafeId(manifest.ManifestId),
                SafeText(manifest.Locale, MaxTextLength),
                items.OrderBy(item => item.Mood, StringComparer.Ordinal).ToArray()));
    }

    public CardMediaManifest BuildJapanMoodMediaManifest()
    {
        return new CardMediaManifest(
            "japan-mood-media-v1",
            "en-US",
            [
                ManifestItem("cultural_immersive", "Activity", "cherry-indigo", "Generated cherry and indigo cultural backdrop."),
                ManifestItem("scenic_relaxed", "Activity", "sea-glass", "Generated sea glass scenic Japan backdrop."),
                ManifestItem("lively_food", "Restaurant", "vermilion-amber", "Generated warm market food backdrop."),
                ManifestItem("calm_morning", "ScheduleBlock", "pale-sun", "Generated calm morning rice paper backdrop."),
                ManifestItem("reflective_culture", "Activity", "paper-lantern", "Generated reflective culture lantern backdrop."),
                ManifestItem("soft_nature", "Activity", "moss-mist", "Generated soft nature moss and mist backdrop."),
                ManifestItem("restorative_downtime", "Activity", "lavender-wood", "Generated restorative downtime bathhouse backdrop."),
                ManifestItem("logistics_transit", "Transit", "signal-blue", "Generated clean transit logistics backdrop.")
            ]);
    }

    public CardMediaReference? MediaForCandidate(
        CardMediaManifest? manifest,
        CandidateKind candidateKind,
        IReadOnlyList<string> evidenceIds)
    {
        var validation = ValidateManifest(manifest);
        if (!validation.IsAccepted || validation.Manifest is null)
        {
            return null;
        }

        var mood = MoodFor(candidateKind, evidenceIds);
        var item = validation.Manifest.Items.FirstOrDefault(item => string.Equals(item.Mood, mood, StringComparison.Ordinal))
            ?? validation.Manifest.Items.FirstOrDefault(item => string.Equals(item.CandidateKind, candidateKind.ToString(), StringComparison.Ordinal))
            ?? validation.Manifest.Items.FirstOrDefault();
        return item?.Media;
    }

    private static CardMediaManifestItem ManifestItem(string mood, string candidateKind, string color, string altText)
    {
        var mediaId = $"japan-{mood}";
        return new CardMediaManifestItem(
            mood,
            candidateKind,
            new CardMediaReference(
                mediaId,
                CardMediaSourceClass.Generated,
                CardMediaLicenseClass.Generated,
                $"local://card-media/japan/{mood}",
                altText,
                color,
                1200,
                800,
                new CardMediaAttribution(
                    "Progressive Commitment Harness",
                    "deterministic harness fixture",
                    null,
                    "Generated internal fixture",
                    null),
                [$"evidence-media-{mood}"]));
    }

    private static CardMediaValidationResult Reject(string code, string summary)
    {
        return new(false, code, summary, null);
    }

    private static string MoodFor(CandidateKind candidateKind, IReadOnlyList<string> evidenceIds)
    {
        if (evidenceIds.Any(id => id.Contains("family", StringComparison.OrdinalIgnoreCase)))
        {
            return "calm_morning";
        }

        return candidateKind switch
        {
            CandidateKind.Restaurant => "lively_food",
            CandidateKind.Transit or CandidateKind.Flight => "logistics_transit",
            CandidateKind.Hotel => "calm_morning",
            CandidateKind.ScheduleBlock => "calm_morning",
            CandidateKind.Activity => evidenceIds.Any(id => id.Contains("downtime", StringComparison.OrdinalIgnoreCase))
                ? "restorative_downtime"
                : "scenic_relaxed",
            _ => "scenic_relaxed"
        };
    }

    private static IReadOnlyList<string> CleanIds(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceIds)
            .ToArray()
            ?? [];
    }

    private static string SafeToken(string value)
    {
        var text = SafeText(value, MaxTextLength).ToLowerInvariant().Replace('_', '-');
        return ColorTokenPattern.IsMatch(text) ? text.Replace('-', '_') : "redacted";
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value, MaxTextLength);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' || character is '/' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string SafeUri(string value)
    {
        var text = SafeText(value, MaxUriLength);
        if (text == "redacted" || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return RedactedMediaUri;
        }

        return uri.Scheme is "https" or "local" ? text : RedactedMediaUri;
    }

    private static string? SafeOptionalUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = SafeText(value, MaxUriLength);
        if (text == "redacted" || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme == "https" ? text : null;
    }

    private static string? SafeOptionalText(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : SafeText(value, maxLength);
    }

    private static string SafeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        if (UnsafeFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public sealed record CardMediaValidationResult(
    bool IsAccepted,
    string Code,
    string Summary,
    CardMediaReference? Media);

public sealed record CardMediaManifestValidationResult(
    bool IsAccepted,
    string Code,
    string Summary,
    CardMediaManifest? Manifest);
