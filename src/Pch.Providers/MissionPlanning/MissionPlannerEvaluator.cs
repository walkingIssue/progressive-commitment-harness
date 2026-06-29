namespace Pch.Providers.MissionPlanning;

public sealed class MissionPlannerEvaluator
{
    public const string OutcomeAccepted = "mission_planner_accepted";
    public const string OutcomePacketIdMismatch = "mission_planner_packet_id_mismatch";
    public const string OutcomeKindMismatch = "mission_planner_kind_mismatch";
    public const string OutcomeUnsupportedMissionKind = "mission_planner_unsupported_mission_kind";
    public const string OutcomeError = "mission_planner_error";

    private readonly IMissionPlannerClient _planner;

    public MissionPlannerEvaluator(IMissionPlannerClient planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    }

    public async Task<IReadOnlyList<SanitizedMissionPlannerEvalRow>> EvaluateAsync(
        IReadOnlyList<MissionPlannerEvalCase> cases,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedMissionPlannerEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var result = await _planner.PlanAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                rows.Add(ToRow(evalCase, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new SanitizedMissionPlannerEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    evalCase.Packet.Scenario,
                    false,
                    evalCase.ExpectedMissionKind,
                    null,
                    OutcomeError,
                    ToErrorCode(ex),
                    0,
                    0,
                    0,
                    0,
                    0,
                    null,
                    null,
                    null,
                    null));
            }
        }

        return rows;
    }

    private static SanitizedMissionPlannerEvalRow ToRow(
        MissionPlannerEvalCase evalCase,
        MissionPlannerResult result)
    {
        var packetMatches = string.Equals(evalCase.Packet.PacketId, result.PacketId, StringComparison.Ordinal);
        var kindAllowed = MissionKindPolicy.IsAllowed(result.MissionKind);
        var kindMatches = kindAllowed && string.Equals(evalCase.ExpectedMissionKind, result.MissionKind, StringComparison.Ordinal);
        var outcome = OutcomePacketIdMismatch;
        if (packetMatches)
        {
            outcome = kindAllowed
                ? kindMatches ? OutcomeAccepted : OutcomeKindMismatch
                : OutcomeUnsupportedMissionKind;
        }

        return new SanitizedMissionPlannerEvalRow(
            evalCase.Name,
            evalCase.Packet.PacketId,
            evalCase.Packet.Scenario,
            packetMatches && kindMatches,
            evalCase.ExpectedMissionKind,
            packetMatches && kindAllowed ? result.MissionKind : null,
            outcome,
            null,
            result.Fields.Count(field => field.AuthoritySource == MissionProposalSource.UserStated),
            result.Fields.Count(field => field.AuthoritySource == MissionProposalSource.ModelInferred),
            result.Commitments.Count,
            result.Constraints.Count,
            result.PendingConfirmations.Count,
            result.ResponseContentLength,
            result.Provider,
            result.Model,
            result.RequestId);
    }

    private static string ToErrorCode(Exception exception) =>
        exception.GetType().Name.Contains("Provider", StringComparison.Ordinal)
            ? "provider_error"
            : "mission_planner_error";
}
