# Sprint 023 Provider Dynamic Form Builder Report

Status: `accepted`

Required tests remained deterministic/offline. Guarded manual live smoke used the provider-local `PlannerPrimitiveRunner` plus `PlannerPrimitiveEvaluator`; no direct-provider shortcut was counted as proof.

## Sanitized Live Attempts

### OpenAI

- Provider: `openai`
- Model: `gpt-4.1-mini-2025-04-14`
- Request id: `chatcmpl-DwlpW3nWYCyvuiXBXseYepKhrsLrZ`
- Prompt hash: `2a4a2caf9753d8ad`
- Outcome: `planner_model_accepted`
- Failure class: none
- Response length: `1146`
- Primitive count: `1`
- Task count: `1`
- Option count: `0`
- Output kind: `CompositeForm`
- Manifest version: `v1`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenRouter

- Provider: `openrouter`
- Model: `qwen/qwen3-14b-04-28`
- Request id: `gen-1782899282-WDi965RqtIvkVemEPQx0`
- Prompt hash: `2a4a2caf9753d8ad`
- Outcome: `planner_model_repaired_json`
- Failure class: none
- Response length: `2555`
- Primitive count: `4`
- Task count: `1`
- Option count: `0`
- Output kind: `CompositeForm`
- Manifest version: `v1`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

OpenAI initially returned a fixed `provider_http_4xx` when the schema used free-form `rendererHints`. The committed repair bounds `rendererHints` to explicit `layout` and `variant` string properties. The subsequent OpenAI run accepted through the provider runner/evaluator.

Raw-free local JSONL diagnostics were written under gitignored `artifacts/live-runs/`. They contain only provider/model/request id, prompt hash, fixed outcome, duration, response length, primitive count, task count, option count, output kind, manifest version, and fallback/raw persistence booleans.

## Prompt-Specific Proof

- The provider prompt builder now sends the transient runtime prompt, stage, graph revision, allowed primitive manifest, allowed field paths, allowed mood/media tokens, submitted answer values, and sanitized context refs to the model.
- Accepted provider rows require prompt-specific model output. Generic static output such as `Trip basics` / `Tell us the basics` is rejected as `planner_model_schema_invalid`.
- Offline prompt A/B tests prove Osaka and Iceland prompts differ structurally by option ids and task ids.
- Contextual claims require `toolContextRefs`; the only Sprint 023 context source used by the provider lane is explicitly named `mock_context_provider`.

No raw prompt text, submitted answer values, request bodies, provider responses, completions, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, or raw exception text were committed.
