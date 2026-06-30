# Provider Model Role Guardrails

Sprint 015 adds provider-local model role guardrail DTOs, a deterministic status evaluator, and mock status source behavior for the end-user chat UI.

The UI can label model posture from enum values only:

- `DeterministicOffline` is the default first-screen role for required paths.
- `SmallModel` and `StrongModel` are modeled as explicit roles, but required tests do not call hosted providers.
- `LiveProviderDisabled` makes the disabled live-provider posture visible without needing keys or network.

The evaluator validates packet-owned role config before invoking the source. Duplicate roles, unknown role or mode enums, missing roles, missing preferred role, too many defaults, null packets, and null role collections all produce fixed `model_role_malformed_config` rows.

Result rows are accepted only when source roles exactly match the trusted packet role set and modes. Raw provider status codes are mapped to the allowlisted `role_status_unspecified` value rather than persisted.

Fallback behavior is explicit:

- `model_role_fallback_disabled` means no silent fallback to another provider/model should occur.
- Live provider checks are disabled by default.
- Required tests use deterministic mocks only and do not read API keys, consume provider credits, or make network calls.

Future live provider status checks must be opt-in and guarded by key presence, provider health, credit checks when relevant, strict timeout handling, malformed/empty output blocking, fixed sanitized errors, and no paid-provider fallback.
