using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Core;

namespace Pch.Harness;

public sealed class GoldenTurnTraceRunner
{
    public const string TraceCompleteCode = "golden_trace_complete";
    public const string TraceBlockedCode = "golden_trace_blocked";
    public const string InvalidScriptCode = "golden_trace_invalid_script";

    public const string HappyScenario = "happy_trip_planning";
    public const string BlockedScenario = "blocked_safety";

    private static readonly DateTimeOffset FixedAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxTurns = 16;
    private const int MaxEvidenceReferences = 8;
    private const int MaxMarkers = 8;
    private const int MaxTextLength = 120;

    public static GoldenTurnTraceScript HappyPathScript { get; } = new(
        ScriptId: "golden-happy-trip-planning",
        Scenario: HappyScenario,
        PromptCategory: "vacation",
        TransientUserPrompt: "Plan a one day vacation in Tokyo with one clear activity.",
        Steps:
        [
            new("step-01", GoldenTurnKind.User, "prompt_intake"),
            new("step-02", GoldenTurnKind.Assistant, "prompt_packet_summary"),
            new("step-03", GoldenTurnKind.Harness, "mission_intake"),
            new("step-04", GoldenTurnKind.Harness, "itinerary_compile"),
            new("step-05", GoldenTurnKind.Decision, "candidate_select"),
            new("step-06", GoldenTurnKind.Harness, "availability_preview"),
            new("step-07", GoldenTurnKind.Evidence, "trip_run_snapshot")
        ]);

    public static GoldenTurnTraceScript BlockedSafetyScript { get; } = new(
        ScriptId: "golden-blocked-safety",
        Scenario: BlockedScenario,
        PromptCategory: "vacation",
        TransientUserPrompt: "RAW_PROMPT_SHOULD_NOT_LEAK plan with payment and hold pressure.",
        Steps:
        [
            new("step-01", GoldenTurnKind.User, "prompt_intake"),
            new("step-02", GoldenTurnKind.Assistant, "prompt_packet_summary"),
            new("step-03", GoldenTurnKind.Harness, "mission_intake"),
            new("step-04", GoldenTurnKind.Harness, "itinerary_compile"),
            new("step-05", GoldenTurnKind.Decision, "candidate_select"),
            new("step-06", GoldenTurnKind.Blocked, "approval_required_preview"),
            new("step-07", GoldenTurnKind.Evidence, "blocked_snapshot")
        ]);

    public IReadOnlyList<GoldenTurnTraceScript> DefaultScripts()
    {
        return [HappyPathScript, BlockedSafetyScript];
    }

    public IReadOnlyList<GoldenTurnTraceResult> RunDefaultScripts()
    {
        return DefaultScripts().Select(Run).ToArray();
    }

    public GoldenTurnTraceResult Run(GoldenTurnTraceScript script)
    {
        if (script is null
            || string.IsNullOrWhiteSpace(script.ScriptId)
            || string.IsNullOrWhiteSpace(script.Scenario)
            || string.IsNullOrWhiteSpace(script.TransientUserPrompt)
            || script.Steps is null
            || script.Steps.Count == 0)
        {
            return InvalidTrace();
        }

        return script.Scenario switch
        {
            HappyScenario => RunHappy(script),
            BlockedScenario => RunBlocked(script),
            _ => InvalidTrace()
        };
    }

    private static GoldenTurnTraceResult RunHappy(GoldenTurnTraceScript script)
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var turns = new List<GoldenTurnTraceTurn>();
        var prompt = new PromptPacketBuilder().Build(session, PromptRequest(session, script));
        turns.Add(UserTurn(1, prompt));
        turns.Add(AssistantTurn(2, prompt));

        var mission = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-golden-happy",
            [new("/mission/purpose", "Golden trace vacation", AuthoritySource.User, ["evidence-golden-user"])],
            [],
            []));
        turns.Add(HarnessTurn(3, "mission_intake", mission.Code, mission.Summary, mission.Digest.TraceReferences));

        var compilation = Compile(session);
        turns.Add(HarnessTurn(4, "itinerary_compile", compilation.Code, compilation.Summary, []));

        var slot = Slot(session, ItinerarySlotKind.Activity);
        AddCandidate(session, slot, new Candidate(
            "candidate-golden-activity",
            CandidateKind.Activity,
            "Golden activity",
            "Trusted deterministic activity.",
            null,
            null,
            ["evidence-golden-candidate"],
            120));
        var selection = Select(session, slot, "candidate-golden-activity", CandidateKind.Activity);
        turns.Add(DecisionTurn(5, selection.Code, selection.Summary, selection.EvidenceIds));

        var availability = new AvailabilityQuotePreviewApplication();
        var preview = availability.Preview(session, PreviewRequest(availability, session, slot, "candidate-golden-activity", CandidateKind.Activity));
        turns.Add(HarnessTurn(6, "availability_preview", preview.Code, preview.Summary, preview.EvidenceReferences));

        session.RecordApproval(new ApprovalToken("approval-golden", "approved-golden-token", FixedAt));
        var snapshot = new TripRunSnapshotBuilder().Build(session);
        turns.Add(EvidenceTurn(7, snapshot.Code, snapshot.Summary, snapshot.Snapshot.EvidenceReferences));

        return BuildResult(script, TraceCompleteCode, "Golden turn trace completed.", turns);
    }

    private static GoldenTurnTraceResult RunBlocked(GoldenTurnTraceScript script)
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var turns = new List<GoldenTurnTraceTurn>();
        var prompt = new PromptPacketBuilder().Build(session, PromptRequest(session, script));
        turns.Add(UserTurn(1, prompt));
        turns.Add(AssistantTurn(2, prompt));

        var mission = new MissionIntakeApplication().Apply(session, new MissionIntakeProposal(
            "proposal-golden-blocked",
            [new("/mission/purpose", "Golden blocked safety check", AuthoritySource.User, ["evidence-golden-user"])],
            [],
            []));
        turns.Add(HarnessTurn(3, "mission_intake", mission.Code, mission.Summary, mission.Digest.TraceReferences));

        var compilation = Compile(session);
        turns.Add(HarnessTurn(4, "itinerary_compile", compilation.Code, compilation.Summary, []));

        var slot = Slot(session, ItinerarySlotKind.Activity);
        AddCandidate(session, slot, new Candidate(
            "candidate-golden-priced",
            CandidateKind.Activity,
            "RAW_CANDIDATE_DISPLAY_SHOULD_NOT_LEAK",
            "RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK",
            250,
            "USD",
            ["evidence-golden-candidate", "RAW_HOLD_REFERENCE_SHOULD_NOT_LEAK"],
            120));
        var selection = Select(session, slot, "candidate-golden-priced", CandidateKind.Activity);
        turns.Add(DecisionTurn(5, selection.Code, selection.Summary, selection.EvidenceIds));

        var availability = new AvailabilityQuotePreviewApplication();
        var preview = availability.Preview(session, PreviewRequest(
            availability,
            session,
            slot,
            "candidate-golden-priced",
            CandidateKind.Activity,
            AvailabilityQuoteKind.Quote));
        turns.Add(BlockedTurn(6, preview.Code, preview.Summary, preview.EvidenceReferences));

        var snapshot = new TripRunSnapshotBuilder().Build(session);
        turns.Add(EvidenceTurn(7, snapshot.Code, snapshot.Summary, snapshot.Snapshot.EvidenceReferences));

        return BuildResult(script, TraceBlockedCode, "Golden turn trace blocked by safety gate.", turns);
    }

    private static PromptIntakeRequest PromptRequest(TripSession session, GoldenTurnTraceScript script)
    {
        return new(
            session.SessionId,
            script.TransientUserPrompt,
            null,
            "en-US",
            [script.PromptCategory]);
    }

    private static ItineraryCompilationResult Compile(TripSession session)
    {
        return new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            session.MemoryDigest,
            []));
    }

    private static void AddCandidate(TripSession session, ItinerarySlot slot, Candidate candidate)
    {
        session.AddItineraryCandidatePool(slot.SlotId, new CandidatePool(
            $"pool-{SafeId(slot.SlotId)}",
            "all",
            [candidate],
            [],
            ObservedAt));
    }

    private static ItinerarySlotApplicationResult Select(
        TripSession session,
        ItinerarySlot slot,
        string candidateId,
        CandidateKind candidateKind)
    {
        return new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            candidateId,
            candidateKind,
            FixedAt));
    }

    private static AvailabilityQuotePreviewRequest PreviewRequest(
        AvailabilityQuotePreviewApplication application,
        TripSession session,
        ItinerarySlot slot,
        string candidateId,
        CandidateKind candidateKind,
        AvailabilityQuoteKind quoteKind = AvailabilityQuoteKind.Availability)
    {
        var context = application.CurrentContext(session);
        return new(
            session.SessionId,
            slot.SlotId,
            candidateId,
            slot.Kind,
            candidateKind,
            quoteKind,
            context.CompilationFingerprint,
            context.SnapshotId,
            FixedAt);
    }

    private static ItinerarySlot Slot(TripSession session, ItinerarySlotKind kind)
    {
        return session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == kind);
    }

    private static GoldenTurnTraceResult BuildResult(
        GoldenTurnTraceScript script,
        string code,
        string summary,
        IReadOnlyList<GoldenTurnTraceTurn> turns)
    {
        var boundedTurns = turns.Take(MaxTurns).ToArray();
        var evidence = SafeReferences(boundedTurns.SelectMany(turn => turn.EvidenceReferences))
            .Take(MaxEvidenceReferences)
            .ToArray();
        var withoutHash = new GoldenTurnTraceResult(
            IsAccepted: code == TraceCompleteCode,
            IsBlocked: code == TraceBlockedCode,
            Code: code,
            Summary: summary,
            ScriptId: SafeText(script.ScriptId),
            Scenario: SafeText(script.Scenario),
            Prompt: PromptMetadata.From(script.TransientUserPrompt),
            Turns: boundedTurns,
            EvidenceReferences: evidence,
            TranscriptHash: string.Empty);
        var serialized = JsonSerializer.Serialize(withoutHash, JsonOptions);
        return withoutHash with { TranscriptHash = Hash(serialized) };
    }

    private static GoldenTurnTraceResult InvalidTrace()
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            Code: InvalidScriptCode,
            Summary: "Golden turn trace script failed validation.",
            ScriptId: "invalid",
            Scenario: "invalid",
            Prompt: PromptMetadata.Empty,
            Turns: [],
            EvidenceReferences: [],
            TranscriptHash: Hash(InvalidScriptCode));
    }

    private static GoldenTurnTraceTurn UserTurn(int ordinal, PromptIntakeResult prompt)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.User,
            "user",
            "Intake",
            prompt.Code,
            $"User prompt metadata accepted: {prompt.Prompt.Category}.",
            [$"prompt_length:{prompt.Prompt.Length}", $"prompt_category:{prompt.Prompt.Category}"],
            prompt.Packet?.EvidenceReferences ?? []);
    }

    private static GoldenTurnTraceTurn AssistantTurn(int ordinal, PromptIntakeResult prompt)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.Assistant,
            "assistant",
            "Intake",
            prompt.Code,
            "Assistant prepared a deterministic planning packet.",
            [$"prompt_hash:{prompt.Prompt.Sha256[..12]}", $"packet:{SafeText(prompt.Packet?.PacketId)}"],
            prompt.Packet?.EvidenceReferences ?? []);
    }

    private static GoldenTurnTraceTurn HarnessTurn(
        int ordinal,
        string operation,
        string code,
        string summary,
        IReadOnlyList<string> evidence)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.Harness,
            "harness",
            operation,
            code,
            summary,
            [operation],
            evidence);
    }

    private static GoldenTurnTraceTurn DecisionTurn(
        int ordinal,
        string code,
        string summary,
        IReadOnlyList<string> evidence)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.Decision,
            "harness",
            "candidate_decision",
            code,
            summary,
            ["selected_itinerary_candidate"],
            evidence);
    }

    private static GoldenTurnTraceTurn BlockedTurn(
        int ordinal,
        string code,
        string summary,
        IReadOnlyList<string> evidence)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.Blocked,
            "harness",
            "safety_gate",
            code,
            summary,
            ["blocked", code],
            evidence);
    }

    private static GoldenTurnTraceTurn EvidenceTurn(
        int ordinal,
        string code,
        string summary,
        IReadOnlyList<string> evidence)
    {
        return Turn(
            ordinal,
            GoldenTurnKind.Evidence,
            "harness",
            "trip_run_snapshot",
            code,
            summary,
            ["trip_run_snapshot", code],
            evidence);
    }

    private static GoldenTurnTraceTurn Turn(
        int ordinal,
        GoldenTurnKind kind,
        string actor,
        string stage,
        string code,
        string summary,
        IReadOnlyList<string> markers,
        IReadOnlyList<string> evidence)
    {
        return new(
            TurnId: $"turn-{ordinal:00}",
            Kind: kind.ToString().ToLowerInvariant(),
            Actor: actor,
            Stage: SafeText(stage),
            Code: SafeText(code),
            Summary: SafeText(summary),
            Markers: markers.Select(SafeText).Where(IsSafeReference).Distinct(StringComparer.Ordinal).Take(MaxMarkers).ToArray(),
            EvidenceReferences: SafeReferences(evidence).Take(MaxEvidenceReferences).ToArray());
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IEnumerable<string> SafeReferences(IEnumerable<string> values)
    {
        return values
            .Where(IsSafeReference)
            .Select(SafeText)
            .Distinct(StringComparer.Ordinal);
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "[redacted]";
        }

        var trimmed = value.Trim();
        if (!IsSafeReference(trimmed))
        {
            return "[redacted]";
        }

        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }

    private static string SafeId(string? value)
    {
        var safe = SafeText(value);
        return string.Equals(safe, "[redacted]", StringComparison.Ordinal) ? "redacted" : safe;
    }

    private static bool IsSafeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("RAW_PROMPT", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PAYMENT", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("BOOKING_REF", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("CANDIDATE_DISPLAY", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record GoldenTurnTraceScript(
    string ScriptId,
    string Scenario,
    string PromptCategory,
    [property: JsonIgnore] string TransientUserPrompt,
    IReadOnlyList<GoldenTurnTraceScriptStep> Steps);

public sealed record GoldenTurnTraceScriptStep(
    string StepId,
    GoldenTurnKind Kind,
    string Operation);

public enum GoldenTurnKind
{
    User,
    Assistant,
    Harness,
    Decision,
    Blocked,
    Evidence
}

public sealed record GoldenTurnTraceResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    string ScriptId,
    string Scenario,
    PromptMetadata Prompt,
    IReadOnlyList<GoldenTurnTraceTurn> Turns,
    IReadOnlyList<string> EvidenceReferences,
    string TranscriptHash);

public sealed record GoldenTurnTraceTurn(
    string TurnId,
    string Kind,
    string Actor,
    string Stage,
    string Code,
    string Summary,
    IReadOnlyList<string> Markers,
    IReadOnlyList<string> EvidenceReferences);
