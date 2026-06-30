# Live Preflight Eval Notes

Live preflight eval rows are provider-local release artifacts for explaining whether the configured model roles can safely attempt structured live output.

Accepted rows may persist:

- safe eval case name and trusted packet id
- fixed outcome code `live_preflight_accepted`
- role enum
- probe id
- configured model id
- provider kind enum
- allowlisted output kind `structured_output_ready`
- role counts and accepted-role counts
- response content length
- accepted provider/model/request metadata

Rejected rows use fixed identifiers:

- row name `live_preflight_rejected`
- packet id `live_preflight_packet_redacted`

Rejected rows omit roles, response length, provider metadata, raw request body, raw provider response, raw model completion, prompt text, API keys, credentials, approval tokens, hold references, candidate display values, and raw exception text.

Fixed outcomes:

- `live_preflight_accepted`
- `live_preflight_disabled`
- `live_preflight_key_missing`
- `live_preflight_credit_exhausted`
- `live_preflight_timeout`
- `live_preflight_empty_content`
- `live_preflight_malformed_json`
- `live_preflight_schema_unsupported`
- `live_preflight_packet_mismatch`
- `live_preflight_fallback_disabled`
- `live_preflight_provider_unavailable`

Required tests remain deterministic/offline. Optional live smoke records only fixed/sanitized status and must not fall back to a different paid provider.
