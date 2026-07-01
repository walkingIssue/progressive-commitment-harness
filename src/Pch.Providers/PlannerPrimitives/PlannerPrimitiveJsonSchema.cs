namespace Pch.Providers.PlannerPrimitives;

internal static class PlannerPrimitiveJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["manifestId", "manifestVersion", "graphRevision", "sessionId", "outputKind", "primitives"],
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
                "required": ["primitiveId", "primitiveKind", "instanceId", "rendererKey", "fieldPath", "moodToken", "candidateIds", "label", "promptText"],
                "properties": {
                  "primitiveId": { "type": "string" },
                  "primitiveKind": { "type": "string" },
                  "instanceId": { "type": "string" },
                  "rendererKey": { "type": "string" },
                  "fieldPath": { "type": ["string", "null"] },
                  "moodToken": { "type": ["string", "null"] },
                  "candidateIds": { "type": "array", "items": { "type": "string" } },
                  "label": { "type": "string" },
                  "promptText": { "type": "string" }
                }
              }
            }
          }
        }
        """;
}
