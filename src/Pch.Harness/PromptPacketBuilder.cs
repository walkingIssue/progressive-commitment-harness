using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Pch.Core;

namespace Pch.Harness;

public sealed class PromptPacketBuilder
{
    private const int MaxPromptLength = 4_000;
    private const int MaxInputFacts = 24;
    private const int MaxInputPendingConfirmations = 24;
    private const int MaxInputConstraints = 24;
    private const int MaxPacketFacts = 8;
    private const int MaxPacketPendingConfirmations = 6;
    private const int MaxPacketConstraints = 8;
    private const int MaxPacketScenarioHints = 6;
    private const int MaxPacketEvidenceReferences = 8;
    private const int MaxHintLength = 80;
    private const int MaxLocaleLength = 32;

    public PromptIntakeResult Build(TripSession session, PromptIntakeRequest request)
    {
        var validation = Validate(session, request);
        if (!validation.IsAccepted)
        {
            return PromptIntakeResult.Rejected(validation.Code, validation.Summary, PromptMetadata.Empty);
        }

        var metadata = PromptMetadata.From(request.RawPrompt);
        var memory = request.CurrentMemory ?? new MissionIntakeApplication().BuildDigest(session);
        var packet = new MissionPlannerPromptPacket(
            PacketId: $"prompt-packet-{session.SessionId}-{session.Stage}".ToLowerInvariant(),
            SessionId: session.SessionId,
            MissionId: session.Mission.MissionId,
            Stage: session.Stage.ToString(),
            Locale: request.Locale?.Trim(),
            ScenarioHints: CleanHints(request.ScenarioHints).Take(MaxPacketScenarioHints).ToArray(),
            Prompt: metadata,
            TransientRawPrompt: request.RawPrompt,
            CurrentMissionFacts: memory.LoadBearingFacts.Take(MaxPacketFacts).ToArray(),
            PendingConfirmations: memory.PendingConfirmations.Take(MaxPacketPendingConfirmations).Select(ToPromptPendingConfirmation).ToArray(),
            KnownConstraints: session.Mission.Constraints
                .OrderBy(constraint => constraint.ConstraintId, StringComparer.Ordinal)
                .Take(MaxPacketConstraints)
                .Select(ToPromptConstraint)
                .ToArray(),
            EvidenceReferences: memory.TraceReferences
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxPacketEvidenceReferences)
                .ToArray(),
            PlannerInstructions:
            [
                "Use the prompt only as transient input; do not persist raw prompt text.",
                "Treat pending confirmations as unverified until user confirmation.",
                "Do not invent prices, bookings, availability, or unsupported facts."
            ]);

        return PromptIntakeResult.Accepted(packet);
    }

    private static PromptPacketValidation Validate(TripSession session, PromptIntakeRequest request)
    {
        if (request is null)
        {
            return Reject("invalid_request", "Prompt intake request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Reject("invalid_session", "Prompt intake request failed validation.");
        }

        if (string.IsNullOrWhiteSpace(request.RawPrompt))
        {
            return Reject("invalid_prompt", "Prompt intake request failed validation.");
        }

        if (request.RawPrompt.Length > MaxPromptLength)
        {
            return Reject("prompt_too_long", "Prompt intake request exceeded length limits.");
        }

        if (request.CurrentMemory is not null && !ValidMemory(request.CurrentMemory))
        {
            return Reject("too_many_memory_items", "Prompt intake request exceeded memory limits.");
        }

        if (session.Mission.Constraints.Count > MaxInputConstraints)
        {
            return Reject("too_many_constraints", "Prompt intake request exceeded constraint limits.");
        }

        if (!ValidOptionalText(request.Locale, MaxLocaleLength) || !ValidHints(request.ScenarioHints))
        {
            return Reject("invalid_context_hint", "Prompt intake request failed validation.");
        }

        return new(true, "accepted", "Prompt intake request accepted.");
    }

    private static bool ValidMemory(StructuredMemoryDigest memory)
    {
        return memory.LoadBearingFacts is not null
            && memory.PendingConfirmations is not null
            && memory.TraceReferences is not null
            && memory.LoadBearingFacts.Count <= MaxInputFacts
            && memory.PendingConfirmations.Count <= MaxInputPendingConfirmations
            && memory.LoadBearingFacts.All(fact => !string.IsNullOrWhiteSpace(fact))
            && memory.PendingConfirmations.All(ValidPendingConfirmation)
            && memory.TraceReferences.All(reference => !string.IsNullOrWhiteSpace(reference));
    }

    private static bool ValidPendingConfirmation(MissionPendingConfirmation pending)
    {
        return pending is not null
            && !string.IsNullOrWhiteSpace(pending.FieldPath)
            && !string.IsNullOrWhiteSpace(pending.ReasonCode)
            && pending.EvidenceIds is not null
            && pending.EvidenceIds.All(id => !string.IsNullOrWhiteSpace(id));
    }

    private static bool ValidHints(IReadOnlyList<string>? hints)
    {
        return hints is null || hints.All(hint => !string.IsNullOrWhiteSpace(hint) && hint.Length <= MaxHintLength);
    }

    private static IEnumerable<string> CleanHints(IReadOnlyList<string>? hints)
    {
        return hints?
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Select(hint => hint.Trim())
            .Distinct(StringComparer.Ordinal)
            ?? [];
    }

    private static bool ValidOptionalText(string? value, int maxLength)
    {
        return value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= maxLength);
    }

    private static PromptPendingConfirmation ToPromptPendingConfirmation(MissionPendingConfirmation pending)
    {
        return new(
            pending.FieldPath,
            pending.Source.ToString(),
            pending.ReasonCode,
            pending.EvidenceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static PromptConstraint ToPromptConstraint(Constraint constraint)
    {
        return new(
            constraint.ConstraintId,
            constraint.Label,
            constraint.Source.ToString(),
            constraint.IsHard);
    }

    private static PromptPacketValidation Reject(string code, string summary)
    {
        return new(false, code, summary);
    }
}

public sealed record PromptIntakeRequest(
    string SessionId,
    string RawPrompt,
    StructuredMemoryDigest? CurrentMemory,
    string? Locale,
    IReadOnlyList<string> ScenarioHints);

public sealed record PromptIntakeResult(
    bool IsAccepted,
    string Code,
    string Summary,
    PromptMetadata Prompt,
    MissionPlannerPromptPacket? Packet)
{
    public static PromptIntakeResult Accepted(MissionPlannerPromptPacket packet)
    {
        return new(true, "prompt_packet_built", "Prompt packet built.", packet.Prompt, packet);
    }

    public static PromptIntakeResult Rejected(string code, string summary, PromptMetadata prompt)
    {
        return new(false, code, summary, prompt, null);
    }
}

public sealed record MissionPlannerPromptPacket(
    string PacketId,
    string SessionId,
    string MissionId,
    string Stage,
    string? Locale,
    IReadOnlyList<string> ScenarioHints,
    PromptMetadata Prompt,
    [property: JsonIgnore] string TransientRawPrompt,
    IReadOnlyList<string> CurrentMissionFacts,
    IReadOnlyList<PromptPendingConfirmation> PendingConfirmations,
    IReadOnlyList<PromptConstraint> KnownConstraints,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> PlannerInstructions);

public sealed record PromptMetadata(
    int Length,
    string Sha256,
    string Category)
{
    public static PromptMetadata Empty { get; } = new(0, string.Empty, "invalid");

    public static PromptMetadata From(string rawPrompt)
    {
        return new(rawPrompt.Length, Sha256Hex(rawPrompt), Categorize(rawPrompt));
    }

    private static string Categorize(string rawPrompt)
    {
        var text = rawPrompt.ToLowerInvariant();
        if (text.Contains("funeral", StringComparison.Ordinal) || text.Contains("quiet", StringComparison.Ordinal))
        {
            return "funeral_downtime";
        }

        if (text.Contains("business", StringComparison.Ordinal) || text.Contains("client", StringComparison.Ordinal)
            || text.Contains("workshop", StringComparison.Ordinal))
        {
            return "business";
        }

        if (text.Contains("family", StringComparison.Ordinal) || text.Contains("help", StringComparison.Ordinal)
            || text.Contains("move", StringComparison.Ordinal))
        {
            return "family_support";
        }

        if (text.Contains("vacation", StringComparison.Ordinal) || text.Contains("holiday", StringComparison.Ordinal))
        {
            return "vacation";
        }

        return "general";
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record PromptPendingConfirmation(
    string FieldPath,
    string Source,
    string ReasonCode,
    IReadOnlyList<string> EvidenceIds);

public sealed record PromptConstraint(
    string ConstraintId,
    string Label,
    string Source,
    bool IsHard);

public sealed record PromptPacketValidation(
    bool IsAccepted,
    string Code,
    string Summary);
