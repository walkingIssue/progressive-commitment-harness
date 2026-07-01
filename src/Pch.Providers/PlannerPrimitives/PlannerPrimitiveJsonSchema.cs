namespace Pch.Providers.PlannerPrimitives;

internal static class PlannerPrimitiveJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["manifestId", "manifestVersion", "graphRevision", "sessionId", "outputKind", "primitives", "tasks"],
          "properties": {
            "manifestId": { "type": "string" },
            "manifestVersion": { "type": "string" },
            "graphRevision": { "type": "string" },
            "sessionId": { "type": "string" },
            "outputKind": { "type": "string", "enum": ["composite_form", "tool_search_request", "tool_gap_request"] },
            "primitives": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["primitiveId", "primitiveKind", "instanceId", "rendererKey", "fieldPath", "moodToken", "mediaToken", "candidateIds", "taskRefs", "evidenceRefs", "toolContextRefs", "options", "label", "promptText", "helpText", "defaultValue", "rendererHints"],
                "properties": {
                  "primitiveId": { "type": "string", "enum": ["assistant_message", "status_notice", "text_input", "textarea", "number_input", "slider", "date", "date_range", "radio_group", "select", "multi_select", "checkbox", "choice_card", "candidate_deck", "task_decomposition", "timeline_item", "tool_search_request", "tool_gap_request"] },
                  "primitiveKind": { "type": "string", "enum": ["assistant_message", "status_notice", "text_input", "textarea", "number_input", "slider", "date", "date_range", "radio_group", "select", "multi_select", "checkbox", "choice_card", "candidate_deck", "task_decomposition", "timeline_item", "tool_search_request", "tool_gap_request"] },
                  "instanceId": { "type": "string" },
                  "rendererKey": { "type": "string", "enum": ["assistant_message", "status_notice", "text_input", "textarea", "number_input", "slider", "date", "date_range", "radio_group", "select", "multi_select", "checkbox", "choice_card", "candidate_deck", "task_decomposition", "timeline_item", "tool_search_request", "tool_gap_request"] },
                  "fieldPath": { "type": ["string", "null"] },
                  "moodToken": { "type": ["string", "null"] },
                  "mediaToken": { "type": ["string", "null"] },
                  "candidateIds": { "type": "array", "items": { "type": "string" } },
                  "taskRefs": { "type": "array", "items": { "type": "string" } },
                  "evidenceRefs": { "type": "array", "items": { "type": "string" } },
                  "toolContextRefs": { "type": "array", "items": { "type": "string" } },
                  "options": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["optionId", "moodToken", "mediaToken", "toolContextRefs", "label", "summary"],
                      "properties": {
                        "optionId": { "type": "string" },
                        "moodToken": { "type": ["string", "null"] },
                        "mediaToken": { "type": ["string", "null"] },
                        "toolContextRefs": { "type": "array", "items": { "type": "string" } },
                        "label": { "type": "string" },
                        "summary": { "type": "string" }
                      }
                    }
                  },
                  "label": { "type": "string" },
                  "promptText": { "type": "string" },
                  "helpText": { "type": "string" },
                  "defaultValue": { "type": ["string", "null"] },
                  "rendererHints": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["layout", "variant"],
                    "properties": {
                      "layout": { "type": "string" },
                      "variant": { "type": "string" }
                    }
                  }
                }
              }
            },
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["taskId", "primitiveRefs", "title", "summary", "state", "order"],
                "properties": {
                  "taskId": { "type": "string" },
                  "primitiveRefs": { "type": "array", "items": { "type": "string" } },
                  "title": { "type": "string" },
                  "summary": { "type": "string" },
                  "state": { "type": "string", "enum": ["pending", "active", "blocked", "complete"] },
                  "order": { "type": "integer", "minimum": 0, "maximum": 32 }
                }
              }
            }
          }
        }
        """;
}
