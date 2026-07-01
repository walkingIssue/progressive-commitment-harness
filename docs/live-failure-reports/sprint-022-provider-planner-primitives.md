# Sprint 022 Provider Planner Primitives Report

Status: `attempted`

Required tests remained deterministic/offline. Guarded manual live smoke initially reported `provider_unknown_error` through a coarse script path. A repair smoke then used direct provider connectivity/schema checks and obtained accepted strict primitive/form envelopes from both OpenAI and OpenRouter. A second repair added a repo OpenAI `IModelCompletionClient` and verified the real `PlannerPrimitiveRunner`/evaluator path with OpenAI.

## Providers And Models

- OpenAI / `gpt-4.1-mini`: `accepted`
- OpenRouter / `qwen/qwen3-14b`: `accepted`
- Groq-compatible: `not_configured_for_this_lane`
- Grok/xAI-compatible: `not_configured_for_this_lane`

## Sanitized Live Attempts

### OpenAI

- Attempt count: `2`
- Completion request: `attempted`
- Fixed outcome: `planner_model_accepted`
- Failure class: none
- Model: `gpt-4.1-mini-2025-04-14`
- Request id: `chatcmpl-Dwijtj7U9XT2g0o31nckbuWmB3GDk`
- Response length: `750`
- Primitive count: `2`
- Manifest version: `v1`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenAI Through PlannerPrimitiveRunner

- Attempt count: `1`
- Completion request: `attempted`
- Fixed outcome: `planner_model_accepted`
- Failure class: none
- Model: `gpt-4.1-mini-2025-04-14`
- Request id: `chatcmpl-Dwj8nJbGYnWKVNgLCF0tNkGy25JJA`
- Response length: `702`
- Primitive count: `2`
- Manifest version: `v1`
- Runner/evaluator validation: `accepted`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenRouter

- Attempt count: `2`
- Credit check: `attempted`
- Completion request: `attempted_when_guard_passed`
- Fixed outcome: `planner_model_accepted`
- Failure class: none
- Model: `qwen/qwen3-14b-04-28`
- Request id: `gen-1782887425-iiYAigQasKbTlNxhzyaE`
- Response length: `538`
- Primitive count: `1`
- Manifest version: `v1`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

Raw-free local JSONL diagnostics were written under `artifacts/live-runs/`, which is gitignored and not part of the release artifact set. The repair diagnostics include only provider/model/request id, fixed outcome, duration, response length, primitive count, and manifest version.

## Findings

- The provider runner supports accepted composite forms, tool search requests, tool gap requests, malformed JSON repair, unsupported primitive blocking, key missing, credit exhausted, rate limited, timeout, empty content, schema invalid, and provider unavailable outcomes.
- `OpenAiModelCompletionClient` now gives repo composition a real OpenAI `IModelCompletionClient` path with `OPENAI_API_KEY` / `OPENAI_API_KEY_FILE` / configured key-file support and fixed failure classification.
- Both live providers returned strict primitive/form envelopes that passed the narrow provider smoke validation.
- A real OpenAI primitive/form envelope also passed through `PlannerPrimitiveRunner` and `PlannerPrimitiveEvaluator`.
- No raw keys, request bodies, provider responses, completions, prompts, or exception text were printed or committed.
- No paid-provider fallback was used. OpenAI and OpenRouter were attempted independently because both key files were configured.
