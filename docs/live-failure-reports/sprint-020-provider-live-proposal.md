# Sprint 020 Provider Live Proposal Report

Status: `attempted`

Required tests remained deterministic/offline. A single guarded manual OpenRouter smoke was attempted because the local OpenRouter key file was present and non-empty.

## Providers And Models

- OpenRouter / Qwen: `attempted`
- OpenAI-compatible: `not_configured`
- Grok/xAI-compatible: `not_configured`

## Sanitized Live Attempt

- Credit check: `attempted`
- Completion request: `attempted_when_guard_passed`
- Fixed outcome: `live_mission_proposal_provider_unavailable`
- Provider: `openrouter`
- Raw request body persisted: no
- Raw response body persisted: no
- Raw completion persisted: no
- API key or credential persisted: no
- Paid-provider fallback used: no

The guarded script used the expected OpenRouter sequence: `/api/v1/credits` first, then `/api/v1/chat/completions` only after the credit guard allowed the attempt. The provider failure was recorded only as the fixed sanitized outcome above.

## Findings

- The required provider tests cover disabled, key missing, credit exhausted, timeout, empty content, malformed JSON, schema invalid, packet mismatch, unsupported proposal value, unsafe value redaction, fallback disabled, provider unavailable, and accepted structured proposal paths.
- Accepted eval rows keep only trusted packet/session metadata, allowlisted enums, counts, response length, and provider/model/request metadata.
- Runtime field values, commitment titles, locations, and evidence id collections are not serialized into eval/status artifacts.
- The manual live smoke did not produce an accepted structured proposal in this run; no alternate paid provider fallback was attempted.
