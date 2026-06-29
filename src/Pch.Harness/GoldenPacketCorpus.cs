using System.Text.Json;
using Pch.Core;

namespace Pch.Harness;

public sealed class GoldenPacketCorpus
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ProjectionService _projectionService;

    public GoldenPacketCorpus(ProjectionService? projectionService = null)
    {
        _projectionService = projectionService ?? new ProjectionService();
    }

    public IReadOnlyDictionary<string, StagePacket> Create()
    {
        return new SortedDictionary<string, StagePacket>(StringComparer.Ordinal)
        {
            ["approval_request_packet"] = _projectionService.Project(SyntheticTripFactory.CreateSession(7), HarnessStage.ApprovalQueue),
            ["business_trip_packet"] = _projectionService.Project(SyntheticTripFactory.CreateBusinessTripSession(), HarnessStage.Logistics),
            ["choice_collapse_packet"] = _projectionService.Project(SyntheticTripFactory.CreateSession(14), HarnessStage.Logistics),
            ["conflict_review_packet"] = _projectionService.Project(SyntheticTripFactory.CreateSession(7), HarnessStage.ConflictVerify),
            ["funeral_downtime_packet"] = _projectionService.Project(SyntheticTripFactory.CreateFuneralDowntimeSession(), HarnessStage.ActivitiesDowntime),
            ["slot_collection_packet"] = _projectionService.Project(SyntheticTripFactory.CreateSession(1), HarnessStage.SlotCollection)
        };
    }

    public string Serialize(StagePacket packet)
    {
        return JsonSerializer.Serialize(packet, Options);
    }
}
