# Sprint 022 End-User Server Primitive UI Report

## Scope

- Added a UI-owned server-side planning session seam for `/trip`.
- Live mode now receives a `ValidatedTurnView` from `PlanningSessionService` and renders primitives through `PrimitiveRenderer` / `FormBuilderView`.
- Deterministic golden trace cards remain available only through explicit deterministic mode.
- Browser-local fallback no longer mutates transcript/final state. When Blazor is absent and an interaction does not reach the server, the UI marks `browser_circuit_disconnected`.

## Static Verification

- `npm run build:ui`: passed.
- `dotnet build src\Pch.UI\Pch.UI.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed during development.
- `dotnet test tests\Pch.UI.Tests\Pch.UI.Tests.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 72 tests.

## In-App Browser Baseline

- URL: `http://127.0.0.1:5222/trip?live=1`.
- Server started with live OpenRouter key-file config.
- In-app browser rendered `/trip`, but reported `window.Blazor=false`.
- Reconnect modal was not open.
- First attempted live click did not mutate transcript or final state. The browser helper set:
  - `data-browser-circuit-state="browser_circuit_disconnected"`
  - `data-error-code="PCH_UI_BROWSER_CIRCUIT_DISCONNECTED"`
  - `data-blocked-reason="browser_circuit_disconnected"`
- This is a valid hard blocked browser state, not a passing interaction.

## Secondary Browser Diagnostic

- Standalone local Chrome was used only as a secondary diagnostic.
- Baseline markers:
  - `window.Blazor=true`
  - reconnect modal absent
  - `data-browser-circuit-state="connected"`
  - live mode selected with `data-send-action="planner"`
- Live prompt send reached the server and attempted the provider path:
  - `data-provider-request-state="attempted"`
  - `data-live-turn-attempt-count="1"`
  - `data-selected-provider="openrouter"`
- The real provider/preflight path blocked before a model-produced primitive form:
  - `data-live-preflight-state="preflight_blocked"`
  - `data-live-proposal-state="provider_blocked"`
  - `data-provider-outcome="live_preflight_timeout"`
  - `data-error-code="PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"`
  - `data-blocked-reason="live_preflight_timeout"`
  - `data-validated-turn-source="provider_blocked"`
  - `data-validated-turn-outcome="live_preflight_timeout"`
- UI rendered a validated provider-failure primitive:
  - `data-primitive-renderer="provider-failure"`
  - `data-primitive-id="provider_failure_notice"`
  - `data-primitive-instance-id="primitive-provider-blocked"`
  - `data-media-asset-id="backdrop.urban.station_grid.budget_practical"`

## Live Smoke Status

Sprint 022 Lane C is not READY under the stated exit gate:

- In-app browser did not provide a healthy Blazor circuit.
- The configured live provider path attempted a request, but blocked at `live_preflight_timeout`.
- No model-produced form/deck was accepted from the real provider in browser smoke.
- No user answer reached a second real provider turn in browser smoke.

## Sanitization

No raw prompt text, provider body, API key, credentials, approval token, hold/booking/payment reference, candidate display sentinel, raw exception text, or secret sentinel was rendered in the observed DOM markers. The report records fixed outcome codes and trusted ids only.
