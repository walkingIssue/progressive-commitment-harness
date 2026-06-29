using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pch.Core;

namespace Pch.Harness;

public sealed class TripRunReplayAudit
{
    public const string AuditCompleteCode = "replay_audit_complete";
    public const string AuditBlockedCode = "replay_audit_blocked";
    public const string ReplayCompleteSnapshotCode = "replay_snapshot_complete";
    public const string ReplayPendingConfirmationCode = "replay_snapshot_pending_confirmation";
    public const string ReplayBlockedCompilerCode = "replay_snapshot_blocked_compiler";
    public const string ReplayBlockedCandidateCode = "replay_snapshot_blocked_candidate";
    public const string ReplayHoldPrepRequiredCode = "replay_snapshot_hold_prep_required";
    public const string ReplayInvalidCaseCode = "replay_invalid_case";
    public const string ReplayNondeterministicCode = "replay_nondeterministic";
    public const string ReplayMutatedStateCode = "replay_mutated_state";

    private static readonly DateTimeOffset FixedDecisionAt = new(2027, 4, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedObservedAt = new(2026, 6, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const int MaxCases = 8;
    private const int MaxEvidenceReferences = 8;
    private const int MaxTraceReferences = 8;
    private const int MaxTextLength = 120;

    private readonly TripRunSnapshotBuilder _snapshotBuilder = new();

    public TripRunReplayAuditResult ReplayDefaultCorpus()
    {
        var cases = DefaultCorpus()
            .Take(MaxCases)
            .Select(scenario => ReplayCase(scenario.CaseId, scenario.Scenario, scenario.CreateSession()))
            .ToArray();
        var isAccepted = cases.All(result => result.IsDeterministic && result.IsReadOnly);

        return new(
            IsAccepted: isAccepted,
            Code: isAccepted ? AuditCompleteCode : AuditBlockedCode,
            Summary: isAccepted
                ? "Trip run replay audit corpus completed."
                : "Trip run replay audit corpus found a replay failure.",
            CaseCount: cases.Length,
            Cases: cases);
    }

    public TripRunReplayCaseResult ReplayCase(string caseId, string scenario, TripSession session)
    {
        var safeCaseId = SafeText(caseId);
        var safeScenario = SafeText(scenario);
        if (session is null)
        {
            return InvalidCase(safeCaseId, safeScenario);
        }

        var before = SessionSignature.From(session);
        var first = _snapshotBuilder.Build(session);
        var afterFirst = SessionSignature.From(session);
        var second = _snapshotBuilder.Build(session);
        var afterSecond = SessionSignature.From(session);
        var firstSerialized = JsonSerializer.Serialize(first, JsonOptions);
        var secondSerialized = JsonSerializer.Serialize(second, JsonOptions);
        var isDeterministic = string.Equals(firstSerialized, secondSerialized, StringComparison.Ordinal);
        var isReadOnly = before == afterFirst && afterFirst == afterSecond;
        var replayCode = ResolveReplayCode(first.Code, isDeterministic, isReadOnly);

        return new(
            CaseId: safeCaseId,
            Scenario: safeScenario,
            Code: replayCode,
            Summary: ResolveReplaySummary(replayCode),
            SnapshotCode: SafeText(first.Code),
            SnapshotSummary: SafeText(first.Summary),
            IsCompleteSnapshot: first.IsComplete,
            IsBlockedSnapshot: first.IsBlocked,
            SnapshotId: SafeText(first.Snapshot.SnapshotId),
            SessionId: SafeText(first.Snapshot.SessionId),
            MissionId: SafeText(first.Snapshot.Mission.MissionId),
            SnapshotHash: Hash(firstSerialized),
            IsDeterministic: isDeterministic,
            IsReadOnly: isReadOnly,
            EvidenceReferenceCount: first.Snapshot.EvidenceReferences.Count,
            TraceReferenceCount: first.Snapshot.TraceReferences.Count,
            EvidenceReferences: SafeReferences(first.Snapshot.EvidenceReferences)
                .Take(MaxEvidenceReferences)
                .ToArray(),
            TraceReferences: first.Snapshot.TraceReferences
                .Where(reference => IsSafeReference(reference.TraceId) && IsSafeReference(reference.Kind))
                .Take(MaxTraceReferences)
                .Select(reference => new TripRunReplayTraceReference(
                    TraceId: SafeText(reference.TraceId),
                    Kind: SafeText(reference.Kind)))
                .ToArray());
    }

    private static IReadOnlyList<TripRunReplayScenario> DefaultCorpus()
    {
        return
        [
            new("replay-vacation", "vacation", CompleteVacationSession),
            new("replay-business", "business", CompleteBusinessSession),
            new("replay-funeral-downtime", "funeral_downtime", CompleteFuneralDowntimeSession),
            new("replay-family-support", "family_support", CompleteFamilySupportSession),
            new("replay-blocked-compiler", "blocked_compiler", BlockedCompilerSession),
            new("replay-blocked-candidate", "blocked_candidate", BlockedCandidateSession),
            new("replay-pending-confirmation", "pending_confirmation", PendingConfirmationSession),
            new("replay-missing-approval", "missing_approval", MissingApprovalSession)
        ];
    }

    private static TripSession CompleteVacationSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        Compile(session);
        Select(session, ItinerarySlotKind.Activity, "candidate-03", CandidateKind.Activity, "pool-logistics");
        Approve(session);
        return session;
    }

    private static TripSession CompleteBusinessSession()
    {
        var session = SyntheticTripFactory.CreateBusinessTripSession();
        Compile(session);
        Select(session, ItinerarySlotKind.Sleep, "candidate-business-hotel", CandidateKind.Hotel, "pool-business");
        Approve(session);
        return session;
    }

    private static TripSession CompleteFuneralDowntimeSession()
    {
        var session = SyntheticTripFactory.CreateFuneralDowntimeSession();
        Compile(session);
        Select(session, ItinerarySlotKind.Downtime, "candidate-quiet-garden", CandidateKind.Activity, "pool-downtime");
        Approve(session);
        return session;
    }

    private static TripSession CompleteFamilySupportSession()
    {
        var session = SyntheticTripFactory.CreateSession(3);
        session.ReplaceMission(session.Mission with
        {
            Purpose = "Help family move apartment",
            Commitments =
            [
                .. session.Mission.Commitments,
                new(
                    "commitment-family-support",
                    CommitmentKind.FixedAnchor,
                    "Family support window",
                    new DateTimeOffset(2027, 4, 2, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2027, 4, 2, 12, 0, 0, TimeSpan.Zero),
                    "Family apartment",
                    false,
                    false)
            ]
        });
        session.ReplaceMemoryDigest(new StructuredMemoryDigest(
            "digest-family-support",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Help family move apartment", "commitment: Family support window"],
            [],
            ["evidence-family-support"]));
        Compile(session);
        Select(session, ItinerarySlotKind.Activity, "candidate-03", CandidateKind.Activity, "pool-logistics");
        Approve(session);
        return session;
    }

    private static TripSession BlockedCompilerSession()
    {
        return SyntheticTripFactory.CreateSession(1);
    }

    private static TripSession BlockedCandidateSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        Compile(session);
        return session;
    }

    private static TripSession PendingConfirmationSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        var memory = new StructuredMemoryDigest(
            "digest-pending-confirmation",
            session.SessionId,
            session.Mission.MissionId,
            ["purpose: Vacation"],
            [
                new(
                    "/mission/destination_country",
                    "Japan",
                    AuthoritySource.StrongModelInference,
                    "requires_confirmation",
                    ["evidence-pending-confirmation"])
            ],
            ["evidence-pending-confirmation"]);
        session.ReplaceMemoryDigest(memory);
        Compile(session, memory);
        return session;
    }

    private static TripSession MissingApprovalSession()
    {
        var session = SyntheticTripFactory.CreateSession(1);
        Compile(session);
        Select(session, ItinerarySlotKind.Activity, "candidate-03", CandidateKind.Activity, "pool-logistics");
        return session;
    }

    private static void Compile(TripSession session, StructuredMemoryDigest? memory = null)
    {
        new ItinerarySlotCompiler().Compile(session, new ItineraryCompilationRequest(
            session.SessionId,
            null,
            null,
            memory,
            []));
    }

    private static void Select(
        TripSession session,
        ItinerarySlotKind slotKind,
        string candidateId,
        CandidateKind candidateKind,
        string poolId)
    {
        var slot = session.LastItineraryCompilation!.Days
            .SelectMany(day => day.Slots)
            .First(slot => slot.Kind == slotKind);
        session.AssociateItineraryCandidatePool(slot.SlotId, poolId);
        new ItineraryCandidateApplication().Apply(session, new ItinerarySlotDecisionRequest(
            session.SessionId,
            slot.SlotId,
            ItinerarySlotDecisionKind.Selected,
            slot.Kind,
            candidateId,
            candidateKind,
            FixedDecisionAt));
    }

    private static void Approve(TripSession session)
    {
        session.RecordApproval(new ApprovalToken("approval-replay", "approved-replay-token", FixedObservedAt));
    }

    private static TripRunReplayCaseResult InvalidCase(string caseId, string scenario)
    {
        return new(
            CaseId: caseId,
            Scenario: scenario,
            Code: ReplayInvalidCaseCode,
            Summary: "Trip run replay case failed validation.",
            SnapshotCode: "unavailable",
            SnapshotSummary: "Trip run replay case failed validation.",
            IsCompleteSnapshot: false,
            IsBlockedSnapshot: true,
            SnapshotId: "unavailable",
            SessionId: "unavailable",
            MissionId: "unavailable",
            SnapshotHash: "unavailable",
            IsDeterministic: false,
            IsReadOnly: true,
            EvidenceReferenceCount: 0,
            TraceReferenceCount: 0,
            EvidenceReferences: [],
            TraceReferences: []);
    }

    private static string ResolveReplayCode(string snapshotCode, bool isDeterministic, bool isReadOnly)
    {
        if (!isDeterministic)
        {
            return ReplayNondeterministicCode;
        }

        if (!isReadOnly)
        {
            return ReplayMutatedStateCode;
        }

        return snapshotCode switch
        {
            TripRunSnapshotBuilder.CompleteCode => ReplayCompleteSnapshotCode,
            TripRunSnapshotBuilder.PendingConfirmationCode => ReplayPendingConfirmationCode,
            TripRunSnapshotBuilder.BlockedCompilerCode => ReplayBlockedCompilerCode,
            TripRunSnapshotBuilder.BlockedCandidateCode => ReplayBlockedCandidateCode,
            TripRunSnapshotBuilder.HoldPrepRequiredCode => ReplayHoldPrepRequiredCode,
            _ => ReplayInvalidCaseCode
        };
    }

    private static string ResolveReplaySummary(string replayCode)
    {
        return replayCode switch
        {
            ReplayCompleteSnapshotCode => "Trip run replay snapshot completed.",
            ReplayPendingConfirmationCode => "Trip run replay snapshot has pending confirmations.",
            ReplayBlockedCompilerCode => "Trip run replay snapshot is blocked by compiler state.",
            ReplayBlockedCandidateCode => "Trip run replay snapshot is blocked by candidate state.",
            ReplayHoldPrepRequiredCode => "Trip run replay snapshot requires approval before mock hold preparation.",
            ReplayNondeterministicCode => "Trip run replay case was not deterministic.",
            ReplayMutatedStateCode => "Trip run replay case mutated session state.",
            _ => "Trip run replay case failed validation."
        };
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

    private static bool IsSafeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Contains("RAW_", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PROVIDER_PAYLOAD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PROMPT", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("APPROVAL_TOKEN", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("HOLD_REFERENCE", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TripRunReplayScenario(
        string CaseId,
        string Scenario,
        Func<TripSession> CreateSession);

    private sealed record SessionSignature(
        HarnessStage Stage,
        int CandidatePoolCount,
        int ActionCount,
        int FormValueCount,
        int SelectedCandidateCount,
        int ApprovalTokenCount,
        int DeferredSlotCount,
        int HandoffCount,
        int ItineraryDecisionCount,
        int DecisionRecordCount,
        int EvidenceItemCount,
        int ClaimCount,
        string? MemoryDigestId,
        string? ItineraryCode)
    {
        public static SessionSignature From(TripSession session)
        {
            return new(
                session.Stage,
                session.CandidatePools.Count,
                session.Actions.Count,
                session.FormValues.Count,
                session.SelectedCandidateIds.Count,
                session.ApprovalTokens.Count,
                session.DeferredSlots.Count,
                session.Handoffs.Count,
                session.ItineraryDecisions.Count,
                session.DecisionLedger.Records.Count,
                session.EvidenceTrace.Items.Count,
                session.ClaimLedger.Claims.Count,
                session.MemoryDigest?.DigestId,
                session.LastItineraryCompilation?.Code);
        }
    }
}

public sealed record TripRunReplayAuditResult(
    bool IsAccepted,
    string Code,
    string Summary,
    int CaseCount,
    IReadOnlyList<TripRunReplayCaseResult> Cases);

public sealed record TripRunReplayCaseResult(
    string CaseId,
    string Scenario,
    string Code,
    string Summary,
    string SnapshotCode,
    string SnapshotSummary,
    bool IsCompleteSnapshot,
    bool IsBlockedSnapshot,
    string SnapshotId,
    string SessionId,
    string MissionId,
    string SnapshotHash,
    bool IsDeterministic,
    bool IsReadOnly,
    int EvidenceReferenceCount,
    int TraceReferenceCount,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<TripRunReplayTraceReference> TraceReferences);

public sealed record TripRunReplayTraceReference(
    string TraceId,
    string Kind);
