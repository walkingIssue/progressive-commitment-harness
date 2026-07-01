# Planner Primitive Runner

Sprint 022 adds a provider-local planner primitive/form runner. It consumes a provider-local mirror of the harness `PlannerToolManifest` shape and asks a live model to emit only primitive/form invocations that the harness can validate before UI render.

This lane remains dependency-light:

- no provider-to-harness reference;
- no UI reference;
- no browser-side model calls;
- required tests stay deterministic/offline.

Provider-local shapes:

- `PlannerToolManifestMirror` carries manifest id/version, graph revision, session id, stage, allowed primitive definitions, allowed field paths, mood tokens, and max primitive count.
- `PlannerModelRequest` carries run/turn ids, manifest mirror, locale, optional prompt digest, and runtime-only raw prompt text marked `JsonIgnore`.
- `PlannerModelResult` carries output kind, primitive invocations, repair status, response length, duration, and accepted provider/model/request metadata.
- `SanitizedPlannerModelLogRow` carries only fixed outcomes, safe ids, manifest version, primitive ids/counts, repair flag, timing, response length, and accepted provider metadata.

Supported output kinds:

- `composite_form`
- `tool_search_request`
- `tool_gap_request`

Runner behavior:

- key/config guard;
- optional credit guard;
- strict JSON schema request over `IModelCompletionClient`;
- one bounded repair attempt after malformed JSON or schema-invalid output;
- unsupported primitive validation against manifest ids/kinds/renderers;
- no paid-provider fallback unless explicitly configured by the caller and reported.

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
- `planner_model_unsupported_primitive`
- `planner_model_provider_unavailable`

Runtime primitive labels, prompt text, raw user prompt text, completions, request/response bodies, keys, credentials, approval tokens, hold references, booking/payment refs, and candidate-display values must not be persisted in committed artifacts.

Shellby contract mapping assumption: this provider-local mirror is intentionally shaped around the Sprint 022 `PlannerToolManifest` plan. Once the canonical shared contract is published, an integration adapter should map canonical manifest primitive ids/kinds/renderers into `PlannerToolManifestMirror` without inventing semantic data.
