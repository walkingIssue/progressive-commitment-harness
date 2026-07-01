using Pch.Providers.LiveMissionProposal;
using Pch.Providers.ModelRoles;

namespace Pch.Providers.LiveTurns;

public sealed class LiveTurnEvaluator
{
    private readonly LiveTurnRunner _runner;

    public LiveTurnEvaluator(LiveTurnRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<SanitizedLiveTurnLogRow>> EvaluateAsync(
        IReadOnlyList<LiveTurnEvalCase?>? cases,
        LiveTurnOptions options,
        CancellationToken cancellationToken = default)
    {
        if (cases is null)
        {
            return [Rejected(LiveTurnRunner.OutcomeProviderSchemaInvalid, ProviderFailureClass.ProviderSchemaInvalid)];
        }

        var rows = new List<SanitizedLiveTurnLogRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            if (evalCase?.Packet is null)
            {
                rows.Add(Rejected(LiveTurnRunner.OutcomeProviderSchemaInvalid, ProviderFailureClass.ProviderSchemaInvalid));
                continue;
            }

            try
            {
                var result = await _runner.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(ExceptionRow(ex));
            }
        }

        return rows;
    }

    private static SanitizedLiveTurnLogRow ToRow(LiveTurnEvalCase evalCase, LiveTurnResult result)
    {
        if (!IsSafePacket(evalCase.Packet) ||
            !string.Equals(evalCase.Packet.RunId, result.RunId, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Packet.TurnId, result.TurnId, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal) ||
            !string.Equals(evalCase.Packet.SessionId, result.SessionId, StringComparison.Ordinal) ||
            evalCase.Packet.Role != result.Role)
        {
            return Rejected(LiveTurnRunner.OutcomePacketMismatch, ProviderFailureClass.ProviderSchemaInvalid);
        }

        if (!evalCase.Packet.AllowedOutputKinds.Contains(result.OutputKind) ||
            !Enum.IsDefined(result.OutputKind) ||
            !Enum.IsDefined(result.Role))
        {
            return Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid);
        }

        if (result.HasUnsafeValue)
        {
            return Rejected(LiveTurnRunner.OutcomeUnsafeValueRedacted, ProviderFailureClass.ProviderSchemaInvalid);
        }

        return result.OutputKind switch
        {
            LiveTurnOutputKind.MissionProposal => MissionProposalRow(evalCase, result),
            LiveTurnOutputKind.PendingConfirmationQuestion => PendingQuestionRow(evalCase, result),
            LiveTurnOutputKind.ChoiceSet => ChoiceSetRow(evalCase, result),
            LiveTurnOutputKind.SummaryFallbackNotice => SummaryNoticeRow(evalCase, result),
            _ => Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid)
        };
    }

    private static SanitizedLiveTurnLogRow MissionProposalRow(LiveTurnEvalCase evalCase, LiveTurnResult result)
    {
        if (result.MissionProposal is null ||
            result.PendingQuestion is not null ||
            result.ChoiceSet is not null ||
            result.SummaryNotice is not null ||
            !Enum.IsDefined(result.MissionProposal.MissionKind))
        {
            return Rejected(LiveTurnRunner.OutcomeProviderSchemaInvalid, ProviderFailureClass.ProviderSchemaInvalid);
        }

        if (result.MissionProposal.Fields is null ||
            result.MissionProposal.Commitments is null ||
            result.MissionProposal.PendingConfirmations is null ||
            result.MissionProposal.Fields.Any(field =>
                field is null ||
                !IsSafeFieldPath(field.FieldPath) ||
                !Enum.IsDefined(field.AuthoritySource)) ||
            result.MissionProposal.Commitments.Any(commitment =>
                commitment is null ||
                !LiveTurnRunner.IsSafeIdentifier(commitment.CommitmentId) ||
                !Enum.IsDefined(commitment.CommitmentKind) ||
                !Enum.IsDefined(commitment.Priority) ||
                !Enum.IsDefined(commitment.AuthoritySource)) ||
            result.MissionProposal.PendingConfirmations.Any(pending =>
                pending is null ||
                !LiveTurnRunner.IsSafeIdentifier(pending.ConfirmationId) ||
                !IsSafeFieldPath(pending.FieldPath) ||
                !Enum.IsDefined(pending.ReasonCode) ||
                !Enum.IsDefined(pending.AuthoritySource)))
        {
            return Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid);
        }

        return Accepted(evalCase, result, [], []);
    }

    private static SanitizedLiveTurnLogRow PendingQuestionRow(LiveTurnEvalCase evalCase, LiveTurnResult result)
    {
        if (result.PendingQuestion is null ||
            result.MissionProposal is not null ||
            result.ChoiceSet is not null ||
            result.SummaryNotice is not null ||
            !LiveTurnRunner.IsSafeIdentifier(result.PendingQuestion.QuestionId) ||
            !IsSafeFieldPath(result.PendingQuestion.FieldPath) ||
            !Enum.IsDefined(result.PendingQuestion.ReasonCode))
        {
            return Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid);
        }

        return Accepted(evalCase, result, [], []);
    }

    private static SanitizedLiveTurnLogRow ChoiceSetRow(LiveTurnEvalCase evalCase, LiveTurnResult result)
    {
        if (result.ChoiceSet is null ||
            result.MissionProposal is not null ||
            result.PendingQuestion is not null ||
            result.SummaryNotice is not null ||
            !LiveTurnRunner.IsSafeIdentifier(result.ChoiceSet.ChoiceSetId) ||
            result.ChoiceSet.Options is null ||
            result.ChoiceSet.Options.Count == 0 ||
            !Enum.IsDefined(result.ChoiceSet.UiMood))
        {
            return Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid);
        }

        var trusted = evalCase.Packet.TrustedCandidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidateIds = new List<string>(result.ChoiceSet.Options.Count);
        var categories = new List<LiveTurnCandidateCategory>(result.ChoiceSet.Options.Count);
        foreach (var option in result.ChoiceSet.Options)
        {
            if (option is null ||
                !trusted.TryGetValue(option.CandidateId, out var trustedCandidate) ||
                !seen.Add(option.CandidateId) ||
                !string.Equals(trustedCandidate.SlotId, option.SlotId, StringComparison.Ordinal) ||
                trustedCandidate.Category != option.Category)
            {
                return Rejected(LiveTurnRunner.OutcomePacketMismatch, ProviderFailureClass.ProviderSchemaInvalid);
            }

            candidateIds.Add(trustedCandidate.CandidateId);
            categories.Add(trustedCandidate.Category);
        }

        return Accepted(evalCase, result, candidateIds.Order(StringComparer.Ordinal).ToArray(), categories.OrderBy(category => category).ToArray());
    }

    private static SanitizedLiveTurnLogRow SummaryNoticeRow(LiveTurnEvalCase evalCase, LiveTurnResult result)
    {
        if (result.SummaryNotice is null ||
            result.MissionProposal is not null ||
            result.PendingQuestion is not null ||
            result.ChoiceSet is not null ||
            !Enum.IsDefined(result.SummaryNotice.NoticeKind))
        {
            return Rejected(LiveTurnRunner.OutcomeUnsupportedValue, ProviderFailureClass.ProviderSchemaInvalid);
        }

        return Accepted(evalCase, result, [], []);
    }

    private static SanitizedLiveTurnLogRow Accepted(
        LiveTurnEvalCase evalCase,
        LiveTurnResult result,
        IReadOnlyList<string> candidateIds,
        IReadOnlyList<LiveTurnCandidateCategory> candidateCategories) =>
        new(
            evalCase.Name,
            evalCase.Packet.RunId,
            evalCase.Packet.TurnId,
            evalCase.Packet.PacketId,
            true,
            LiveTurnRunner.OutcomeAccepted,
            null,
            null,
            result.OutputKind,
            evalCase.Packet.Role,
            candidateIds,
            candidateCategories,
            candidateIds.Count,
            (int)Math.Min(result.Duration.TotalMilliseconds, int.MaxValue),
            DurationBucket(result.Duration),
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);

    private static bool IsSafePacket(LiveTurnPacket packet) =>
        LiveTurnRunner.IsSafeIdentifier(packet.RunId) &&
        LiveTurnRunner.IsSafeIdentifier(packet.TurnId) &&
        LiveTurnRunner.IsSafeIdentifier(packet.PacketId) &&
        LiveTurnRunner.IsSafeIdentifier(packet.SessionId) &&
        Enum.IsDefined(packet.Role) &&
        packet.AllowedOutputKinds is not null &&
        packet.AllowedOutputKinds.Count > 0 &&
        packet.AllowedOutputKinds.All(Enum.IsDefined) &&
        packet.TrustedCandidates is not null &&
        packet.TrustedCandidates.All(candidate =>
            LiveTurnRunner.IsSafeIdentifier(candidate.CandidateId) &&
            LiveTurnRunner.IsSafeIdentifier(candidate.SlotId) &&
            Enum.IsDefined(candidate.Category));

    private static bool IsSafeFieldPath(string? fieldPath) =>
        !string.IsNullOrWhiteSpace(fieldPath) &&
        fieldPath.StartsWith("/mission/", StringComparison.Ordinal) &&
        LiveTurnRunner.IsSafeIdentifier(fieldPath);

    private static SanitizedLiveTurnLogRow ExceptionRow(Exception exception)
    {
        if (exception is LiveTurnGuardException guard)
        {
            return Rejected(guard.OutcomeCode, guard.FailureClass);
        }

        var failureClass = ProviderFailureClassifier.Classify(exception);
        return Rejected(ProviderFailureClassifier.OutcomeFor(failureClass), failureClass);
    }

    public static SanitizedLiveTurnLogRow Rejected(
        string outcomeCode,
        ProviderFailureClass? failureClass) =>
        new(
            LiveTurnRunner.RejectedRowName,
            LiveTurnRunner.RejectedRunId,
            LiveTurnRunner.RejectedTurnId,
            LiveTurnRunner.RejectedPacketId,
            false,
            outcomeCode,
            failureClass,
            failureClass is null ? null : ProviderFailureClassifier.CodeFor(failureClass.Value),
            null,
            null,
            [],
            [],
            0,
            null,
            null,
            null,
            null,
            null,
            null);

    private static string DurationBucket(TimeSpan duration)
    {
        if (duration.TotalMilliseconds < 1_000)
        {
            return "lt_1s";
        }

        if (duration.TotalMilliseconds < 5_000)
        {
            return "lt_5s";
        }

        if (duration.TotalMilliseconds < 15_000)
        {
            return "lt_15s";
        }

        return "gte_15s";
    }
}
