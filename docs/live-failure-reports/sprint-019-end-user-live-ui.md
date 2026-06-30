# Sprint 019 End-User Live UI Report

## Scope

- End-user `/trip` chat keeps deterministic/offline mode as the required-test default.
- Live role selection is visible for deterministic, in-harness, and strong-planner roles.
- The first live-mode send records a sanitized preflight state before any provider request can run.

## Observations

- With no explicit live configuration, the UI reports `blocked_by_guard`, `provider_request_state=not_attempted`, and `latest_turn_source=deterministic_fallback`.
- The deterministic transcript, option cards, selected-card echo path, and planning timeline remain available after the guard block.
- The provider failure notice uses fixed codes only: `PCH_UI_LIVE_MODEL_GUARDED` and provider outcome codes such as `live_model_disabled`.

## Live Smoke Status

- No live provider smoke was attempted from this checkout because live provider configuration was not enabled for the UI lane.
- Required tests and default browser smoke remain deterministic/offline and do not depend on API keys, provider credits, search, booking, payment, or live network calls.
- In-app browser control timed out during navigation in this worker thread. Local HTTP smoke verified `/trip`, `/stage-cockpit`, marker presence, route separation, and static Blazor/helper asset availability; coordinator browser smoke should re-run the click path from an integration checkout.

## Sanitization

- Raw prompt text is summarized by length before transcript storage.
- Raw provider payloads, raw completions, credentials, approval tokens, hold references, payment data, candidate display sentinels, exception text, and secret-like sentinels are not rendered or serialized by the UI state.

## Deferred Hardening

- Replace the UI-local live-turn seam with the canonical Sprint 019 harness/provider live chat coordinator if Shellby/Kaneki publish one.
- Add a live-provider smoke path only when explicit runtime configuration and a coordinator-approved key/credit posture are present.
