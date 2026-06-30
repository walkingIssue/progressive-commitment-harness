# Live Preflight Runner

Sprint 019 adds a provider-local live preflight surface for the end-user model-attached planning loop. It checks whether configured model roles can reach a live provider and return strict structured output before the UI attempts a live planning turn.

The preflight lane is dependency-light and provider-local. It uses the existing `IModelCompletionClient` and optional `IProviderCreditClient` seams, and it shares the existing `LiveModelRole` role vocabulary:

- `InHarnessActionGenerator`
- `StrongPlanner`

Configuration builds on the existing live-model vocabulary:

- `PCH_LIVE_MODEL_ENABLED` or `PCH_LIVE_PREFLIGHT_ENABLED` enables preflight.
- `PCH_LIVE_MODEL_KEY_AVAILABLE` or a recognized key env var marks key availability.
- Recognized key env vars are `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `XAI_API_KEY`, and `GROK_API_KEY`.
- `PCH_LIVE_MODEL_PROVIDER` selects `openrouter`, `openai`, or `grok-xai` style provider kind.
- `PCH_LIVE_MODEL_SKIP_CREDIT_GUARD=true` disables credit guard for providers without a credit endpoint.
- `PCH_LIVE_MODEL_SCHEMA_UNSUPPORTED=true` blocks preflight with `live_preflight_schema_unsupported`.
- `PCH_LIVE_MODEL_ALLOW_PAID_FALLBACK=true` blocks preflight; this lane does not silently fall back to another paid provider.
- `PCH_LIVE_MODEL_TIMEOUT_SECONDS` sets the provider operation timeout.
- `PCH_LIVE_IN_HARNESS_MODEL` and `PCH_LIVE_STRONG_PLANNER_MODEL` override role model ids.

OpenRouter expected sequence when enabled:

1. Call `/api/v1/credits` when credit guard is enabled.
2. If credits are available, call `/api/v1/chat/completions` with strict JSON schema response format.
3. Persist only sanitized preflight status fields: fixed outcome, role enum, probe id, model id, provider kind, response length, provider/model/request metadata.

Fixed blocked outcomes include disabled config, missing key, credit exhausted, timeout, empty content, malformed JSON/schema, schema unsupported, packet mismatch, fallback disabled, and provider unavailable.

Required tests are deterministic and offline. Manual live smoke may spend credits only when explicitly configured and must never print or persist raw request bodies, raw responses, completions, keys, credentials, approval tokens, hold references, candidate display values, or exception text.
