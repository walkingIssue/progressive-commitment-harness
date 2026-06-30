using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pch.Core;

namespace Pch.Harness;

public sealed class PlanningEditImpactAnalyzer
{
    public const string AcceptedCode = "edit_impact_accepted";
    public const string StaleSnapshotCode = "stale_snapshot";
    public const string UnknownNodeCode = "unknown_node";
    public const string UnsupportedEditCode = "unsupported_edit";
    public const string NoImpactCode = "no_impact";
    public const string RepairRequiredCode = "repair_required";

    private const int MaxNodes = 96;
    private const int MaxRefs = 24;
    private const int MaxEvidenceIds = 8;
    private const int MaxTextLength = 120;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] UnsafeFragments =
    [
        "RAW_",
        "PROVIDER_PAYLOAD",
        "RAW_PROMPT",
        "APPROVAL_TOKEN",
        "HOLD_REFERENCE",
        "PAYMENT",
        "BOOKING_REF",
        "CANDIDATE_DISPLAY",
        "SECRET",
        "CREDENTIAL",
        "PASSWORD",
        "API_KEY"
    ];

    public PlanningDependencySnapshot BuildSnapshot(
        TripSession session,
        IReadOnlyList<AvailabilityQuotePreviewResult>? availabilityPreviews = null)
    {
        var nodes = new List<PlanningNode>();
        AddMissionNodes(session, nodes);
        AddItineraryNodes(session, nodes);
        AddDecisionNodes(session, nodes);
        AddAvailabilityNodes(session, availabilityPreviews ?? [], nodes);
        AddMockHoldNode(session, availabilityPreviews ?? [], nodes);

        var bounded = nodes
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .Take(MaxNodes)
            .ToArray();

        return new PlanningDependencySnapshot(
            Fingerprint: Fingerprint(bounded),
            SessionId: SafeId(session.SessionId),
            Nodes: bounded);
    }

    public EditImpactResult Analyze(
        TripSession session,
        EditImpactRequest request,
        IReadOnlyList<AvailabilityQuotePreviewResult>? availabilityPreviews = null)
    {
        var snapshot = BuildSnapshot(session, availabilityPreviews);
        if (request is null
            || string.IsNullOrWhiteSpace(request.SessionId)
            || !string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Blocked(UnsupportedEditCode, "Edit impact request failed validation.", snapshot, [], []);
        }

        if (!string.Equals(request.ObservedFingerprint, snapshot.Fingerprint, StringComparison.Ordinal))
        {
            return Blocked(
                StaleSnapshotCode,
                "Edit impact request references stale planning context.",
                snapshot,
                [],
                snapshot.Nodes.Take(MaxRefs).Select(ToRef).ToArray());
        }

        if (!Supported(request.EditKind))
        {
            return Blocked(UnsupportedEditCode, "Edit impact request uses an unsupported edit kind.", snapshot, [], []);
        }

        var editedNodeId = SafeId(request.EditedNodeId);
        var edited = snapshot.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, editedNodeId, StringComparison.Ordinal));
        if (edited is null)
        {
            return Blocked(UnknownNodeCode, "Edit impact request references an unknown planning node.", snapshot, [], []);
        }

        if (!EditKindMatchesNode(request.EditKind, edited.Kind))
        {
            return Blocked(UnsupportedEditCode, "Edit impact request uses an unsupported edit kind.", snapshot, [], []);
        }

        var affected = AffectedNodes(snapshot.Nodes, edited.NodeId).ToArray();
        var preserved = snapshot.Nodes
            .Where(node => affected.All(affectedNode => !string.Equals(affectedNode.NodeId, node.NodeId, StringComparison.Ordinal)))
            .Take(MaxRefs)
            .Select(ToRef)
            .ToArray();

        if (affected.Length <= 1)
        {
            return new EditImpactResult(
                IsAccepted: true,
                IsBlocked: false,
                Code: NoImpactCode,
                Summary: "Edit impact analysis found no downstream impact.",
                Fingerprint: snapshot.Fingerprint,
                AffectedNodes: affected.Select(ToRef).ToArray(),
                PreservedNodes: preserved,
                StaleContext: [],
                MinimalRepairPrompts: [],
                RequiresUserConfirmation: false,
                RequiresModelRepair: false);
        }

        var affectedRefs = affected.Take(MaxRefs).Select(ToRef).ToArray();
        return new EditImpactResult(
            IsAccepted: true,
            IsBlocked: false,
            Code: RepairRequiredCode,
            Summary: "Edit impact analysis found downstream nodes that need repair.",
            Fingerprint: snapshot.Fingerprint,
            AffectedNodes: affectedRefs,
            PreservedNodes: preserved,
            StaleContext: [],
            MinimalRepairPrompts: RepairPrompts(request.EditKind, affectedRefs),
            RequiresUserConfirmation: true,
            RequiresModelRepair: RequiresModelRepair(affected));
    }

    private static void AddMissionNodes(TripSession session, List<PlanningNode> nodes)
    {
        nodes.Add(Node("mission:purpose", "mission_fact", "accepted", [], [], "mission_purpose"));
        nodes.Add(Node("mission:destination_country", "mission_fact", "accepted", [], [], "mission_destination"));
        nodes.Add(Node("mission:date_window", "mission_fact", "accepted", [], [], "mission_date_window"));
    }

    private static void AddItineraryNodes(TripSession session, List<PlanningNode> nodes)
    {
        if (session.LastItineraryCompilation is not { IsCompiled: true } compilation)
        {
            return;
        }

        foreach (var day in compilation.Days)
        {
            var dayId = $"day:{day.DayId}";
            nodes.Add(Node(dayId, "day", "compiled", ["mission:date_window"], [], "itinerary_day"));
            foreach (var slot in day.Slots)
            {
                nodes.Add(Node(
                    $"slot:{slot.SlotId}",
                    "slot",
                    "compiled",
                    [dayId, "mission:date_window"],
                    slot.PendingFieldPath is null ? [] : [SafeId(slot.PendingFieldPath)],
                    $"slot_{SafeId(slot.Kind.ToString()).ToLowerInvariant()}"));
            }
        }
    }

    private static void AddDecisionNodes(TripSession session, List<PlanningNode> nodes)
    {
        foreach (var decision in session.ItineraryDecisions)
        {
            var slotId = $"slot:{SafeId(decision.SlotId)}";
            var dependsOn = new List<string> { slotId };
            if (!string.IsNullOrWhiteSpace(decision.CandidateId))
            {
                var candidateId = $"candidate:{SafeId(decision.CandidateId)}";
                nodes.Add(Node(
                    candidateId,
                    "candidate",
                    "trusted",
                    [slotId],
                    decision.EvidenceIds,
                    decision.CandidateKind?.ToString().ToLowerInvariant() ?? "candidate"));
                dependsOn.Add(candidateId);
            }

            nodes.Add(Node(
                $"decision:{SafeId(decision.DecisionId)}",
                decision.Kind is ItinerarySlotDecisionKind.Selected ? "selected_decision" : "deferred_decision",
                decision.Kind.ToString().ToLowerInvariant(),
                dependsOn,
                decision.EvidenceIds,
                decision.Kind is ItinerarySlotDecisionKind.Selected ? "selected_itinerary_candidate" : "deferred_itinerary_slot"));
        }
    }

    private static void AddAvailabilityNodes(
        TripSession session,
        IReadOnlyList<AvailabilityQuotePreviewResult> previews,
        List<PlanningNode> nodes)
    {
        foreach (var preview in previews.Where(preview => preview?.Preview is not null).Take(MaxRefs))
        {
            var item = preview.Preview!;
            var matchingDecision = session.ItineraryDecisions.FirstOrDefault(decision =>
                string.Equals(decision.SlotId, item.SlotId, StringComparison.Ordinal)
                && string.Equals(decision.CandidateId, item.CandidateId, StringComparison.Ordinal));
            var dependencies = new List<string>
            {
                $"slot:{SafeId(item.SlotId)}",
                $"candidate:{SafeId(item.CandidateId)}"
            };
            if (matchingDecision is not null)
            {
                dependencies.Add($"decision:{SafeId(matchingDecision.DecisionId)}");
            }

            nodes.Add(Node(
                $"availability:{SafeId(item.PreviewId)}",
                "availability_preview",
                SafeId(item.Status),
                dependencies,
                preview.EvidenceReferences,
                item.RequiresApproval ? "approval_required_preview" : "availability_preview"));
        }
    }

    private static void AddMockHoldNode(
        TripSession session,
        IReadOnlyList<AvailabilityQuotePreviewResult> previews,
        List<PlanningNode> nodes)
    {
        var dependencies = session.ItineraryDecisions
            .Where(decision => decision.Kind is ItinerarySlotDecisionKind.Selected)
            .Select(decision => $"decision:{SafeId(decision.DecisionId)}")
            .Concat(previews.Where(preview => preview?.Preview is not null)
                .Select(preview => $"availability:{SafeId(preview.Preview!.PreviewId)}"))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceIds)
            .ToArray();
        var status = session.ApprovalTokens.Any(token => !string.IsNullOrWhiteSpace(token.Token))
            ? "ready"
            : "approval_required";

        nodes.Add(Node(
            "mock_hold:readiness",
            "mock_hold_readiness",
            status,
            dependencies,
            [],
            status == "ready" ? "mock_hold_ready" : "mock_hold_approval_required"));
    }

    private static IReadOnlyList<PlanningNode> AffectedNodes(IReadOnlyList<PlanningNode> nodes, string editedNodeId)
    {
        var byDependency = nodes
            .SelectMany(node => node.DependsOnNodeIds.Select(dependency => (dependency, node)))
            .GroupBy(pair => pair.dependency, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.node).ToArray(), StringComparer.Ordinal);
        var affected = new Dictionary<string, PlanningNode>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(editedNodeId);

        while (queue.Count > 0 && affected.Count < MaxRefs)
        {
            var nodeId = queue.Dequeue();
            var node = nodes.FirstOrDefault(item => string.Equals(item.NodeId, nodeId, StringComparison.Ordinal));
            if (node is null || !affected.TryAdd(node.NodeId, node))
            {
                continue;
            }

            if (byDependency.TryGetValue(node.NodeId, out var dependents))
            {
                foreach (var dependent in dependents.OrderBy(item => item.NodeId, StringComparer.Ordinal))
                {
                    queue.Enqueue(dependent.NodeId);
                }
            }
        }

        return affected.Values.OrderBy(node => node.NodeId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<RepairPrompt> RepairPrompts(PlanningEditKind editKind, IReadOnlyList<PlanningNodeRef> affected)
    {
        var promptCode = editKind switch
        {
            PlanningEditKind.SelectedCandidate => "repair_selected_candidate",
            PlanningEditKind.Day => "repair_day_plan",
            PlanningEditKind.Slot => "repair_slot_plan",
            PlanningEditKind.MissionFact => "repair_mission_context",
            _ => "repair_planning_context"
        };

        return
        [
            new(
                PromptId: $"prompt-{promptCode}",
                Code: promptCode,
                Summary: "Confirm whether affected planning items should be repaired.",
                AffectedNodeIds: affected.Select(node => node.NodeId).Take(MaxEvidenceIds).ToArray())
        ];
    }

    private static bool RequiresModelRepair(IReadOnlyList<PlanningNode> affected)
    {
        return affected.Any(node => node.Kind is "day" or "slot" or "availability_preview");
    }

    private static bool Supported(PlanningEditKind kind)
    {
        return kind is PlanningEditKind.SelectedCandidate or PlanningEditKind.Day or PlanningEditKind.Slot or PlanningEditKind.MissionFact;
    }

    private static bool EditKindMatchesNode(PlanningEditKind kind, string nodeKind)
    {
        return kind switch
        {
            PlanningEditKind.SelectedCandidate => nodeKind is "selected_decision" or "candidate",
            PlanningEditKind.Day => nodeKind is "day",
            PlanningEditKind.Slot => nodeKind is "slot",
            PlanningEditKind.MissionFact => nodeKind is "mission_fact",
            _ => false
        };
    }

    private static EditImpactResult Blocked(
        string code,
        string summary,
        PlanningDependencySnapshot snapshot,
        IReadOnlyList<PlanningNodeRef> affected,
        IReadOnlyList<PlanningNodeRef> staleContext)
    {
        return new(
            IsAccepted: false,
            IsBlocked: true,
            Code: code,
            Summary: summary,
            Fingerprint: snapshot.Fingerprint,
            AffectedNodes: affected,
            PreservedNodes: [],
            StaleContext: staleContext,
            MinimalRepairPrompts: [],
            RequiresUserConfirmation: false,
            RequiresModelRepair: false);
    }

    private static PlanningNode Node(
        string nodeId,
        string kind,
        string status,
        IReadOnlyList<string> dependsOn,
        IReadOnlyList<string> evidenceIds,
        string labelCode)
    {
        return new(
            SafeId(nodeId),
            SafeId(kind),
            SafeId(status),
            dependsOn.Select(SafeId).Distinct(StringComparer.Ordinal).Take(MaxEvidenceIds).ToArray(),
            CleanIds(evidenceIds),
            SafeId(labelCode));
    }

    private static PlanningNodeRef ToRef(PlanningNode node)
    {
        return new(
            node.NodeId,
            node.Kind,
            node.Status,
            node.LabelCode,
            node.EvidenceIds);
    }

    private static string Fingerprint(IReadOnlyList<PlanningNode> nodes)
    {
        var json = JsonSerializer.Serialize(nodes, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> CleanIds(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(SafeId)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceIds)
            .ToArray()
            ?? [];
    }

    private static string SafeId(string? value)
    {
        var text = SafeText(value);
        if (text == "redacted")
        {
            return text;
        }

        var normalized = new string(text
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':' ? character : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "redacted" : normalized;
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "redacted";
        }

        if (UnsafeFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "redacted";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}

public sealed record EditImpactRequest(
    string SessionId,
    string ObservedFingerprint,
    string EditedNodeId,
    PlanningEditKind EditKind,
    string ProposedChangeCode);

public sealed record EditImpactResult(
    bool IsAccepted,
    bool IsBlocked,
    string Code,
    string Summary,
    string Fingerprint,
    IReadOnlyList<PlanningNodeRef> AffectedNodes,
    IReadOnlyList<PlanningNodeRef> PreservedNodes,
    IReadOnlyList<PlanningNodeRef> StaleContext,
    IReadOnlyList<RepairPrompt> MinimalRepairPrompts,
    bool RequiresUserConfirmation,
    bool RequiresModelRepair);

public sealed record PlanningDependencySnapshot(
    string Fingerprint,
    string SessionId,
    IReadOnlyList<PlanningNode> Nodes);

public sealed record PlanningNode(
    string NodeId,
    string Kind,
    string Status,
    IReadOnlyList<string> DependsOnNodeIds,
    IReadOnlyList<string> EvidenceIds,
    string LabelCode);

public sealed record PlanningNodeRef(
    string NodeId,
    string Kind,
    string Status,
    string LabelCode,
    IReadOnlyList<string> EvidenceIds);

public sealed record RepairPrompt(
    string PromptId,
    string Code,
    string Summary,
    IReadOnlyList<string> AffectedNodeIds);

[JsonConverter(typeof(JsonStringEnumConverter<PlanningEditKind>))]
public enum PlanningEditKind
{
    SelectedCandidate,
    Day,
    Slot,
    MissionFact,
    Unsupported
}
