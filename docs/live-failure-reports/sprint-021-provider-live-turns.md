# Sprint 021 Provider Live Turns Report

Status: `attempted`

Required tests remained deterministic/offline. A single guarded manual OpenRouter live turn smoke was attempted because the local OpenRouter key file was present and non-empty.

## Providers And Models

- OpenRouter / Qwen: `attempted`
- OpenAI-compatible: `not_configured`
- Grok/xAI-compatible: `not_configured`

## Sanitized Live Attempts

- Attempt count: `1`
- Credit check: `attempted`
- Completion request: `attempted_when_guard_passed`
- Fixed outcome: `live_turn_provider_unknown_error`
- Failure class: `ProviderUnknownError`
- Provider: `openrouter`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

The guarded script used the expected OpenRouter sequence: `/api/v1/credits` first, then `/api/v1/chat/completions` only after the credit guard allowed the attempt. The provider failure was recorded only as the fixed sanitized outcome above. A raw-free local JSONL diagnostic was written under `artifacts/live-runs/`, which is not part of the release artifact set.

## Findings

- The required provider tests cover accepted mission proposal, pending confirmation question, choice set, summary/fallback notice, disabled, key missing, credit exhausted, fallback disabled, timeout, empty content, malformed JSON, schema invalid, http 4xx/5xx, rate limited, upstream model unavailable, network error, unknown provider error, packet mismatch, unsupported output, candidate ownership mismatch, unsafe value redaction, and caller cancellation.
- Accepted log rows keep only trusted run/turn/packet metadata, allowlisted output/role enums, trusted candidate ids/categories, counts, response length, duration, and provider/model/request metadata.
- Runtime question text, choice framing, option labels/rationales, summary text, mission field values, commitment titles/locations, and evidence id collections are not serialized into log artifacts.
- The manual live smoke did not produce an accepted structured live turn in this run; no alternate paid provider fallback was attempted.
