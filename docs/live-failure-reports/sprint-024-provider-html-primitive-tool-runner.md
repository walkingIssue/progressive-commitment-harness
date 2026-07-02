# Sprint 024 Provider HTML Primitive Tool Runner Report

Status: `attempted`

Required tests remained deterministic/offline. Guarded manual live smoke used the provider-local `PlannerPrimitiveRunner` plus `PlannerPrimitiveEvaluator`; no direct-provider shortcut was counted as proof.

Repair update: after UI integration showed `planner_model_accepted` could still reach the harness without canonical task decomposition, the runner now performs the same semantic task-decomposition gate before returning an accepted runtime result. Missing `task_decomposition`, missing task refs, or missing safe task records now returns fixed `planner_model_task_decomposition_missing` instead of accepted.

Parser repair update: after UI integration later saw repeated `planner_model_malformed_json`, the planner parser now performs runtime-only extraction of a single balanced JSON object from fenced or prose-wrapped model content before using the bounded repair prompt. Raw completion text remains unlogged and uncommitted.

Answer-shape repair update: after current browser trace showed `planner_model_accepted` followed by harness `primitive_answer_schema_invalid` for `slider_budget`, provider semantic validation now blocks untrusted field paths and numeric primitives that carry select-style option payloads. Invalid budget field paths return fixed `planner_model_field_path_not_allowed`; malformed slider/number answer shapes return fixed `planner_model_answer_schema_invalid`.

## Providers And Models

- OpenAI / `gpt-4.1-mini`: `accepted`
- OpenRouter / `qwen/qwen3-14b`: `provider_timeout`
- Groq-compatible: `not_configured_for_this_lane`
- Grok/xAI-compatible: `not_configured_for_this_lane`

## Sanitized Live Attempts

### OpenAI

- Attempt count: `5`
- Completion request: `attempted`
- Fixed outcome: `planner_model_accepted`
- Failure class: none
- Model: `gpt-4.1-mini-2025-04-14`
- Request id: `chatcmpl-DwoWilCQAtMV7iNEVLQZksJTWCwQP`
- Output kind: `composite_form`
- Primitive ids: `date_range`, `multi_select`, `radio_group`, `select`, `task_decomposition`
- Primitive kinds: `date_range`, `multi_select`, `radio_group`, `select`, `task_decomposition`
- Primitive count: `5`
- Task count: `4`
- Option count: `9`
- Response length: `6399`
- Anti-gaming proof: accepted row contains non-text primitives plus `task_decomposition`; it is not a generic text-input-only form.
- Task-decomposition proof: accepted row contains `task_decomposition` plus nonzero safe task ids/count; `planner_model_accepted` cannot be emitted by the provider runner for missing task decomposition.
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenAI Budget/Slider Repair Smoke

- Attempt count: `1`
- Completion request: `attempted`
- Fixed outcome: `planner_model_task_decomposition_missing`
- Failure class: `task_decomposition_missing`
- Model: `gpt-4.1-mini`
- Request id persisted: none
- Primitive count: `0`
- Task count: `0`
- Option count: `0`
- False accepted slider shape observed: no
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenRouter

- Attempt count: `6`
- Credit check: `attempted`
- Completion request: `attempted_when_guard_passed`
- Fixed outcome: `planner_model_timeout`
- Failure class: `provider_timeout`
- Model: `qwen/qwen3-14b`
- Request id persisted: none
- Primitive count: `0`
- Task count: `0`
- Option count: `0`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

Raw-free local JSONL diagnostics were written under `artifacts/live-runs/`, which is gitignored and not part of the release artifact set.

## Findings

- The provider prompt now presents the full Sprint 024 HTML/form primitive tool menu to the model.
- Fenced/prose-wrapped JSON is parsed only in memory; unextractable or invalid JSON still maps to fixed `planner_model_malformed_json`.
- The runner and evaluator block destination/date/pace controls when a model tries to satisfy them with generic `text_input`/`textarea`.
- The runner and evaluator block slider/number controls with select-style options before provider accepted status can be reported.
- Accepted composite-form rows require at least one non-text interactive primitive and `task_decomposition` with task refs plus safe task records. This gate runs before `PlannerPrimitiveRunner` returns success, so UI/server integration cannot receive `planner_model_accepted` with missing task decomposition.
- Accepted rows persist only fixed codes, safe ids, primitive ids/kinds/counts, task ids/count, option count, response length, duration bucket, and accepted provider metadata.
- A runner timeout classification edge found during live OpenRouter smoke was repaired so provider timeout rows remain fixed-code sanitized instead of surfacing cancellation.
- No raw keys, request bodies, provider responses, completions, prompts, submitted answers, context text, or exception text were printed or committed.
