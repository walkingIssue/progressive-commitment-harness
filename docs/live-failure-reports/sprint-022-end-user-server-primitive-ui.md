# Sprint 022 End-User Server Primitive UI Report

## Scope

- Added a DI-backed server-side planning session service for `/trip`.
- Integrated Shellby's canonical planner tool manifest and validated primitive contracts from `origin/sprint-022/harness-planner-tool-manifest` at `ba5a528dac492e268f613a54bd6bcba01e7572ad`.
- Integrated Kaneki's repaired provider planner primitive runner at `bd30f6eeca195293ec374cb625fdff5787ee67ba`, then the OpenAI client repair at `4990c98d50dd522ca9d39af3312b07b8c49db737`.
- Live mode now builds a canonical `PlannerToolManifest`, calls the provider `PlannerPrimitiveRunner` when configured, validates provider primitives through `PlannerPrimitiveValidator`, and renders a UI projection through `PrimitiveRenderer` and `FormBuilderView`.
- Deterministic golden trace cards remain available only through explicit deterministic mode.
- Browser-local fallback no longer mutates transcript or final state. If the helper detects a failed Blazor reconnect state, the UI marks `browser_circuit_disconnected`.

## Static Verification

- `npm run build:ui`: passed.
- `dotnet build src\Pch.UI\Pch.UI.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 0 warnings/errors.
- `dotnet test tests\Pch.UI.Tests\Pch.UI.Tests.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 72 tests.
- `git diff --check`: passed.

## Browser Diagnostics

- Static endpoint diagnostics from outside the in-app browser returned HTTP 200 for `/trip?live=1`, the Blazor framework script, and `/_blazor/negotiate?negotiateVersion=1`.
- Browser automation can report `window.Blazor=false` in the in-app browser context, but the first Send click still reaches the server and mutates Blazor component state. The contradiction is recorded as an in-app browser diagnostic, not a pass by itself.
- The final live smoke did not use fallback DOM mutation as acceptance evidence.

## Deterministic Smoke

- `/trip` deterministic mode remained functional.
- Observed fixed markers included:
  - `data-deterministic-mode="offline-deterministic"`
  - `data-final-state="applied"`
  - `data-provider-request-state="not_attempted"`
  - `data-provider-outcome="deterministic_fallback_active"`
  - `data-choice-set-id="choice-japan-style"`

## OpenRouter Live Smoke

- Configuration was sanitized and key-file based: live model enabled, planner primitive enabled, provider `openrouter`, model `openai/gpt-4.1-mini`, key loaded from a file. No key value was logged.
- A real provider request was attempted through the canonical provider planner primitive runner.
- The first provider turn was accepted and rendered a validated form through `PrimitiveRenderer` / `FormBuilderView`.
- Observed markers included:
  - `data-selected-provider="openrouter"`
  - `data-provider-request-state="attempted"`
  - `data-provider-outcome="planner_model_accepted"`
  - `data-validated-turn-source="live_provider_candidate"`
  - `data-validated-turn-outcome="awaiting_user_input"`
  - `data-harness-validation-state="awaiting_user_input"`
  - `data-primitive-renderer="form"`
  - `data-primitive-instance-id="primitive-trip-basics-form"`
- The in-app browser circuit disconnected before answer submission could reach the server, so no second provider turn was proven.

## OpenAI Live Smoke

- Configuration was sanitized and key-file based: live model enabled, planner primitive enabled, provider `openai`, model `gpt-4.1-mini`, key loaded from a file. No key value was logged.
- A real provider request was attempted through the canonical provider planner primitive runner and repaired OpenAI completion client.
- The first provider turn was accepted and rendered a validated form through `PrimitiveRenderer` / `FormBuilderView`.
- Observed markers included:
  - `data-selected-provider="openai"`
  - `data-provider-request-state="attempted"`
  - `data-provider-outcome="planner_model_accepted"`
  - `data-validated-turn-source="live_provider_candidate"`
  - `data-validated-turn-outcome="awaiting_user_input"`
  - `data-harness-validation-state="awaiting_user_input"`
  - `data-primitive-renderer="form"`
  - `data-primitive-instance-id="primitive-trip-basics-form"`
  - `data-browser-circuit-state="browser_circuit_disconnected"`
  - `data-error-code="PCH_UI_BROWSER_CIRCUIT_DISCONNECTED"`
  - `data-blocked-reason="browser_circuit_disconnected"`
- The parent server submit control was present and clicked, but the disconnected circuit prevented the answer DTO from reaching the server. No second provider turn was proven.

## Status

Sprint 022 Lane C remains BLOCKED under the stated exit gate:

- Real OpenRouter and OpenAI provider requests can be attempted from the UI process.
- Real provider output can produce an accepted validated primitive/form rendered through `PrimitiveRenderer` / `FormBuilderView`.
- The required answer submission and second provider turn did not pass in the in-app browser because the Blazor circuit disconnected after the first live provider turn.
- This is not being relabeled as READY, and standalone or fallback-only evidence is not being counted.

## Sanitization

No raw prompt text, raw completion, provider body, API key, credential, approval token, hold/booking/payment reference, candidate display sentinel, raw exception text, or secret sentinel was rendered in the observed DOM markers or committed report. The report records only fixed outcome codes, provider names, model names, and trusted ids.
