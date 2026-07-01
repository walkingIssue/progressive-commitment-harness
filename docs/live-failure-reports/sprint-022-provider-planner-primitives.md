# Sprint 022 Provider Planner Primitives Report

Status: `attempted`

Required tests remained deterministic/offline. Guarded manual live smoke attempted one OpenAI request and one OpenRouter request because both local key files were present and non-empty.

## Providers And Models

- OpenAI / `gpt-4.1-mini`: `attempted`
- OpenRouter / `qwen/qwen3-14b`: `attempted`
- Groq-compatible: `not_configured_for_this_lane`
- Grok/xAI-compatible: `not_configured_for_this_lane`

## Sanitized Live Attempts

### OpenAI

- Attempt count: `1`
- Completion request: `attempted`
- Fixed outcome: `planner_model_provider_unavailable`
- Failure class: `provider_unknown_error`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

### OpenRouter

- Attempt count: `1`
- Credit check: `attempted`
- Completion request: `attempted_when_guard_passed`
- Fixed outcome: `planner_model_provider_unavailable`
- Failure class: `provider_unknown_error`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

Raw-free local JSONL diagnostics were written under `artifacts/live-runs/`, which is gitignored and not part of the release artifact set.

## Findings

- The provider runner supports accepted composite forms, tool search requests, tool gap requests, malformed JSON repair, unsupported primitive blocking, key missing, credit exhausted, rate limited, timeout, empty content, schema invalid, and provider unavailable outcomes.
- Both live providers reached fixed failure reporting without printing or committing raw keys, request bodies, provider responses, completions, prompts, or exception text.
- No accepted live primitive/form envelope was obtained in this run. This lane is therefore READY as a fixed provider-failure report rather than a live accepted-output proof.
- No paid-provider fallback was used. The lane stopped after bounded same-class failures.
