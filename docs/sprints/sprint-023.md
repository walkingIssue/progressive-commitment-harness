# Sprint 023 - Dynamic Model Authored Forms And Turn Rendering

Coordinator: Collin

Dispatch base: pending at sprint start.

## Objective

Fix the Sprint 022 semantic failure.

Sprint 022 proved a server-side HTTP transport can reach a real provider and a second server turn, but it did not prove the product is dynamically driven by model output. The current live path still flattens model responses into static UI:

- the provider prompt omits the raw starter task and sends only a prompt digest plus manifest metadata;
- each live turn starts from a fresh synthetic `TripSession`;
- the UI adapter discards model-authored labels/prompts/options;
- hard-coded defaults such as `Japan`, `2027-04-01`, `balanced`, and `comfortable` are injected into the rendered form;
- the task rail is generic static UI state, not model/harness task decomposition;
- the second turn receives field ids, not submitted values plus updated harness state.

Sprint 023 must make the live planner dynamic:

```text
user prompt
  -> server-side planning session
  -> persistent harness planning context
  -> context/tool search inputs for the model
  -> model-authored primitive/form invocation
  -> harness validation and sanitization
  -> UI renders the validated primitive content exactly
  -> user answer/card click becomes a normal answer DTO
  -> harness updates session state
  -> second model turn receives updated state and answer values
  -> next validated turn differs according to the actual interaction
```

Cards, decks, and mood backdrops are renderers. They do not change the primitive contract. A visual card click must produce the same answer DTO that a normal HTML form submit would produce.

## Current Failure Evidence

Coordinator reproduced the bug through the local HTTP session API with two different prompts:

1. `Plan a weird food-first Osaka trip with late night ramen, markets, and no temples.`
2. `Plan a quiet Iceland hiking trip focused on glaciers, hot springs, and early nights.`

Both produced:

- `PrimitiveTitles: Trip basics`
- `FieldIds: purpose,destination_country,start_date,end_date,pace,budget`
- `FieldValues: purpose=trip-planning,destination_country=Japan,start_date=2027-04-01,pace=balanced,budget=comfortable`
- `Tasks: Answer live planner form | Generate planning options`

That is not acceptable live behavior.

Primary code failure points:

- `src/Pch.Providers/PlannerPrimitives/PlannerPrimitiveRunner.cs`
  - `CreateSanitizedProbe` does not include the runtime prompt or contextual state.
- `src/Pch.Providers/PlannerPrimitives/PlannerPrimitiveDtos.cs`
  - `RuntimePrompt`, `Label`, and `PromptText` are `JsonIgnore`; runtime prompt should remain non-persisted, but it must still be used to build the server-side provider request.
- `src/Pch.UI/Features/EndUserChat/PlanningSessionService.cs`
  - `CreatePrimitiveSession()` creates fresh synthetic state per turn.
  - `FixedLabel`, `FixedPrompt`, and `DefaultAnswers` override model output.
  - `ProjectForm` collapses model primitives into one hard-coded `primitive-trip-basics-form`.
  - `TaskFromRef` and `LivePrimitiveTasks` create static task rail text.
  - `SecondTurnPrompt` sends only field ids, not submitted values or current graph state.

Sprint 023 passes only when these failure modes are removed from live mode.

## Non-Negotiable Rules

- Provider/model calls happen server-side only.
- Browser JavaScript may post prompt/answer DTOs and render sanitized DTOs. It may not build prompts, hold keys, call providers, or own model/tool logic.
- The live path must not use deterministic seeded cards/forms/options.
- The model must receive enough transient context to build an appropriate form for the actual user task.
- Raw user prompt may be sent to the configured model as transient provider input, but must not be persisted in committed artifacts, DTO logs, or DOM markers.
- Safe model-authored display text may be rendered after validation and sanitization.
- Unsafe model-authored display text must be blocked or redacted with fixed failure codes.
- Visual cards/decks are UI renderers over validated primitives. They must submit normal primitive answer DTOs.
- Booking, payment, and real hold execution remain mocked or approval-blocked.
- Real OpenAI/OpenRouter inference is explicitly allowed for manual and in-context browser testing. Required automated tests remain deterministic/offline.

Known key files:

- OpenAI: `C:\Users\Bartek\Documents\Playground\openai_key.txt`
- OpenRouter: `C:\Users\Bartek\Documents\Playground\openrouter.txt`
- Groq: `C:\Users\Bartek\Documents\Playground\groq_key.txt`
- xAI/Grok: `C:\Users\Bartek\Documents\Playground\grok_key.txt`

## Target Data Flow

```text
Browser /trip?live=1
  POST /api/planning/session/start { prompt, selectedModelRole }

PlanningSessionStore
  owns PlanningSessionContext
  owns TripSession
  owns current graph revision
  owns last validated turn
  owns sanitized live run log ids

PlanningContextBuilder
  receives raw prompt transiently
  extracts safe prompt intent summary for harness state
  builds current stage/state snapshot
  attaches available primitive manifest
  attaches tool/search/booking context summaries when available

Provider PlannerPrimitiveRunner
  receives transient runtime prompt and sanitized planning context
  receives manifest of allowed primitive definitions
  may emit:
    assistant_message
    composite_form
    text_input / textarea / date_range / select / multiselect
    candidate_deck
    task_list / task_group
    tool_search_request
    tool_gap_request

Harness PlannerPrimitiveValidator
  validates primitive ids, field paths, answer schemas, graph revision,
  ownership, mood/media tokens, task refs, search/tool requests, and safe text

ValidatedTurnView
  preserves safe model labels/prompts/options/task titles
  redacts or blocks unsafe fields
  carries renderer-neutral primitive data

UI
  renders primitive as plain form, card deck, timeline item, or task rail item
  card click builds PrimitiveAnswerDto
  form submit builds the same PrimitiveAnswerDto

Answer
  POST /api/planning/session/{sessionId}/answer { primitiveInstanceId, fieldValues }
  updates planning context
  second model turn receives current structured state and submitted values
```

## Canonical Primitive Abstraction

The core primitive is not a visual card. It is a validated interaction contract.

Minimum primitive fields:

- `primitive_id`
- `instance_id`
- `schema_version`
- `field_path`
- `answer_schema`
- `label`
- `prompt`
- `help_text`
- `options`
- `default_value`
- `mood_token`
- `media_token`
- `task_refs`
- `evidence_refs`
- `tool_context_refs`
- `validation_rules`
- `renderer_hints`

Renderer hints are advisory only. The UI may render a `single_select` as:

- radio buttons;
- a select;
- a card group;
- a swipe deck;
- compact chips.

Every renderer must submit the same answer shape.

Example:

```json
{
  "primitive_id": "single_select",
  "instance_id": "travel-style-choice",
  "field_path": "/mission/travel_style",
  "label": "Choose the feel of the trip",
  "prompt": "For Osaka, should this lean food-first, culture-first, or slow restorative?",
  "answer_schema": {
    "kind": "single_select",
    "required": true,
    "options": ["food_first", "culture_first", "slow_restorative"]
  },
  "options": [
    {
      "id": "food_first",
      "label": "Food-first nights",
      "summary": "Markets, ramen lanes, and late snacks.",
      "mood_token": "lively_food",
      "media_token": "lively_food"
    }
  ]
}
```

The model may author the labels/summaries/options. The harness validates ids, field path, schema, option count, text safety, mood/media tokens, and stage eligibility.

## Stage Plan

### Stage 0 - Freeze The Existing Failure

Owner: coordinator before dispatch, then all lanes maintain tests.

Why:

The current code can claim `planner_model_accepted` while rendering static Japan defaults. That must become a failing regression test before repairs begin.

How:

- Add a focused live-path regression test with a fake provider runner returning two different primitive envelopes for two different prompts.
- Add an HTTP API-level deterministic test that starts two sessions with different prompts and asserts:
  - primitive labels differ;
  - field defaults differ where model output differs;
  - task rail titles differ;
  - no hard-coded `Japan`/`2027-04-01` appears unless the model output included those exact safe values.
- Add a source-level guard test or grep-style test that live projection does not call `FixedLabel`, `FixedPrompt`, `DefaultAnswers`, or `CreatePrimitiveSession` for every turn.

Tests:

- `tests/Pch.UI.Tests/PlanningSessionDynamicRegressionTests.cs`
- `tests/Pch.Providers.Tests/PlannerPrimitivePromptContextTests.cs`
- `tests/Pch.Harness.Tests/PlannerPrimitiveValidationDynamicTests.cs`

Expected result:

- These tests fail on current `main`.
- Worker branches must make them pass by changing architecture, not by changing assertions.

Acceptance:

- Current static behavior is captured as a named failure in test output and live report.

### Stage 1 - Harness Dynamic Primitive Contract

Ticket: `sprint-023/harness-dynamic-primitive-contract`

Lane: Shellby

Why:

The harness must define what model-authored forms mean independently of UI visuals. If the primitive is not a protocol, the UI will keep smuggling fake state back in.

How:

- Extend or replace `PlannerPrimitiveContracts` with richer model-authored primitive fields:
  - safe display text fields;
  - option DTOs;
  - default value DTOs;
  - validation rules;
  - task list/task group primitives;
  - tool search request primitive;
  - tool result context reference primitive;
  - renderer hint metadata.
- Add `PlanningSessionContext` or harness-owned equivalent that persists:
  - `TripSession`;
  - graph revision;
  - accepted facts;
  - submitted primitive answers;
  - validated turn history summaries;
  - available contextual tool results.
- Add `PlannerTurnContextBuilder`:
  - accepts transient raw prompt;
  - builds model input context with raw prompt for provider use;
  - builds sanitized state summary for logs/DTOs;
  - includes submitted answer values after validation.
- Add `PlannerPrimitiveValidator` support for:
  - model-authored labels/prompts/options after sanitization;
  - per-field answer defaults;
  - option ownership;
  - task rail refs;
  - tool search requests;
  - tool result refs;
  - stage eligibility;
  - graph revision continuity.

Data flow:

```text
PlanningSessionContext
  -> PlannerToolManifestCompiler
  -> PlannerTurnContextBuilder
  -> provider proposal
  -> PlannerPrimitiveValidator
  -> ValidatedTurnView
  -> apply answer
  -> updated PlanningSessionContext
```

Failure points and valid responses:

- Unknown primitive id -> `primitive_not_supported`.
- Known primitive not allowed at current stage -> `primitive_not_allowed_for_stage`.
- Unknown field path -> `field_path_not_allowed`.
- Unsafe label/prompt/option -> `primitive_text_redacted` or `primitive_validation_blocked`.
- Unknown mood/media token -> token replaced with `neutral` or blocked according to primitive policy.
- Stale graph revision -> `stale_graph_revision`.
- Tool request outside allowed tool list -> `tool_not_allowed`.
- Option count exceeds manifest max -> `primitive_option_limit_exceeded`.
- Required answer missing -> `answer_schema_invalid`.
- Answer value not in options -> `answer_value_not_allowed`.

Tests:

- Unit test: safe model-authored labels/options survive validation.
- Unit test: unsafe labels/prompts/options are blocked/redacted and raw sentinel absent.
- Unit test: two different model primitive proposals produce different validated turn views.
- Unit test: answer DTO updates persistent planning context.
- Unit test: second turn context contains submitted answer values and accepted facts.
- Unit test: task list primitive validates and becomes task rail refs.
- Unit test: tool search request validates only when tool is allowed.
- Mutation test: provider-blocked and validation-blocked proposals do not mutate session state.

Acceptance:

- Harness can validate and preserve safe dynamic primitive content.
- Harness can apply answer values into a persistent planning context.
- Harness can build a second turn context from updated state.
- No UI or provider dependency in harness lane.

### Stage 2 - Provider Dynamic Form Build Runner

Ticket: `sprint-023/provider-dynamic-form-builder-runner`

Lane: Kaneki

Why:

The provider runner currently asks the model to choose primitive ids from a manifest but does not provide the actual planning task. That makes generic output inevitable. The model needs a server-side form-build tool request with user task, harness state, allowed primitives, and safe context/tool results.

How:

- Replace `CreateSanitizedProbe` with a proper provider prompt builder:
  - system: "You are building the next validated planner interaction."
  - user: transient raw user task for the current turn;
  - context: sanitized harness state summary;
  - answer memory: submitted answer values from previous turns;
  - allowed primitive manifest;
  - allowed field paths;
  - allowed tool/search requests;
  - allowed mood/media tokens;
  - output schema.
- Keep raw prompt as runtime-only input:
  - can be sent to provider;
  - cannot be serialized into log rows, reports, DTOs, or DOM.
- Extend strict JSON schema for:
  - primitive labels/prompts/help text;
  - options;
  - default values;
  - task decomposition;
  - tool search request;
  - tool gap request;
  - renderer hints.
- Add a bounded repair pass that includes only sanitized schema failure explanation.
- Add provider-local evaluator that records:
  - provider;
  - model;
  - request id;
  - duration;
  - response length;
  - primitive count;
  - task count;
  - option count;
  - output kind;
  - fixed failure class;
  - no raw prompt/body/completion.

Tool/context support:

- Add a provider-neutral `PlannerContextToolResult` mirror for future web/search/booking context:
  - tool id;
  - result id;
  - category;
  - title/summary after sanitization;
  - source class;
  - freshness;
  - evidence refs.
- For Sprint 023, if real web/search/booking adapters are not ready, implement deterministic server-side fake context only behind a clearly named `mock_context_provider` and do not present it as live web search.
- If real search is configured later, it must feed sanitized tool results into the same context shape.

Failure points and valid responses:

- Provider timeout -> `planner_model_timeout`.
- Empty content -> `planner_model_empty_content`.
- Malformed JSON -> `planner_model_malformed_json`.
- Schema invalid -> `planner_model_schema_invalid`.
- Unsafe text -> `planner_model_unsafe_text`.
- Unknown primitive -> `planner_model_unsupported_primitive`.
- Unknown field path -> `planner_model_field_path_not_allowed`.
- Tool request not allowed -> `planner_model_tool_not_allowed`.
- Rate limited -> `planner_model_rate_limited`.
- API key missing -> `planner_model_key_missing`.
- Provider unavailable -> `planner_model_provider_unavailable`.

Tests:

- Prompt builder includes runtime prompt in provider request body but not in log rows.
- Prompt builder includes submitted answer values for second turn.
- Prompt builder includes context/tool results when present.
- Accepted OpenAI-shaped fake completion with dynamic labels/options maps to provider result.
- Unsafe model text is rejected or redacted before accepted persistence.
- 429/4xx/5xx/timeout/malformed/empty/schema-invalid classification tests.
- Real provider direct smoke:
  - OpenAI `gpt-4.1-mini`;
  - OpenRouter configured model;
  - at least one accepted dynamic primitive envelope from either provider;
  - record sanitized request id, model, response length, primitive count, task count.

Real UI testing spike support:

- Provide a small CLI/test helper or documented command that posts two live prompts through the runner and emits sanitized comparison:
  - prompt hash;
  - primitive ids;
  - labels;
  - field paths;
  - task titles;
  - option ids;
  - provider/model/request id.

Acceptance:

- The provider runner can produce prompt-specific primitive/form envelopes.
- Direct live smoke obtains at least one accepted prompt-specific primitive/form envelope.
- Runtime prompt is used for inference but absent from committed logs.

### Stage 3 - Server Planning Session Integration

Ticket: `sprint-023/server-dynamic-planning-session`

Lane: Sarah with Shellby/Kaneki integration heads when ready.

Why:

The UI-facing service currently recreates state and maps everything into static forms. The server session must own actual context and render exactly what the harness validates.

How:

- Replace `CreatePrimitiveSession()` per turn with a persistent `PlanningSessionContext` stored in `PlanningSessionStore`.
- Store per HTTP session:
  - `TripSession`;
  - graph revision;
  - validated turn history;
  - submitted answers;
  - task rail state;
  - timeline refs;
  - sanitized provider diagnostics.
- `StartAsync`:
  - creates context from user prompt;
  - compiles manifest;
  - builds provider request with transient raw prompt;
  - runs provider;
  - validates provider output;
  - projects validated turn without static replacement.
- `SubmitAnswer`:
  - validates answer against the exact primitive instance;
  - applies answer to context;
  - compiles fresh manifest/context;
  - sends submitted values and current state to provider;
  - validates second provider output;
  - returns next validated turn.
- Remove live-mode dependency on:
  - `FixedLabel`;
  - `FixedPrompt`;
  - `DefaultAnswers`;
  - `LivePrimitiveTasks`;
  - static `primitive-trip-basics-form` as the only live form id.
- Keep deterministic fixtures only behind explicit deterministic mode.

Data flow:

```text
PlanningSessionStore[sessionId]
  -> StartAsync(prompt)
  -> PlanningSessionContext(raw prompt transient, safe state persistent)
  -> provider dynamic primitive result
  -> harness validated turn
  -> UI DTO

PlanningSessionStore[sessionId]
  -> AnswerAsync(answer)
  -> validate answer against stored turn
  -> update PlanningSessionContext
  -> provider second turn request with submitted values
  -> next validated turn
```

Failure points and valid responses:

- Unknown HTTP session -> `planning_session_unknown`.
- Answer for unknown primitive -> `primitive_instance_unknown`.
- Answer missing required value -> `answer_validation_failed`.
- Answer value not in model-provided options -> `answer_value_not_allowed`.
- Stale graph revision -> `stale_graph_revision`.
- Provider accepted but harness blocked -> `harness_validation_blocked`.
- Provider failed -> provider-specific fixed code and provider failure primitive.
- Browser transport disconnected -> use HTTP API and mark `browser_transport=http_api`, not fake UI state.

Tests:

- Two different fake provider responses produce different API JSON and different UI DTOs.
- Two different starter prompts are passed to fake provider and can influence output.
- Answer values are passed to second fake provider invocation.
- Session state persists across answer submit.
- Static defaults do not appear in live mode unless provider returned them.
- Deterministic mode still uses deterministic fixtures.
- Unknown session and stale primitive tests.
- Raw prompt absent from serialized API response and DOM-facing DTO.

Acceptance:

- Live session API returns prompt-specific dynamic primitives.
- Answer submission triggers second provider request with submitted values.
- No static live projection path remains.

### Stage 4 - UI Renderer Abstraction And Dynamic Task Rail

Ticket: `sprint-023/end-user-dynamic-primitive-ui`

Lane: Sarah

Why:

The UI should not know whether a primitive came from a plain form or a card deck. It should render validated primitive DTOs and convert interactions back into answer DTOs.

How:

- Make `PrimitiveRenderer` fully data-driven:
  - assistant message primitive;
  - text input;
  - textarea;
  - date range;
  - single select;
  - multi select;
  - ranked choice;
  - candidate deck;
  - confirmation;
  - tool search notice;
  - provider failure.
- Add `PrimitiveAnswerBuilder`:
  - normal HTML input submit -> answer DTO;
  - card click -> same answer DTO;
  - deck swipe/select -> same answer DTO;
  - confirmation button -> same answer DTO.
- Render safe model-authored:
  - form title;
  - prompt;
  - help text;
  - field labels;
  - options;
  - task titles;
  - mood/media tokens.
- Use local media manifest for `media_token`.
- If `media_token` is absent or invalid, use fallback media but do not change primitive semantics.
- Task rail must come from validated task primitives/task refs, not static arrays.
- Timeline/evidence rail must use validated turn ids and answer ids.

Data flow:

```text
ValidatedTurnView DTO
  -> PrimitiveRenderer
  -> NormalFormRenderer or CardDeckRenderer
  -> PrimitiveAnswerBuilder
  -> POST answer DTO
```

Failure points and valid responses:

- Unsupported renderer hint -> render guarded unsupported primitive notice.
- Missing media token -> fallback media marker.
- Invalid answer from UI -> field-level validation error.
- HTTP answer fails -> fixed transport/server error notice.
- Provider blocked -> provider failure primitive.

Tests:

- Snapshot/service test: model-provided labels/options render unchanged after sanitization.
- Form submit and card click produce identical answer DTO shape.
- Dynamic task rail changes with provider task primitive.
- Prompt A and Prompt B API fixtures render different visible form/task content.
- Raw prompt/provider/key/secret sentinels absent from DOM.
- Deterministic fixture cards absent in live mode.

Real in-context browser testing spikes:

1. Debug app with OpenAI:
   - `http://127.0.0.1:<port>/trip?live=1`
   - prompt: Osaka food-first/no temples.
   - expected:
     - `data-provider-request-state="attempted"`;
     - `data-provider-outcome="planner_model_accepted"`;
     - rendered form/deck text references Osaka/food/markets or an equivalent safe model-authored interpretation;
     - does not render static `destination_country=Japan` unless model returned it because prompt asked Japan.
2. Same app, new session:
   - prompt: Iceland hiking/glaciers/hot springs.
   - expected:
     - rendered form/deck/task content differs from Osaka session;
     - task rail differs or at least task labels/options differ;
     - no static Japan defaults.
3. Answer first form/card:
   - submit values.
   - expected:
     - `data-live-turn-attempt-count >= 2`;
     - second provider turn attempted;
     - second turn text/options reflect submitted answer values;
     - timeline has answer item;
     - raw sentinel scan clean.
4. Card renderer parity:
   - same primitive rendered as cards where applicable.
   - click option.
   - expected answer DTO is equivalent to selecting the option in a form.
5. Browser transport fallback:
   - if Blazor disconnects, HTTP API path continues.
   - expected:
     - `data-browser-transport="http_api"`;
     - no DOM-only fake transcript mutation;
     - server log shows real API request.

Acceptance:

- User can see model-authored dynamic content in the UI.
- Different starter prompts produce different validated visible content.
- Task rail is not static.
- Card clicks and form submits use the same validation/submission contract.

### Stage 5 - Coordinator Integration And Truth Gate

Owner: Collin

Why:

This sprint cannot be accepted because "provider accepted" or "DOM marker changed." It must prove that dynamic provider output reaches the user and continues across turns.

How:

- Merge harness/provider/UI heads into an integration branch or `main` only after all lane tests pass.
- Run root verification:
  - `npm run build:ui` from `src/Pch.UI`;
  - `dotnet test tests\Pch.Core.Tests\Pch.Core.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`;
  - `dotnet test tests\Pch.Harness.Tests\Pch.Harness.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`;
  - `dotnet test tests\Pch.Providers.Tests\Pch.Providers.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`;
  - `dotnet test tests\Pch.UI.Tests\Pch.UI.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`;
  - `dotnet build --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nodeReuse:false`;
  - `git diff --check`.
- Run real in-context browser smoke with OpenAI.
- Run real in-context browser smoke with OpenRouter if provider is healthy.
- Record live reports under `docs/live-failure-reports/sprint-023-*.md`.

Required comparison data:

- prompt hash;
- provider;
- model;
- request id if safe;
- duration bucket;
- response length;
- primitive count;
- task count;
- option count;
- first-turn primitive ids;
- first-turn safe visible labels;
- first-turn task titles;
- answer field ids;
- second-turn primitive ids;
- second-turn safe visible labels;
- fixed failure code if blocked.

Acceptance:

- Prompt A and Prompt B produce different validated UI content.
- At least one provider completes a two-turn live browser path.
- If both providers fail, the sprint is not READY; it is BLOCKED with specific fixed provider/harness/browser failure codes and sanitized logs.
- No static live fallback may be counted as pass.

## Worker Dispatch

### Shellby - Harness Dynamic Primitive Contract

Branch: `sprint-023/harness-dynamic-primitive-contract`

Deliver:

- core/harness primitive DTO updates;
- persistent planning context;
- turn context builder;
- validator support for dynamic labels/options/defaults/tasks/tool requests;
- tests and live failure report.

Do not edit:

- `src/Pch.UI/**`;
- `src/Pch.Providers/**`.

### Kaneki - Provider Dynamic Form Builder Runner

Branch: `sprint-023/provider-dynamic-form-builder-runner`

Deliver:

- provider prompt/context builder that uses transient runtime prompt;
- strict schema for dynamic primitives/tasks/options/tool requests;
- OpenAI/OpenRouter live smokes;
- sanitized live diagnostics;
- tests proving prompt and submitted answers influence model request.

Do not edit:

- `src/Pch.UI/**`;
- `src/Pch.Harness/**`, except docs if required for mapping notes.

### Sarah - End-User Dynamic Primitive UI

Branch: `sprint-023/end-user-dynamic-primitive-ui`

Deliver:

- server planning session integration with persistent context;
- UI renderer driven by validated primitive DTOs;
- card/form answer parity;
- dynamic task rail;
- HTTP transport browser fallback retained;
- real in-context browser testing with OpenAI and, if healthy, OpenRouter;
- tests and live report.

Do not fake:

- no static live form defaults;
- no deterministic card deck in live mode;
- no "accepted" marker without real provider/harness validation;
- no browser-only transcript mutation.

## Required Live Browser Scenarios

### Scenario A - Osaka Food Trip

Prompt:

```text
Plan a weird food-first Osaka trip with late night ramen, markets, and no temples.
```

Expected:

- provider request attempted;
- provider accepted or fixed provider failure;
- if accepted, visible content includes safe prompt-specific concepts such as Osaka, food, ramen, markets, late night, no temples, or equivalent model-authored planning interpretation;
- rendered fields/options/tasks are not identical to Scenario B;
- no static Japan/date defaults unless provider returns them.

### Scenario B - Iceland Quiet Hiking Trip

Prompt:

```text
Plan a quiet Iceland hiking trip focused on glaciers, hot springs, and early nights.
```

Expected:

- provider request attempted;
- provider accepted or fixed provider failure;
- if accepted, visible content includes safe prompt-specific concepts such as Iceland, hiking, glaciers, hot springs, quiet pace, early nights, or equivalent model-authored planning interpretation;
- rendered fields/options/tasks are not identical to Scenario A.

### Scenario C - Answer And Continue

Action:

- Submit the first rendered primitive form or select a card option.

Expected:

- answer DTO includes actual submitted values;
- server validates answer against stored primitive instance;
- second provider request attempted;
- second provider context includes submitted values;
- UI renders second validated primitive or fixed provider/harness failure;
- live attempt count increments;
- timeline/task rail updates from validated turn data.

### Scenario D - Card/Form Equivalence

Action:

- Render a select primitive as cards.
- Click a card.

Expected:

- submitted answer DTO has the same field id/value shape as form select submit;
- harness validates it through the same answer schema;
- no special card-only state mutation.

### Scenario E - Unsafe Model Output

Provider fixture:

- label/prompt/option contains raw provider payload, API key-like value, hold ref, booking ref, approval token, CSS/HTML, or sentinel.

Expected:

- unsafe value is redacted or blocked;
- raw value absent from API response, DOM, docs, and committed logs;
- fixed code identifies the boundary.

## Exit Criteria

Sprint 023 is READY only if all are true:

- The current static prompt-insensitive behavior is gone.
- The model receives transient prompt/context server-side.
- Safe model-authored labels/prompts/options/tasks survive validation and render in the UI.
- Prompt A and Prompt B produce different visible validated content in live browser testing.
- Answer submit or card click reaches a second provider turn with submitted values.
- Task rail comes from validated model/harness turn data, not static arrays.
- Deterministic mode still exists but is explicit and cannot satisfy live acceptance.
- Root build/test checks pass.
- Live reports contain sanitized data points and exact failure classes.

Sprint 023 is BLOCKED if any are true:

- UI still renders static live form defaults after provider acceptance.
- Provider prompt still omits runtime prompt/context.
- Session state is recreated from synthetic fixtures each turn.
- Second turn receives only field ids, not answer values/current state.
- Browser smoke cannot prove dynamic prompt-specific visible content.
- The task rail remains static in live mode.

## Documentation Updates

On completion:

- Update `docs/PLAN_PROGRESS.md` with Sprint 023 result.
- Update `docs/MVP_STATUS.md` with what is now live, what remains mocked, and what remains missing.
- Add/refresh live reports:
  - `docs/live-failure-reports/sprint-023-harness-dynamic-primitives.md`
  - `docs/live-failure-reports/sprint-023-provider-dynamic-form-builder.md`
  - `docs/live-failure-reports/sprint-023-end-user-dynamic-ui.md`
- Any claim of readiness must cite the live browser scenario outputs.
