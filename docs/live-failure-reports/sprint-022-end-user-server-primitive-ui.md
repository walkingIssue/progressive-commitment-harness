# Sprint 022 End-User Server Primitive UI Report

## Scope

- Added a UI-owned server-side planning session seam for `/trip`.
- Integrated Shellby's canonical planner tool manifest/validated primitive contracts from `origin/sprint-022/harness-planner-tool-manifest` at `ba5a528dac492e268f613a54bd6bcba01e7572ad`.
- Integrated Kaneki's provider planner primitive runner from `origin/sprint-022/provider-planner-primitive-runner` at `a5c0eca3b0a79c0323444be563fab6f076d84109`.
- Live mode now builds a canonical `PlannerToolManifest`, attempts the provider `PlannerPrimitiveRunner` when configured, validates provider primitives through `PlannerPrimitiveValidator`, and renders a UI projection through `PrimitiveRenderer` / `FormBuilderView`.
- Deterministic golden trace cards remain available only through explicit deterministic mode.
- Browser-local fallback no longer mutates transcript/final state. When the helper is loaded but a Blazor circuit is unavailable, the UI marks `browser_circuit_disconnected`.

## Static Verification

- `npm run build:ui`: passed.
- `dotnet build src\Pch.UI\Pch.UI.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 0 warnings/errors.
- `dotnet test tests\Pch.UI.Tests\Pch.UI.Tests.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 72 tests.

## In-App Browser Baseline

- URL: `http://127.0.0.1:5231/trip?live=1`.
- Server started with live OpenRouter key-file config: live model enabled, planner primitive enabled, provider `openrouter`, model `qwen/qwen3-14b`, key loaded via key-file path. No key value was logged.
- In-app browser rendered `/trip`. Browser automation's read-only context reported `window.Blazor=false`, but clicking Send did reach the server and mutated the Blazor component state.
- Reconnect modal was not open.
- Static endpoint diagnostics from outside the in-app browser:
  - `/trip?live=1`: HTTP 200 and includes the Blazor script.
  - `/_framework/blazor.web.ne14ti1q68.js`: HTTP 200.
  - `/_blazor/negotiate?negotiateVersion=1`: HTTP 200.
- In-app click with OpenRouter produced:
  - `data-provider-request-state="attempted"`
  - `data-provider-outcome="planner_model_malformed_json"`
  - `data-validated-turn-source="provider_blocked"`
  - `data-validated-turn-outcome="planner_model_malformed_json"`
  - `data-primitive-renderer="provider-failure"`
  - `data-primitive-id="assistant_message"`
- A previous OpenRouter attempt reached `planner_model_accepted` but the harness validator blocked it at `ownership_invalid`; after candidate ownership mapping was repaired, the next real provider response failed as malformed JSON. No accepted real primitive/form was produced.

## Secondary Browser Diagnostic

- OpenAI-configured in-app run used live model enabled, planner primitive enabled, provider `openai`, model `gpt-4.1-mini`, and `OPENAI_API_KEY_FILE`. No key value was logged.
- The repo currently has no OpenAI `IModelCompletionClient` implementation wired for the UI planner primitive runner. The UI surfaced:
  - `data-selected-provider="openai"`
  - `data-provider-request-state="not_attempted"`
  - `data-provider-outcome="planner_model_provider_unavailable"`
  - `data-validated-turn-source="provider_blocked"`
  - `data-validated-turn-outcome="planner_model_provider_unavailable"`
- Deterministic browser smoke on `/trip` passed:
  - `data-deterministic-mode="offline-deterministic"`
  - `data-final-state="applied"`
  - `data-provider-request-state="not_attempted"`
  - `data-provider-outcome="deterministic_fallback_active"`
  - `data-choice-set-id="choice-japan-style"` present.

## Live Smoke Status

Sprint 022 Lane C is not READY under the stated exit gate:

- In-app browser did provide a server-side interaction path for Send despite `window.Blazor=false` in the automation context.
- The configured OpenRouter path attempted a real provider request through the canonical provider planner primitive runner.
- OpenRouter did not produce an accepted model primitive/form in final smoke; the fixed sanitized provider blocker was `planner_model_malformed_json`.
- OpenAI could not be attempted because the repo has no OpenAI model-completion client wired into the UI planner primitive runner; the fixed blocker was `planner_model_provider_unavailable`.
- No user answer reached a second real provider turn in browser smoke.

## Sanitization

No raw prompt text, provider body, API key, credentials, approval token, hold/booking/payment reference, candidate display sentinel, raw exception text, or secret sentinel was rendered in the observed DOM markers. The report records fixed outcome codes and trusted ids only.
