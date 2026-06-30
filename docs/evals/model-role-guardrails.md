# Model Role Guardrail Eval Notes

Model role guardrail rows are provider-local release artifacts for the end-user chat UI. They explain the current model posture without invoking live providers by default.

Supported role kinds:

- `DeterministicOffline`
- `SmallModel`
- `StrongModel`
- `LiveProviderDisabled`

Supported provider modes:

- `OfflineDeterministic`
- `HostedSmallModel`
- `HostedStrongModel`
- `LiveProviderDisabled`

Persisted `model_role_status_ready` rows contain only:

- eval case name and trusted packet id
- fixed outcome and error codes
- active role enum
- role kind, provider mode, availability enum, and allowlisted status code
- live-provider and fallback booleans
- provider/model/request metadata and response length from deterministic/offline status source

Blocked rows such as `model_role_live_provider_blocked` and `model_role_fallback_disabled` use fixed row name and packet id placeholders. They may include sanitized role rows so the UI can explain posture, but they omit provider/model/request metadata and response length.

Malformed/error rows use fixed row name and packet id placeholders, zero role rows, fixed outcome/error codes, and no provider metadata. They must not echo raw config text, packet ids, eval labels, provider payloads, credentials, prompts, raw errors, or sentinels.

Fixed outcomes:

- `model_role_status_ready`: deterministic/offline role status is available for UI display.
- `model_role_live_provider_blocked`: live provider roles are blocked or disabled for the default UI path.
- `model_role_fallback_disabled`: fallback to another provider/model is disabled.
- `model_role_malformed_config`: packet or result role config is malformed, duplicated, unknown, or inconsistent.
- `model_role_packet_mismatch`: result packet id does not match the trusted packet.
- `model_role_provider_unavailable`: provider status source failed with a provider error.
- `model_role_status_error`: unexpected status-source error.

Allowed status codes:

- `offline_deterministic`
- `role_available`
- `live_provider_disabled`
- `fallback_disabled`
- `role_status_unspecified`

Required tests are deterministic and offline. Optional OpenRouter, OpenAI, Ollama, or other live checks remain disabled by default and must be guarded by explicit enablement, key presence, provider health/credit checks where applicable, strict timeouts, malformed/empty blocking, sanitized errors, and no paid-provider fallback.
