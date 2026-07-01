# Planner Primitive Runner Eval Notes

Planner primitive eval rows are provider-local release artifacts for diagnosing model-produced primitive/form envelopes.

Accepted rows may persist:

- safe eval case name
- trusted run id and turn id
- trusted manifest id and manifest version
- fixed outcome code
- output kind enum
- primitive ids from the manifest
- primitive count
- repair flag
- duration milliseconds and coarse duration bucket
- response content length
- accepted provider/model/request metadata

Rejected rows use fixed identifiers:

- row name `planner_model_rejected`
- run id `planner_model_run_redacted`
- turn id `planner_model_turn_redacted`
- manifest id `planner_model_manifest_redacted`
- manifest version `manifest_version_redacted`

Rejected rows omit output kind, primitive ids, response length, provider metadata, request id, duration, raw request body, raw provider response, raw completion, raw prompt text, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, model-authored labels, model-authored prompt text, and raw exception text.

Fixed outcomes:

- `planner_model_accepted`
- `planner_model_repaired_json`
- `planner_model_tool_search_requested`
- `planner_model_tool_gap_requested`
- `planner_model_key_missing`
- `planner_model_credit_exhausted`
- `planner_model_rate_limited`
- `planner_model_timeout`
- `planner_model_empty_content`
- `planner_model_malformed_json`
- `planner_model_schema_invalid`
- `planner_model_unsupported_primitive`
- `planner_model_provider_unavailable`

Malformed JSON and schema-invalid responses get one bounded repair attempt. If the repair output is valid, the accepted row uses `planner_model_repaired_json`. If the repair also fails, the row uses the fixed malformed/schema outcome and omits provider result metadata.

Optional live smoke records only fixed/sanitized status and must not fall back to a different paid provider unless explicitly configured and reported.

OpenAI/OpenRouter client diagnostics should classify provider failures with fixed classes before they reach eval rows:

- HTTP 429/rate limit: `provider_rate_limited`
- HTTP 4xx: `provider_http_4xx`
- HTTP 5xx: `provider_http_5xx`
- timeout: `provider_timeout`
- empty content: `provider_empty_content`
- malformed JSON: `provider_malformed_json`
- network error: `provider_network_error`

OpenAI credit status is recorded as unknown/not exhausted when the OpenAI client is used as an `IProviderCreditClient`; it must not be reported as exhausted without a real provider signal.
