# Sprint 022 End-User Server Primitive UI Report

## Scope

- Added a DI-backed server-side planning session service for `/trip`.
- Integrated Shellby's canonical planner tool manifest and validated primitive contracts from `origin/sprint-022/harness-planner-tool-manifest` at `ba5a528dac492e268f613a54bd6bcba01e7572ad`.
- Integrated Kaneki's repaired provider planner primitive runner at `bd30f6eeca195293ec374cb625fdff5787ee67ba`, then the OpenAI client repair at `4990c98d50dd522ca9d39af3312b07b8c49db737`.
- Live mode builds a canonical `PlannerToolManifest`, calls the provider `PlannerPrimitiveRunner` when configured, validates provider primitives through `PlannerPrimitiveValidator`, and renders a UI projection through `PrimitiveRenderer` / `FormBuilderView`.
- Added `/api/planning/session/start`, `/api/planning/session/{sessionId}/answer`, and `/api/planning/session/{sessionId}` as server-side HTTP transport boundaries. The browser sends only prompt/answer DTOs; all model requests, prompt assembly, schema generation, key access, provider calls, and harness validation remain server-side.
- Primitive answer submission invokes the provider runner for the follow-up turn. The old local `BuildSecondTurn` / synthetic candidate deck path was removed.
- Browser-local disconnected handling no longer mutates a fake live transcript. When Blazor is disconnected, the helper calls the HTTP API and renders only returned sanitized validated turn data.

## Static Verification

- `npm run build:ui`: passed.
- `dotnet build src\Pch.UI\Pch.UI.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 0 warnings/errors.
- `dotnet test tests\Pch.UI.Tests\Pch.UI.Tests.csproj -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`: passed, 76 tests.
- `git diff --check`: passed.

## Service And HTTP Tests

- `PlanningSessionStore.StartAsync(...)` invokes the first runner and returns a sanitized validated form primitive.
- `PlanningSessionStore.AnswerAsync(...)` invokes the second runner and returns the follow-up validated turn or provider-failure primitive.
- Unknown session answer returns fixed `PCH_UI_PLANNING_SESSION_UNKNOWN` / `planning_session_unknown` and does not call the provider runner.
- The fake-runner assertion proves calls for:
  - `turn-end-user-planner-primitive`
  - `turn-end-user-planner-primitive-followup`
- Serialized API responses omit raw prompts, provider bodies, schema/key strings, and sentinels.

## In-App Browser Smoke

- URL: `http://127.0.0.1:5243/trip?live=1`.
- Configuration was sanitized and key-file based: live model enabled, planner primitive enabled, provider `openai`, model `gpt-4.1-mini`, key loaded from `OPENAI_API_KEY_FILE`. No key value was logged.
- Blazor Server circuit still reported disconnected after the first live turn:
  - `data-browser-circuit-state="browser_circuit_disconnected"`
  - reconnect modal text showed rejoin failure.
- Live interaction no longer depends on the circuit for provider turns:
  - `data-browser-transport="http_api"`
  - `data-http-session-id="planning-http-session-..."`

## First Turn Result

- Prompt submitted from the page through the HTTP transport.
- Real provider request attempted server-side.
- Observed markers:
  - `data-selected-provider="openai"`
  - `data-provider-request-state="attempted"`
  - `data-provider-outcome="planner_model_accepted"`
  - `data-validated-turn-source="live_provider_candidate"`
  - `data-validated-turn-outcome="awaiting_user_input"`
  - `data-harness-validation-state="awaiting_user_input"`
  - `data-primitive-renderer="form"`
  - `data-primitive-instance-id="primitive-trip-basics-form"`
  - `data-deterministic-mode="live-model-attached"`

## Answer / Second Turn Result

- The page rendered one HTTP-backed submit button scoped under `data-http-primitive-turn`.
- Answer submission posted to `/api/planning/session/{sessionId}/answer`.
- Real second provider runner invocation observed through returned state:
  - `data-provider-request-state="second_turn_attempted"`
  - `data-provider-outcome="planner_model_accepted"`
  - `data-live-turn-attempt-count="2"`
  - `data-live-second-turn-state="attempted"`
  - `data-browser-transport="http_api"`
  - `data-final-state="awaiting_user_input"`
- Raw sentinel scan was clean.

## Status

READY_WITH_BROWSER_TRANSPORT_REPAIR:

- The Blazor circuit remains unstable in the in-app browser during live provider turns.
- The product path no longer depends on that circuit for live model/harness turns.
- The browser can submit a live prompt and a live answer through server-side HTTP session endpoints, with provider work and validation remaining on the server.
- No fallback DOM transcript mutation was counted as success.

## Sanitization

No raw prompt text, raw completion, provider body, API key, credential, approval token, hold/booking/payment reference, candidate display sentinel, raw exception text, or secret sentinel was rendered in observed DOM markers or committed report. The report records only fixed outcome codes, provider/model names, trusted ids, and sanitized status.
