using Pch.Core;

namespace Pch.Harness;

public sealed class FidelityMatrix
{
    public const string MatrixCompleteCode = "fidelity_matrix_complete";
    public const string MatrixBlockedCode = "fidelity_matrix_blocked";
    public const string InvalidInputCode = "fidelity_matrix_invalid_input";

    public const string HarnessOnlyOutcome = "harness_only";
    public const string SmallModelCandidateOutcome = "small_model_candidate";
    public const string StrongModelRequiredOutcome = "strong_model_required";
    public const string BlockedUntilReviewOutcome = "blocked_until_review";

    private const int MaxEntries = 32;
    private const int MaxEvidenceReferences = 8;
    private const int MaxTraceReferences = 8;
    private const int MaxTextLength = 120;

    public FidelityMatrixResult BuildDefaultMatrix()
    {
        var session = SyntheticTripFactory.CreateSession(7);
        var projection = new ProjectionService();
        var packets = Enum.GetValues<HarnessStage>()
            .Select(stage => projection.Project(session, stage))
            .ToArray();
        var replayAudit = new TripRunReplayAudit().ReplayDefaultCorpus();

        return Build(new FidelityMatrixRequest(packets, replayAudit));
    }

    public FidelityMatrixResult Build(FidelityMatrixRequest request)
    {
        if (request is null || request.StagePackets is null || request.ReplayAudit is null)
        {
            return InvalidMatrix();
        }

        var stageEntries = request.StagePackets
            .Take(MaxEntries)
            .Select((packet, index) => EvaluateStagePacket(packet, index + 1));
        var replayEntries = request.ReplayAudit.Cases
            .Take(Math.Max(0, MaxEntries - request.StagePackets.Count))
            .Select((replayCase, index) => EvaluateReplayCase(replayCase, index + 1));
        var entries = stageEntries.Concat(replayEntries).Take(MaxEntries).ToArray();
        var accepted = entries.All(entry => entry.Metrics.SchemaValid
            && entry.Metrics.IsReadOnly
            && entry.Metrics.MutationSafe
            && entry.Metrics.UnsupportedClaimCount == 0);

        return new(
            IsAccepted: accepted,
            Code: accepted ? MatrixCompleteCode : MatrixBlockedCode,
            Summary: accepted
                ? "Fidelity ownership matrix completed."
                : "Fidelity ownership matrix found blocked outputs.",
            EntryCount: entries.Length,
            Totals: FidelityMatrixTotals.From(entries),
            Entries: entries);
    }

    private static FidelityMatrixEntry EvaluateStagePacket(StagePacket? packet, int ordinal)
    {
        if (packet is null)
        {
            return InvalidEntry($"stage-packet-{ordinal}", "stage_packet");
        }

        var unsupportedClaimCount = UnsafeTextCount(PacketStrings(packet))
            + packet.Candidates.Count(candidate => candidate.EvidenceIds.Count == 0);
        var schemaValid = IsValidPacketSchema(packet);
        var candidateIdsPreserved = CandidateIdsPreserved(packet);
        var stage = ParseStage(packet.Stage);
        var metrics = new FidelityMetrics(
            SchemaValid: schemaValid,
            Faithful: schemaValid && unsupportedClaimCount == 0,
            CandidateIdsPreserved: candidateIdsPreserved,
            UnsupportedClaimCount: unsupportedClaimCount,
            NeedsFallback: NeedsStageFallback(stage, schemaValid, unsupportedClaimCount),
            IsReadOnly: true,
            MutationSafe: true);
        var ownership = ResolveStageOwnership(stage, metrics);

        return new(
            EntryId: SafeText(packet.PacketId),
            InputKind: "stage_packet",
            Stage: SafeText(packet.Stage),
            Scenario: "deterministic_stage_packet",
            Ownership: ownership,
            Code: CodeForOwnership(ownership),
            Summary: SummaryForOwnership(ownership),
            Metrics: metrics,
            EvidenceReferences: SafeReferences(packet.Candidates.SelectMany(candidate => candidate.EvidenceIds))
                .Take(MaxEvidenceReferences)
                .ToArray(),
            TraceReferences: SafeReferences(packet.TraceRequirements)
                .Take(MaxTraceReferences)
                .ToArray());
    }

    private static FidelityMatrixEntry EvaluateReplayCase(TripRunReplayCaseResult? replayCase, int ordinal)
    {
        if (replayCase is null)
        {
            return InvalidEntry($"replay-case-{ordinal}", "trip_run_replay");
        }

        var unsupportedClaimCount = UnsafeTextCount(ReplayStrings(replayCase));
        var schemaValid = IsValidReplaySchema(replayCase);
        var metrics = new FidelityMetrics(
            SchemaValid: schemaValid,
            Faithful: schemaValid && unsupportedClaimCount == 0,
            CandidateIdsPreserved: true,
            UnsupportedClaimCount: unsupportedClaimCount,
            NeedsFallback: NeedsReplayFallback(replayCase),
            IsReadOnly: replayCase.IsReadOnly,
            MutationSafe: replayCase.IsReadOnly);
        var ownership = ResolveReplayOwnership(replayCase, metrics);

        return new(
            EntryId: SafeText(replayCase.CaseId),
            InputKind: "trip_run_replay",
            Stage: "TripRunSnapshot",
            Scenario: SafeText(replayCase.Scenario),
            Ownership: ownership,
            Code: CodeForOwnership(ownership),
            Summary: SummaryForOwnership(ownership),
            Metrics: metrics,
            EvidenceReferences: SafeReferences(replayCase.EvidenceReferences)
                .Take(MaxEvidenceReferences)
                .ToArray(),
            TraceReferences: SafeReferences(replayCase.TraceReferences.Select(reference => reference.TraceId))
                .Take(MaxTraceReferences)
                .ToArray());
    }

    private static FidelityMatrixEntry InvalidEntry(string entryId, string inputKind)
    {
        return new(
            EntryId: SafeText(entryId),
            InputKind: inputKind,
            Stage: "unknown",
            Scenario: "invalid",
            Ownership: BlockedUntilReviewOutcome,
            Code: InvalidInputCode,
            Summary: "Fidelity matrix input failed validation.",
            Metrics: new(
                SchemaValid: false,
                Faithful: false,
                CandidateIdsPreserved: false,
                UnsupportedClaimCount: 0,
                NeedsFallback: true,
                IsReadOnly: true,
                MutationSafe: true),
            EvidenceReferences: [],
            TraceReferences: []);
    }

    private static FidelityMatrixResult InvalidMatrix()
    {
        return new(
            IsAccepted: false,
            Code: InvalidInputCode,
            Summary: "Fidelity matrix request failed validation.",
            EntryCount: 0,
            Totals: FidelityMatrixTotals.Empty,
            Entries: []);
    }

    private static bool IsValidPacketSchema(StagePacket packet)
    {
        return !string.IsNullOrWhiteSpace(packet.PacketId)
            && !string.IsNullOrWhiteSpace(packet.SessionId)
            && !string.IsNullOrWhiteSpace(packet.Stage)
            && !string.IsNullOrWhiteSpace(packet.CurrentSubtask)
            && packet.LoadBearingFacts.Count <= 12
            && packet.Candidates.Count <= 6
            && packet.Constraints.Count <= 8
            && packet.AllowedActions.Count > 0
            && packet.AllowedActions.All(HarnessAction.KnownKinds.Contains)
            && packet.Candidates.All(candidate => !string.IsNullOrWhiteSpace(candidate.CandidateId)
                && !string.IsNullOrWhiteSpace(candidate.Kind)
                && candidate.EvidenceIds.Count > 0);
    }

    private static bool IsValidReplaySchema(TripRunReplayCaseResult replayCase)
    {
        return !string.IsNullOrWhiteSpace(replayCase.CaseId)
            && !string.IsNullOrWhiteSpace(replayCase.Scenario)
            && !string.IsNullOrWhiteSpace(replayCase.Code)
            && !string.IsNullOrWhiteSpace(replayCase.SnapshotCode)
            && replayCase.SnapshotHash.Length == 64
            && replayCase.EvidenceReferences.Count <= MaxEvidenceReferences
            && replayCase.TraceReferences.Count <= MaxTraceReferences;
    }

    private static bool CandidateIdsPreserved(StagePacket packet)
    {
        var ids = packet.Candidates.Select(candidate => candidate.CandidateId).ToArray();
        return ids.All(id => !string.IsNullOrWhiteSpace(id))
            && ids.Length == ids.Distinct(StringComparer.Ordinal).Count()
            && packet.TraceRequirements.Any(requirement => requirement.Contains("candidate IDs", StringComparison.OrdinalIgnoreCase));
    }

    private static HarnessStage? ParseStage(string stage)
    {
        return Enum.TryParse<HarnessStage>(stage, ignoreCase: true, out var parsed) ? parsed : null;
    }

    private static bool NeedsStageFallback(HarnessStage? stage, bool schemaValid, int unsupportedClaimCount)
    {
        return !schemaValid
            || unsupportedClaimCount > 0
            || stage is HarnessStage.Logistics
                or HarnessStage.ConflictVerify
                or HarnessStage.EvidencePacket
                or HarnessStage.ApprovalQueue
                or HarnessStage.MockedBooking;
    }

    private static bool NeedsReplayFallback(TripRunReplayCaseResult replayCase)
    {
        return !replayCase.IsDeterministic
            || !replayCase.IsReadOnly
            || replayCase.SnapshotCode is TripRunSnapshotBuilder.PendingConfirmationCode
                or TripRunSnapshotBuilder.BlockedCompilerCode
                or TripRunSnapshotBuilder.BlockedCandidateCode
                or TripRunSnapshotBuilder.HoldPrepRequiredCode;
    }

    private static string ResolveStageOwnership(HarnessStage? stage, FidelityMetrics metrics)
    {
        if (!metrics.SchemaValid || metrics.UnsupportedClaimCount > 0 || !metrics.CandidateIdsPreserved)
        {
            return BlockedUntilReviewOutcome;
        }

        return stage switch
        {
            HarnessStage.Intake or HarnessStage.SlotCollection => HarnessOnlyOutcome,
            HarnessStage.Posture or HarnessStage.DaySkeletonGeneration or HarnessStage.Meals or HarnessStage.ActivitiesDowntime => SmallModelCandidateOutcome,
            HarnessStage.Logistics or HarnessStage.ConflictVerify or HarnessStage.EvidencePacket => StrongModelRequiredOutcome,
            HarnessStage.ApprovalQueue or HarnessStage.MockedBooking => BlockedUntilReviewOutcome,
            _ => BlockedUntilReviewOutcome
        };
    }

    private static string ResolveReplayOwnership(TripRunReplayCaseResult replayCase, FidelityMetrics metrics)
    {
        if (!metrics.SchemaValid || !metrics.IsReadOnly || !metrics.MutationSafe || metrics.UnsupportedClaimCount > 0)
        {
            return BlockedUntilReviewOutcome;
        }

        return replayCase.SnapshotCode switch
        {
            TripRunSnapshotBuilder.CompleteCode => HarnessOnlyOutcome,
            TripRunSnapshotBuilder.PendingConfirmationCode => StrongModelRequiredOutcome,
            TripRunSnapshotBuilder.BlockedCompilerCode
                or TripRunSnapshotBuilder.BlockedCandidateCode
                or TripRunSnapshotBuilder.HoldPrepRequiredCode => BlockedUntilReviewOutcome,
            _ => BlockedUntilReviewOutcome
        };
    }

    private static string CodeForOwnership(string ownership)
    {
        return ownership switch
        {
            HarnessOnlyOutcome => "fidelity_harness_only",
            SmallModelCandidateOutcome => "fidelity_small_model_candidate",
            StrongModelRequiredOutcome => "fidelity_strong_model_required",
            _ => "fidelity_blocked_until_review"
        };
    }

    private static string SummaryForOwnership(string ownership)
    {
        return ownership switch
        {
            HarnessOnlyOutcome => "Fidelity output is owned by deterministic harness logic.",
            SmallModelCandidateOutcome => "Fidelity output is eligible for small-model bake-off.",
            StrongModelRequiredOutcome => "Fidelity output requires strong-model review.",
            _ => "Fidelity output is blocked until review."
        };
    }

    private static IEnumerable<string> PacketStrings(StagePacket packet)
    {
        return new[]
            {
                packet.PacketId,
                packet.SessionId,
                packet.Stage,
                packet.CurrentSubtask
            }
            .Concat(packet.LoadBearingFacts)
            .Concat(packet.Constraints)
            .Concat(packet.AuthorityHints)
            .Concat(packet.AllowedActions)
            .Concat(packet.TraceRequirements)
            .Concat(packet.Candidates.Select(candidate => candidate.CandidateId))
            .Concat(packet.Candidates.Select(candidate => candidate.Kind))
            .Concat(packet.Candidates.Select(candidate => candidate.Title))
            .Concat(packet.Candidates.Select(candidate => candidate.Summary))
            .Concat(packet.Candidates.SelectMany(candidate => candidate.EvidenceIds));
    }

    private static IEnumerable<string> ReplayStrings(TripRunReplayCaseResult replayCase)
    {
        return new[]
            {
                replayCase.CaseId,
                replayCase.Scenario,
                replayCase.Code,
                replayCase.Summary,
                replayCase.SnapshotCode,
                replayCase.SnapshotSummary,
                replayCase.SnapshotId,
                replayCase.SessionId,
                replayCase.MissionId,
                replayCase.SnapshotHash
            }
            .Concat(replayCase.EvidenceReferences)
            .Concat(replayCase.TraceReferences.Select(reference => reference.TraceId))
            .Concat(replayCase.TraceReferences.Select(reference => reference.Kind));
    }

    private static int UnsafeTextCount(IEnumerable<string> values)
    {
        return values.Count(value => !IsSafeReference(value));
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
}

public sealed record FidelityMatrixRequest(
    IReadOnlyList<StagePacket> StagePackets,
    TripRunReplayAuditResult ReplayAudit);

public sealed record FidelityMatrixResult(
    bool IsAccepted,
    string Code,
    string Summary,
    int EntryCount,
    FidelityMatrixTotals Totals,
    IReadOnlyList<FidelityMatrixEntry> Entries);

public sealed record FidelityMatrixTotals(
    int HarnessOnlyCount,
    int SmallModelCandidateCount,
    int StrongModelRequiredCount,
    int BlockedUntilReviewCount,
    int UnsupportedClaimCount,
    int FallbackNeedCount)
{
    public static FidelityMatrixTotals Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public static FidelityMatrixTotals From(IReadOnlyList<FidelityMatrixEntry> entries)
    {
        return new(
            HarnessOnlyCount: entries.Count(entry => entry.Ownership == FidelityMatrix.HarnessOnlyOutcome),
            SmallModelCandidateCount: entries.Count(entry => entry.Ownership == FidelityMatrix.SmallModelCandidateOutcome),
            StrongModelRequiredCount: entries.Count(entry => entry.Ownership == FidelityMatrix.StrongModelRequiredOutcome),
            BlockedUntilReviewCount: entries.Count(entry => entry.Ownership == FidelityMatrix.BlockedUntilReviewOutcome),
            UnsupportedClaimCount: entries.Sum(entry => entry.Metrics.UnsupportedClaimCount),
            FallbackNeedCount: entries.Count(entry => entry.Metrics.NeedsFallback));
    }
}

public sealed record FidelityMatrixEntry(
    string EntryId,
    string InputKind,
    string Stage,
    string Scenario,
    string Ownership,
    string Code,
    string Summary,
    FidelityMetrics Metrics,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> TraceReferences);

public sealed record FidelityMetrics(
    bool SchemaValid,
    bool Faithful,
    bool CandidateIdsPreserved,
    int UnsupportedClaimCount,
    bool NeedsFallback,
    bool IsReadOnly,
    bool MutationSafe);
