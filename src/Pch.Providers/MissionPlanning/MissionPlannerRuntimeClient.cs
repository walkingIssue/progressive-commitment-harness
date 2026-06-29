namespace Pch.Providers.MissionPlanning;

public sealed class MissionPlannerRuntimeClient
{
    private readonly IMissionPlannerClient _planner;
    private readonly MissionPlannerRuntimeBridge _bridge;

    public MissionPlannerRuntimeClient(IMissionPlannerClient planner, MissionPlannerRuntimeBridge? bridge = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _bridge = bridge ?? new MissionPlannerRuntimeBridge();
    }

    public async Task<MissionPlannerRuntimeHandoffResult> RunAsync(
        MissionPlannerPacket packet,
        MissionPlannerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var result = await _planner.PlanAsync(packet, options, cancellationToken).ConfigureAwait(false);
        return _bridge.Bridge(packet, result);
    }
}
