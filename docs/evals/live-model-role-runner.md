# Live Model Role Runner Eval Notes

Live model role runner rows are provider-local release artifacts for explaining whether the UI-facing model role path is offline, blocked, or backed by a guarded live model call.

Accepted rows may persist:

- safe eval case name and trusted packet id
- fixed outcome code `live_model_output_accepted`
- provider-local role enum and configured model id
- allowlisted output kind from the trusted packet
- allowlisted `uiMood` enum, or `Unspecified` for unknown provider text
- response content length
- provider/model/request metadata returned by the completion client

`LiveModelRunResult.Arguments` is an in-memory runtime payload only and is JSON-ignored. Eval rows do not include argument JSON.

Rejected rows use fixed identifiers:

- row name `live_model_run_rejected`
- packet id `live_model_packet_redacted`

Rejected rows may include the role and configured model id when those come from trusted configuration, but they omit output kind, mood, response length, provider metadata, model metadata, request id, raw arguments, raw completion content, prompts, credentials, context digests, and exception text.

Fixed outcomes:

- `live_model_output_accepted`: structured output passed packet id and output kind validation.
- `live_model_disabled`: live mode is disabled.
- `live_model_key_missing`: a live provider key is not available.
- `live_model_credit_exhausted`: credit guard reported exhausted credits or no safe credit client was available.
- `live_model_fallback_disabled`: the packet requires fallback but fallback policy is disabled.
- `live_model_timeout`: provider call timed out.
- `live_model_empty_content`: provider returned no content.
- `live_model_malformed_schema`: packet or provider JSON schema was malformed.
- `live_model_packet_mismatch`: provider packet id did not exactly match the trusted packet.
- `live_model_unsupported_output`: provider output kind was outside the packet allowlist.
- `live_model_provider_unavailable`: provider failed without a more specific sanitized outcome.

Required eval tests are deterministic and offline. Optional OpenRouter/OpenAI/Ollama checks remain blocked or skipped by default and must be key, health, credit, and timeout guarded with no paid-provider fallback.
