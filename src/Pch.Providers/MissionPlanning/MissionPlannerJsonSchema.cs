namespace Pch.Providers.MissionPlanning;

public static class MissionPlannerJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["packet_id", "mission_kind", "fields", "commitments", "constraints", "pending_confirmations", "memory_digest"],
          "properties": {
            "packet_id": { "type": "string" },
            "mission_kind": {
              "type": "string",
              "enum": ["vacation", "business", "funeral_downtime", "helping_family", "family_support", "general"]
            },
            "fields": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["field_path", "value", "authority_source", "evidence_ids", "requires_confirmation"],
                "properties": {
                  "field_path": { "type": "string" },
                  "value": { "type": "string" },
                  "authority_source": { "type": "string", "enum": ["user_stated", "model_inferred"] },
                  "evidence_ids": { "type": "array", "items": { "type": "string" } },
                  "requires_confirmation": { "type": "boolean" }
                }
              }
            },
            "commitments": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["commitment_id", "commitment_kind", "title", "starts_at", "ends_at", "location", "is_irreversible", "requires_spend", "commitment_priority", "authority_source", "evidence_ids"],
                "properties": {
                  "commitment_id": { "type": "string" },
                  "commitment_kind": { "type": "string" },
                  "title": { "type": "string" },
                  "starts_at": { "type": ["string", "null"], "format": "date-time" },
                  "ends_at": { "type": ["string", "null"], "format": "date-time" },
                  "location": { "type": ["string", "null"] },
                  "is_irreversible": { "type": "boolean" },
                  "requires_spend": { "type": "boolean" },
                  "commitment_priority": { "type": "string", "enum": ["normal", "high", "critical"] },
                  "authority_source": { "type": "string", "enum": ["user_stated", "model_inferred"] },
                  "evidence_ids": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "constraints": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["constraint_id", "label", "value", "authority_source", "is_hard", "evidence_ids"],
                "properties": {
                  "constraint_id": { "type": "string" },
                  "label": { "type": "string" },
                  "value": { "type": "string" },
                  "authority_source": { "type": "string", "enum": ["user_stated", "model_inferred"] },
                  "is_hard": { "type": "boolean" },
                  "evidence_ids": { "type": "array", "items": { "type": "string" } }
                }
              }
            },
            "pending_confirmations": { "type": "array", "items": { "type": "string" } },
            "memory_digest": { "type": "string" }
          }
        }
        """;
}
