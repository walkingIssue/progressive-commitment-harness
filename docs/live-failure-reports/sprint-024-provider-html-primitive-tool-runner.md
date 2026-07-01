# Sprint 024 Provider HTML Primitive Tool Runner Report

Status: `attempted`

Required tests remained deterministic/offline. Guarded manual live smoke used the provider-local `PlannerPrimitiveRunner` plus `PlannerPrimitiveEvaluator`; no direct-provider shortcut was counted as proof.

## Providers And Models

- OpenAI / `gpt-4.1-mini`: `accepted`
- OpenRouter / `qwen/qwen3-14b`: `provider_timeout`
- Groq-compatible: `not_configured_for_this_lane`
- Grok/xAI-compatible: `not_configured_for_this_lane`

## Sanitized Live Attempts

### OpenAI

- Attempt count: `2`
- Completion request: `attempted`
- Fixed outcome: `planner_model_accepted`
- Failure class: none
- Model: `gpt-4.1-mini-2025-04-14`
- Request id: `chatcmpl-DwndVXa36VBJYO44krcxYLsRlU64x`
- Output kind: `composite_form`
- Primitive ids: `date_range`, `radio_group`, `select`, `task_decomposition`
- Primitive kinds: `date_range`, `radio_group`, `select`, `task_decomposition`
- Primitive count: `4`
- Task count: `3`
- Option count: `5`
- Response length: `4209`
- Anti-gaming proof: accepted row contains non-text primitives plus `task_decomposition`; it is not a generic text-input-only form.
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenRouter

- Attempt count: `2`
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
- The evaluator blocks destination/date/pace controls when a model tries to satisfy them with generic `text_input`/`textarea`.
- Accepted composite-form rows require at least one non-text interactive primitive and `task_decomposition` with task records.
- Accepted rows persist only fixed codes, safe ids, primitive ids/kinds/counts, task count, option count, response length, duration bucket, and accepted provider metadata.
- A runner timeout classification edge found during live OpenRouter smoke was repaired so provider timeout rows remain fixed-code sanitized instead of surfacing cancellation.
- No raw keys, request bodies, provider responses, completions, prompts, submitted answers, context text, or exception text were printed or committed.
