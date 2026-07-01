namespace Pch.Providers.PlannerPrimitives;

public static class PlannerPrimitiveToolCatalog
{
    public static readonly IReadOnlyList<string> RequiredPrimitiveIds =
    [
        "assistant_message",
        "status_notice",
        "text_input",
        "textarea",
        "number_input",
        "slider",
        "date",
        "date_range",
        "radio_group",
        "select",
        "multi_select",
        "checkbox",
        "choice_card",
        "candidate_deck",
        "task_decomposition",
        "timeline_item",
        "tool_search_request",
        "tool_gap_request"
    ];

    public static readonly IReadOnlyList<string> NonTextInteractivePrimitiveIds =
    [
        "number_input",
        "slider",
        "date",
        "date_range",
        "radio_group",
        "select",
        "multi_select",
        "checkbox",
        "choice_card",
        "candidate_deck"
    ];

    public static IReadOnlyList<PlannerPrimitiveDefinition> CreateRequiredDefinitions() =>
        RequiredPrimitiveIds
            .Select(id => new PlannerPrimitiveDefinition(id, id, id))
            .ToArray();
}
