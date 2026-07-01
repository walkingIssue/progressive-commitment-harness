# Planner Primitive Runner

Sprint 022 adds a provider-local planner primitive/form runner. It consumes a provider-local mirror of the harness `PlannerToolManifest` shape and asks a live model to emit only primitive/form invocations that the harness can validate before UI render.

This lane remains dependency-light:

- no provider-to-harness reference;
- no UI reference;
- no browser-side model calls;
- required tests stay deterministic/offline.

Provider-local shapes:

- `PlannerToolManifestMirror` carries manifest id/version, graph revision, session id, stage, allowed primitive definitions, allowed field paths, mood tokens, and max primitive count.
- `PlannerModelRequest` carries run/turn ids, manifest mirror, locale, optional prompt digest, runtime-only raw prompt text marked `JsonIgnore`, runtime submitted answer values, and provider-local context/tool refs.
- `PlannerContextToolResult` is the provider-neutral context mirror for future web/search/booking sources. Sprint 023 uses only explicitly named `mock_context_provider` context and must not present it as live web/search.
- `PlannerModelResult` carries output kind, primitive invocations, task invocations, repair status, prompt-specific status, response length, duration, and accepted provider/model/request metadata.
- `SanitizedPlannerModelLogRow` carries only fixed outcomes, safe ids, manifest version, primitive ids/kinds/counts, task ids/count, option count, repair flag, timing, response length, and accepted provider metadata.

Sprint 024 primitive menu:

- `assistant_message`
- `status_notice`
- `text_input`
- `textarea`
- `number_input`
- `slider`
- `date`
- `date_range`
- `radio_group`
- `select`
- `multi_select`
- `checkbox`
- `choice_card`
- `candidate_deck`
- `task_decomposition`
- `timeline_item`
- `tool_search_request`
- `tool_gap_request`

Supported output kinds:

- `composite_form`
- `tool_search_request`
- `tool_gap_request`

Runner behavior:

- key/config guard;
- optional credit guard;
- provider prompt/context builder over `IModelCompletionClient` that includes the transient raw prompt, stage, graph revision, allowed primitive manifest, allowed field paths, mood/media tokens, submitted answer values, and sanitized context refs;
- explicit HTML/form primitive tool menu in the provider request, including selection rules for destination confirmation, exact dates, pace, multiple preferences, and planning task decomposition;
- one bounded repair attempt after malformed JSON or schema-invalid output;
- unsupported primitive validation against manifest ids/kinds/renderers;
- prompt-specific acceptance; generic/static output is blocked as schema invalid;
- semantic primitive acceptance: destination confirmation as `text_input`, exact date/date-range as `text_input`, and pace as `text_input` with available options are blocked as `planner_model_primitive_renderer_mismatch`;
- composite-form acceptance requires at least one non-text interactive primitive plus `task_decomposition`, task refs, and task records with safe ids/titles/state/order; otherwise the runner/evaluator blocks with fixed renderer-mismatch or task-decomposition-missing outcomes before reporting accepted;
- no paid-provider fallback unless explicitly configured by the caller and reported.

Provider clients:

- `OpenRouterModelCompletionClient` remains the Qwen/OpenRouter path with `/api/v1/credits` guard support.
- `OpenAiModelCompletionClient` is the OpenAI-compatible path for UI/server composition. It implements `IModelCompletionClient` and `IProviderCreditClient`, uses `OPENAI_API_KEY` or `OPENAI_API_KEY_FILE`/configured key file loading, defaults to `gpt-4.1-mini`, and reports OpenAI credit status as unknown/not exhausted because no safe credit endpoint is used in this provider lane.
- Both clients use strict JSON schema response format, bounded timeout handling, empty-content blocking, malformed JSON blocking, caller-cancellation preservation, and typed provider exceptions for fixed downstream classification.

Fixed outcomes include:

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
- `planner_model_tool_not_allowed`
- `planner_model_primitive_renderer_mismatch`
- `planner_model_task_decomposition_missing`
- `planner_model_provider_unavailable`

Runtime primitive labels, prompt text, raw user prompt text, completions, request/response bodies, keys, credentials, approval tokens, hold references, booking/payment refs, and candidate-display values must not be persisted in committed artifacts.

Shellby contract mapping assumption: this provider-local mirror is intentionally shaped around the Sprint 022 `PlannerToolManifest` plan. Once the canonical shared contract is published, an integration adapter should map canonical manifest primitive ids/kinds/renderers into `PlannerToolManifestMirror` without inventing semantic data.
