# Sprint 018 Provider Repair Posture Live Observation

Status: `blocked_by_guard`

This provider lane added a guarded live runner shape for repair posture suggestions, but required verification remained deterministic/offline. No live network call, API key read, or provider credit spend was performed for this report.

## Provider Roles

- OpenRouter/Qwen 14B: `blocked_by_guard`
- OpenAI-compatible repair suggestion runner: `not_configured`
- Grok/xAI-compatible repair suggestion runner: `not_configured`

## Sanitized Outcome Categories

- OpenRouter path is represented by `ModelCompletionRepairPostureSource` over `IModelCompletionClient` plus optional `IProviderCreditClient`.
- Live execution remains blocked unless explicit live enablement and key availability are provided.
- Missing live enablement maps to `repair_posture_live_disabled`.
- Missing key availability maps to `repair_posture_key_missing`.
- Credit exhaustion maps to `repair_posture_credit_exhausted`.
- Empty or malformed model output maps to `repair_posture_malformed_result`.
- Timeout maps to `repair_posture_timeout`.
- Packet/node/mode validation failures map to fixed packet, node, or unsupported-mode codes.

## What Worked

- Deterministic mock posture can explain keep, replan-day, reselect-candidate, ask-user, and blocked-review modes from sanitized node metadata.
- Accepted rows persist only trusted node ids/kinds, fixed enums, counts, response length, and accepted provider/model/request metadata.
- Rejected rows use fixed identifiers and omit provider metadata.

## Findings

- No live provider output was attached, so this lane did not discover a live harness/UI failure point.
- The live runner shape is ready for an explicit smoke once keys, provider health, timeout, and OpenRouter credit guard are configured.
- Any live finding should be captured as fixed outcome codes and sanitized observations, not raw completions or prompts.

## Follow-Up Tickets

- Wire the provider-local repair posture source to Shellby's harness edit-impact packet after the harness contract is published.
- Add an explicit manual smoke command or small tool wrapper that checks OpenRouter credits before attempting one repair-posture suggestion.
- Add UI copy for deterministic/live repair posture state once Sarah's timeline edit affordance is ready.
