# Planner Primitive Runner Eval Notes

Planner primitive eval rows are provider-local release artifacts for diagnosing model-produced primitive/form envelopes.

Accepted rows may persist:

- safe eval case name
- trusted run id and turn id
- trusted manifest id and manifest version
- fixed outcome code
- output kind enum
- primitive ids from the manifest
- primitive kinds from the manifest
- primitive count
- task ids from validated task records
- task count
- option count
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
- `planner_model_unsafe_text`
- `planner_model_unsupported_primitive`
- `planner_model_field_path_not_allowed`
- `planner_model_answer_schema_invalid`
- `planner_model_tool_not_allowed`
- `planner_model_primitive_renderer_mismatch`
- `planner_model_task_decomposition_missing`
- `planner_model_provider_unavailable`

Malformed JSON and schema-invalid responses get one bounded repair attempt. If the repair output is valid, the accepted row uses `planner_model_repaired_json`. If the repair also fails, the row uses the fixed malformed/schema outcome and omits provider result metadata.

Before repair, the parser may extract a single balanced JSON object from fenced or prose-wrapped model content in memory. The original completion and wrapper text are never persisted. If no balanced object can be extracted or the extracted object is still invalid JSON, the row remains `planner_model_malformed_json`.

Optional live smoke records only fixed/sanitized status and must not fall back to a different paid provider unless explicitly configured and reported.

Sprint 023 dynamic-form eval rows also enforce prompt-specific output. Accepted rows may preserve only manifest-owned primitive ids and counts; runtime model-authored labels, prompts, help text, default values, option labels/summaries, task titles/summaries, submitted answer values, context summaries, and raw prompts remain runtime-only and are omitted from serialized eval rows.

Sprint 024 HTML primitive eval rows also enforce structural anti-gaming gates before acceptance:

- accepted `composite_form` rows must include at least one non-text interactive primitive such as `select`, `radio_group`, `date`, `date_range`, `slider`, `multi_select`, `choice_card`, or `candidate_deck`;
- accepted `composite_form` rows must include a `task_decomposition` primitive, task refs, and task records with safe ids/titles/state/order;
- destination confirmation, exact dates, and pace controls must not be accepted as generic `text_input`/`textarea` when the prompt/context calls for structured controls;
- `slider` and `number_input` controls must not persist select-style options; if a default is present it must be numeric, otherwise rows block with `planner_model_answer_schema_invalid`;
- row `PrimitiveIds`, `PrimitiveKinds`, `TaskIds`, `PrimitiveCount`, `TaskCount`, `OptionCount`, and `OutputKind` are the persisted proof of accepted structure.

OpenAI/OpenRouter client diagnostics should classify provider failures with fixed classes before they reach eval rows:

- HTTP 429/rate limit: `provider_rate_limited`
- HTTP 4xx: `provider_http_4xx`
- HTTP 5xx: `provider_http_5xx`
- timeout: `provider_timeout`
- empty content: `provider_empty_content`
- malformed JSON: `provider_malformed_json`
- network error: `provider_network_error`

OpenAI credit status is recorded as unknown/not exhausted when the OpenAI client is used as an `IProviderCreditClient`; it must not be reported as exhausted without a real provider signal.
