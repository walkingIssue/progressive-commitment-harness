namespace Pch.Providers.LiveTurns;

internal static class LiveTurnJsonSchema
{
    public const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["runId", "turnId", "packetId", "sessionId", "role", "outputKind", "missionProposal", "pendingQuestion", "choiceSet", "summaryNotice"],
          "properties": {
            "runId": { "type": "string" },
            "turnId": { "type": "string" },
            "packetId": { "type": "string" },
            "sessionId": { "type": "string" },
            "role": { "type": "string", "enum": ["in_harness_action_generator", "strong_planner"] },
            "outputKind": { "type": "string", "enum": ["mission_proposal", "pending_confirmation_question", "choice_set", "summary_fallback_notice"] },
            "missionProposal": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "required": ["missionKind", "fields", "commitments", "pendingConfirmations"],
              "properties": {
                "missionKind": { "type": "string", "enum": ["vacation", "business", "funeral", "helping_family"] },
                "fields": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["fieldPath", "value", "authoritySource", "evidenceIds"],
                    "properties": {
                      "fieldPath": { "type": "string" },
                      "value": { "type": "string" },
                      "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                      "evidenceIds": { "type": "array", "items": { "type": "string" } }
                    }
                  }
                },
                "commitments": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["commitmentId", "commitmentKind", "title", "startsAt", "endsAt", "location", "isIrreversible", "requiresSpend", "priority", "authoritySource", "evidenceIds"],
                    "properties": {
                      "commitmentId": { "type": "string" },
                      "commitmentKind": { "type": "string", "enum": ["travel", "lodging", "dining", "activity", "family_support", "work"] },
                      "title": { "type": "string" },
                      "startsAt": { "type": ["string", "null"] },
                      "endsAt": { "type": ["string", "null"] },
                      "location": { "type": ["string", "null"] },
                      "isIrreversible": { "type": "boolean" },
                      "requiresSpend": { "type": "boolean" },
                      "priority": { "type": "string", "enum": ["normal", "high", "critical"] },
                      "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                      "evidenceIds": { "type": "array", "items": { "type": "string" } }
                    }
                  }
                },
                "pendingConfirmations": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["confirmationId", "fieldPath", "reasonCode", "authoritySource", "evidenceIds"],
                    "properties": {
                      "confirmationId": { "type": "string" },
                      "fieldPath": { "type": "string" },
                      "reasonCode": { "type": "string", "enum": ["needs_user_confirmation", "needs_date_confirmation", "needs_budget_confirmation", "needs_location_confirmation"] },
                      "authoritySource": { "type": "string", "enum": ["user_stated", "model_inference_pending_confirmation", "trusted_provider"] },
                      "evidenceIds": { "type": "array", "items": { "type": "string" } }
                    }
                  }
                }
              }
            },
            "pendingQuestion": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "required": ["questionId", "fieldPath", "reasonCode", "promptText"],
              "properties": {
                "questionId": { "type": "string" },
                "fieldPath": { "type": "string" },
                "reasonCode": { "type": "string", "enum": ["needs_user_confirmation", "needs_date_confirmation", "needs_budget_confirmation", "needs_location_confirmation"] },
                "promptText": { "type": "string" }
              }
            },
            "choiceSet": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "required": ["choiceSetId", "options", "uiMood", "framingText"],
              "properties": {
                "choiceSetId": { "type": "string" },
                "options": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["candidateId", "slotId", "category", "label", "rationale"],
                    "properties": {
                      "candidateId": { "type": "string" },
                      "slotId": { "type": "string" },
                      "category": { "type": "string", "enum": ["dining", "activity", "transit", "downtime", "lodging"] },
                      "label": { "type": "string" },
                      "rationale": { "type": "string" }
                    }
                  }
                },
                "uiMood": { "type": "string", "enum": ["unspecified", "calm_morning", "lively_food", "reflective_culture", "soft_nature", "restorative_downtime", "logistics"] },
                "framingText": { "type": "string" }
              }
            },
            "summaryNotice": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "required": ["noticeKind", "summaryText"],
              "properties": {
                "noticeKind": { "type": "string", "enum": ["summary", "fallback", "provider_blocked"] },
                "summaryText": { "type": "string" }
              }
            }
          }
        }
        """;
}
