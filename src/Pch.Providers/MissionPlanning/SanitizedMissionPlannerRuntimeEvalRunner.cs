namespace Pch.Providers.MissionPlanning;

public sealed class SanitizedMissionPlannerRuntimeEvalRunner
{
    private readonly MissionPlannerRuntimeClient _runtime;

    public SanitizedMissionPlannerRuntimeEvalRunner(MissionPlannerRuntimeClient runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public SanitizedMissionPlannerRuntimeEvalRunner(IMissionPlannerClient planner)
        : this(new MissionPlannerRuntimeClient(planner))
    {
    }

    public async Task<IReadOnlyList<SanitizedMissionPlannerRuntimeEvalRow>> EvaluateAsync(
        IReadOnlyList<MissionPlannerEvalCase> cases,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);

        var rows = new List<SanitizedMissionPlannerRuntimeEvalRow>(cases.Count);
        foreach (var evalCase in cases)
        {
            try
            {
                var handoff = await _runtime.RunAsync(evalCase.Packet, options, cancellationToken).ConfigureAwait(false);
                var proposal = handoff.Proposal;
                var actualMissionKind = handoff.DecodeOutcomeCode == MissionPlannerRuntimeBridge.DecodeAccepted
                    ? proposal?.MissionKind
                    : null;

                rows.Add(new SanitizedMissionPlannerRuntimeEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    evalCase.Packet.Scenario,
                    handoff.IsAccepted &&
                        string.Equals(evalCase.ExpectedMissionKind, actualMissionKind, StringComparison.Ordinal),
                    evalCase.ExpectedMissionKind,
                    actualMissionKind,
                    handoff.DecodeOutcomeCode,
                    handoff.IntakeOutcomeCode,
                    null,
                    proposal?.UserStatedFieldCount ?? 0,
                    proposal?.InferredFieldCount ?? 0,
                    proposal?.CommitmentCount ?? 0,
                    proposal?.ConstraintCount ?? 0,
                    proposal?.PendingConfirmationCount ?? 0,
                    proposal?.ResponseContentLength,
                    proposal?.Provider,
                    proposal?.Model,
                    proposal?.RequestId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rows.Add(new SanitizedMissionPlannerRuntimeEvalRow(
                    evalCase.Name,
                    evalCase.Packet.PacketId,
                    evalCase.Packet.Scenario,
                    false,
                    evalCase.ExpectedMissionKind,
                    null,
                    "mission_planner_decode_not_run",
                    "mission_intake_not_run",
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

    private static string ToErrorCode(Exception exception) =>
        exception.GetType().Name.Contains("Provider", StringComparison.Ordinal)
            ? "provider_error"
            : "mission_planner_runtime_error";
}
