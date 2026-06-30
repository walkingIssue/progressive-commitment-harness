# Live Model Role Runner

Sprint 016 adds a provider-local live model role registry and guarded runner for read-only model output generation. The surface stays dependency-light and returns provider-local DTOs only; it does not reference harness or UI projects.

Supported roles:

- `InHarnessActionGenerator` maps to the configured in-harness action model id.
- `StrongPlanner` maps to the configured strong-planner model id.

Configuration controls:

- `PCH_LIVE_MODEL_ENABLED` must be `true` before the runner calls a completion provider.
- `PCH_LIVE_MODEL_KEY_AVAILABLE` must be `true`; key material is loaded outside this DTO surface and is never stored in runner rows.
- `PCH_LIVE_MODEL_SKIP_CREDIT_GUARD=true` disables the provider credit guard for environments that do not expose credits.
- `PCH_LIVE_MODEL_FALLBACK_POLICY=allow_same_provider` is the only non-default fallback mode. The default is fallback disabled.
- `PCH_LIVE_MODEL_TIMEOUT_SECONDS` records the requested timeout setting for callers that construct the OpenRouter/OpenAI-compatible client.
- `PCH_LIVE_IN_HARNESS_MODEL` and `PCH_LIVE_STRONG_PLANNER_MODEL` override the default `qwen/qwen3-14b` model id.

The runner uses `IModelCompletionClient` and requests strict JSON schema output with packet id, output kind, argument object, summary, and optional `uiMood`. The optional mood field is allowlisted to enum-style values only: `calm_morning`, `lively_food`, `reflective_culture`, `soft_nature`, `restorative_downtime`, and `logistics`. Unknown mood text is recorded as `Unspecified`, not raw model prose.

Guard behavior:

- Live mode disabled, missing key, disabled fallback, missing/exhausted credits, provider unavailable, timeout, empty content, malformed schema, packet mismatch, and unsupported output all return fixed outcome codes.
- `LiveModelRunnerOptions.Timeout` is enforced around credit checks and completion calls. Caller-requested cancellation remains cooperative, while runner timeout maps to `live_model_timeout`.
- Parsed argument JSON is available only as in-memory runtime data and is JSON-ignored on `LiveModelRunResult`; eval/status rows never persist raw argument values.
- Null or malformed packets, options, runner options, eval cases, and eval case packets become fixed `live_model_malformed_schema` rows with redacted row identifiers.
- No blocked/error result persists raw prompts, provider payloads, exception messages, credentials, keys, context digests, or model prose.
- No fallback to another paid provider is attempted by this provider-local runner.

Expected OpenRouter activity for a manual smoke:

- With credit guard enabled, the client should first call `/api/v1/credits`.
- If credits are available, the client should call `/api/v1/chat/completions` using the configured role model, usually `qwen/qwen3-14b`.
- A successful smoke may record provider/model/request metadata and response content length in sanitized rows.
- A blocked smoke should report a fixed outcome such as `live_model_key_missing`, `live_model_credit_exhausted`, `live_model_timeout`, `live_model_empty_content`, or `live_model_malformed_schema` without printing provider payloads or key material.

Required tests remain deterministic and offline. Manual live smoke may spend credits only when the caller has explicitly enabled live mode, key presence, provider health or credit checks are safe, and the smoke is useful.
