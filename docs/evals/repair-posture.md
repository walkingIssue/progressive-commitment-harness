# Repair Posture Eval Notes

Repair posture eval rows are provider-local release artifacts for model-assisted edit repair suggestions. They are suggestion/evaluation only; they do not apply harness edits.

Accepted rows may persist:

- safe eval case name and trusted packet id
- trusted node ids and node kinds from the packet
- fixed repair mode enums
- fixed repair reason-code enums
- node, suggestion, and affected-node counts
- response content length
- accepted provider/model/request metadata

Rejected rows use fixed identifiers:

- row name `repair_posture_rejected`
- packet id `repair_posture_packet_redacted`

Rejected/error rows omit provider/model/request metadata, response length, suggestions, raw prompt text, provider payloads, candidate display text, approval tokens, hold references, credentials, API keys, raw completions, and exception text.

Fixed outcomes:

- `repair_posture_accepted`: result packet id matched, suggestions referenced trusted packet nodes, and all modes/reason codes were allowlisted.
- `repair_posture_packet_mismatch`: result packet id did not exactly match the trusted packet.
- `repair_posture_node_mismatch`: result suggestions referenced missing, duplicated, blank, or non-packet node ids.
- `repair_posture_malformed_packet`: eval packet or node metadata was malformed.
- `repair_posture_malformed_result`: result shape, empty content, or JSON schema was malformed.
- `repair_posture_unsupported_mode`: mode or reason code was outside the allowlist.
- `repair_posture_live_disabled`: live runner was not explicitly enabled.
- `repair_posture_key_missing`: live runner was enabled without key availability.
- `repair_posture_credit_exhausted`: credit guard blocked the provider.
- `repair_posture_timeout`: provider call timed out.
- `repair_posture_provider_unavailable`: provider failed without a more specific code.
- `repair_posture_error`: unexpected source error.

Required tests are deterministic/offline. Optional live OpenRouter/OpenAI/Grok-compatible checks remain guarded by explicit configuration, key presence, credit/health checks where available, strict timeout, schema validation, and no paid-provider fallback.
