namespace Pch.Providers.MissionPlanning;

public interface IMissionPlannerClient
{
    Task<MissionPlannerResult> PlanAsync(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default);
}
