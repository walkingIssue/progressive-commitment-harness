# Live Turn Runner Diagnostics

Sprint 021 adds a provider-local live turn runner for the multi-turn end-user harness loop. It is broader than the Sprint 020 mission proposal runner, but remains dependency-light and does not reference harness or UI projects.

The runner uses the existing `IModelCompletionClient` plus optional `IProviderCreditClient` seams and returns provider-local DTOs:

- `LiveTurnPacket` carries trusted run id, turn id, packet id, session id, role, allowed output kinds, and trusted candidate ids/categories.
- `LiveTurnResult` can carry one of four output kinds: mission proposal, pending confirmation question, choice set, or summary/fallback notice.
- `SanitizedLiveTurnLogRow` records fixed outcome/failure class code, safe run/turn/provider metadata, response length, duration, and trusted candidate ids/categories only after validation.

Supported output kinds for Sprint 021:

- `mission_proposal`
- `pending_confirmation_question`
- `choice_set`
- `summary_fallback_notice`

Configuration follows the existing live model vocabulary:

- `PCH_LIVE_MODEL_ENABLED` or `PCH_LIVE_TURN_ENABLED` enables this runner.
- `PCH_LIVE_MODEL_KEY_AVAILABLE` or a recognized key env var marks key availability.
- Recognized key env vars are `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `XAI_API_KEY`, and `GROK_API_KEY`.
- `PCH_LIVE_MODEL_PROVIDER` selects `openrouter`, `openai`, or `grok-xai` style provider kind.
- `PCH_LIVE_MODEL_SKIP_CREDIT_GUARD=true` disables credit guard for providers without a credit endpoint.
- `PCH_LIVE_MODEL_SCHEMA_UNSUPPORTED=true` blocks with schema-invalid diagnostics.
- `PCH_LIVE_MODEL_ALLOW_PAID_FALLBACK=true` blocks with fallback-disabled diagnostics; this lane does not silently fall back to another paid provider.
- `PCH_LIVE_MODEL_TIMEOUT_SECONDS` sets the provider operation timeout.
- `PCH_LIVE_IN_HARNESS_MODEL` and `PCH_LIVE_STRONG_PLANNER_MODEL` override role model ids.

Failure class codes are intentionally more specific than generic provider unavailable:

- `provider_http_4xx`
- `provider_http_5xx`
- `provider_rate_limited`
- `provider_timeout`
- `provider_empty_content`
- `provider_malformed_json`
- `provider_schema_invalid`
- `provider_upstream_model_unavailable`
- `provider_network_error`
- `provider_unknown_error`

Credit, disabled, missing-key, and fallback-disabled guard classes are also represented so UI/harness integrations can explain why no request was made.

OpenRouter expected sequence when enabled:

1. Call `/api/v1/credits` when credit guard is enabled.
2. If credits are available, call `/api/v1/chat/completions` with strict JSON schema response format.
3. Persist only sanitized log fields: fixed outcome/failure class, trusted run/turn/packet ids, output kind enum, trusted candidate ids/categories, response length, provider/model/request metadata, and duration.

Manual live smoke may spend credits when explicitly configured. Required tests remain deterministic/offline and do not use network, keys, or provider credits.
