# Live Turn Runner Eval Notes

Live turn log rows are provider-local release artifacts for diagnosing real model turns in the Sprint 021 multi-turn harness loop.

Accepted rows may persist:

- safe eval case name
- trusted run id, turn id, and packet id
- fixed outcome code `live_turn_accepted`
- null failure class code
- role enum
- output kind enum
- trusted candidate ids and candidate categories for accepted choice sets
- candidate count
- response content length
- duration milliseconds and coarse duration bucket
- accepted provider/model/request metadata

Rejected rows use fixed identifiers:

- row name `live_turn_rejected`
- run id `live_turn_run_redacted`
- turn id `live_turn_turn_redacted`
- packet id `live_turn_packet_redacted`

Rejected rows persist only fixed outcome and fixed failure class code. They omit output kind, role, candidate ids/categories, response length, provider metadata, request id, duration, raw request body, raw provider response, raw completion, prompt text, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, field values, commitment titles, locations, evidence ids, and raw exception text.

Fixed provider failure outcomes include:

- `live_turn_provider_http_4xx`
- `live_turn_provider_http_5xx`
- `live_turn_provider_rate_limited`
- `live_turn_provider_timeout`
- `live_turn_provider_empty_content`
- `live_turn_provider_malformed_json`
- `live_turn_provider_schema_invalid`
- `live_turn_provider_upstream_model_unavailable`
- `live_turn_provider_network_error`
- `live_turn_provider_unknown_error`

Fixed guard and validation outcomes include:

- `live_turn_disabled`
- `live_turn_key_missing`
- `live_turn_credit_exhausted`
- `live_turn_fallback_disabled`
- `live_turn_packet_mismatch`
- `live_turn_unsupported_value`
- `live_turn_unsafe_value_redacted`

Choice-set rows derive persisted candidate ids/categories from the trusted packet map after exact candidate id, slot id, and category validation. Unknown, duplicate, category-mismatched, or sentinel-bearing candidate outputs are rejected without provider metadata.

Optional live smoke records only fixed/sanitized status and must not fall back to a different paid provider.
