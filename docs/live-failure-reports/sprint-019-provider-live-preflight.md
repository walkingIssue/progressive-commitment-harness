# Sprint 019 Provider Live Preflight Report

Status: `attempted`

Required tests remained deterministic/offline. A single guarded manual OpenRouter smoke was run because the local OpenRouter key file was present and non-empty.

## Providers And Models

- OpenRouter / Qwen: `attempted`
- OpenAI-compatible: `not_configured`
- Grok/xAI-compatible: `not_configured`

## Sanitized Live Attempt

- Credit check: `available`
- Completion request: `attempted`
- Fixed outcome: `live_preflight_accepted`
- Provider: `openrouter`
- Model: `qwen/qwen3-14b-04-28`
- Request id: `gen-1782814628-plDPEeIM2LTpSWo9BfAR`
- Response length: `183`
- Raw request body persisted: no
- Raw response body persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

Expected OpenRouter sequence was observed from the guarded script: `/api/v1/credits` first, then `/api/v1/chat/completions` only after the credit guard reported available credits.

## Findings

- The provider accepted a strict structured-output preflight request and returned non-empty content.
- This lane did not attach the result to the harness conductor; that integration belongs to the Sprint 019 harness/UI merge path.
- The preflight runner now has deterministic rows for disabled, key missing, credit exhausted, timeout, empty content, malformed JSON, schema unsupported, packet mismatch, fallback disabled, provider unavailable, and accepted structured output.

## Follow-Up Tickets

- Wire the provider-local `SanitizedLivePreflightEvalRow` vocabulary into Sarah's live/deterministic provider status strip after Shellby's conductor contract lands.
- Add a coordinator-owned smoke command that runs the same OpenRouter preflight through the application service layer instead of an ad hoc guarded script.
