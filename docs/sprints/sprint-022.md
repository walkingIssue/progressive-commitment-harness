# Sprint 022 - Planner Primitive Tool Library And Real Turn Service

Coordinator: Collin

Dispatch base: pending at sprint start.

## Objective

Fix the architectural hole that Sprint 021 exposed.

The product must stop treating deterministic cards, fallback DOM markers, or prebuilt UI state as proof of a live planning system. Sprint 022 introduces the missing contract:

```text
user action
  -> server-side planning session service
  -> harness state graph
  -> stage-scoped planner tool manifest
  -> real model request on the server
  -> model emits primitive/form invocations only
  -> harness validates primitives/forms
  -> UI renders validated turn view
  -> user answers/selects
  -> second server-side model request
  -> second validated turn view
```

No client-side model calls. No JavaScript prompt assembly. No fake "live" deterministic cards. No pass unless real in-context browser testing proves the server-side loop.

## Why This Sprint Exists

The current codebase has useful pieces:

- harness stage gates and action validation;
- provider live runners with sanitized diagnostics;
- UI components for forms, decks, timelines, and approval plates;
- media assets and mood styling;
- many redaction and deterministic boundary tests.

Those pieces do not yet form the product spine. The missing layer is a canonical planner primitive/tool library. The model should not improvise UI, and the UI should not seed cards that look like model output.

The correct abstraction is:

- Core defines primitive and form contracts.
- Harness decides which primitives are allowed for the current graph/stage.
- Provider receives a manifest and emits only primitive/form invocations.
- Harness validates and compiles those invocations into a safe turn view.
- UI renders validated primitives with a local component/media/theme library.

This sprint is not optional cleanup. It is the foundation required before the live model can drive the end-user planner.

## Non-Negotiable Live Testing Policy

Workers are expected to connect to real models during manual/in-context browser testing when keys are present. OpenAI and OpenRouter inference spend is explicitly allowed for Sprint 022. Groq and xAI/Grok-compatible providers are also allowed when configured.

Known key files on this machine:

- OpenAI: `C:\Users\Bartek\Documents\Playground\openai_key.txt`
- OpenRouter: `C:\Users\Bartek\Documents\Playground\openrouter.txt`
- Groq: `C:\Users\Bartek\Documents\Playground\groq_key.txt`
- xAI/Grok: `C:\Users\Bartek\Documents\Playground\grok_key.txt`

Required automated tests remain deterministic/offline. Manual and browser smoke must use real providers when configured.

Rules:

- Model calls happen server-side only.
- Never place API keys, provider request bodies, raw completions, raw prompts, or provider SDK logic in browser JavaScript.
- Use bounded live budgets. Default target: 10-30 live requests per lane, stop after repeated same-class failures and report them.
- Do not silently paid-fallback from one provider to another.
- Real hold/book/pay/spend remains mocked or approval-blocked.
- Persist sanitized live logs only: provider, model, request id if safe, duration, response length, fixed outcome, failure class, manifest version, primitive counts, harness validation code.
- Raw local debugging logs may exist only under gitignored `artifacts/live-runs/` and only when an explicit debug flag is set. They must never be committed.

## Read First

- `docs/PLAN.md`
- `docs/PLAN_PROGRESS.md`
- `docs/MVP_STATUS.md`
- `docs/sprints/sprint-021.md`
- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/end-user-progressive-history.md`
- `docs/providers/live-turn-runner-diagnostics.md`
- `docs/providers/live-mission-proposal-runner.md`
- `docs/evals/sanitized-artifacts.md`
- `docs/live-failure-reports/sprint-021-end-user-live-multiturn-ui.md`
- `docs/live-failure-reports/sprint-021-provider-live-turns.md`
- `docs/live-failure-reports/sprint-021-harness-live-multiturn.md`

## Scope Boundary

In scope:

- primitive/form definitions;
- composite form authoring from primitives;
- tool search/tool gap request shape;
- mood/media token validation and mapping;
- harness-generated stage-scoped manifests;
- server-side planning session service;
- provider strict-schema primitive runner;
- UI primitive renderer and form builder;
- real two-turn browser smoke with provider calls;
- docs consolidation against MVP status.

Out of scope:

- real booking/payment/hold execution;
- broad itinerary quality optimization;
- live stock/scraper media ingestion;
- full deployment hardening;
- replacing every existing deterministic harness test.

## Stage Plan

### Stage 0 - Truth Gate And Baseline Audit

Estimate: 0.5 day coordinator plus lane startup time.

How:

- Start from exact `origin/main` base.
- Each lane reads the sprint plan and relevant failure reports.
- Record whether current `/trip` can run a healthy Blazor/server interaction in the in-app browser.
- Record whether provider keys are present through key-file checks only, not by printing values.

Why:

- Sprint 021 proved HTTP 200 and DOM markers are not enough. Every lane must begin with a shared definition of "interactive".

Required baseline checks:

- `window.Blazor === true` or equivalent framework-health signal for Blazor Server.
- reconnect modal absent.
- `data-error-code` absent or not a circuit-disconnect code.
- server-side event changes state after a click.
- if unhealthy, report `browser_circuit_failed` and continue with standalone Chrome only as a secondary diagnostic, not as final acceptance.

Valid responses:

- `baseline_browser_healthy`
- `baseline_browser_circuit_failed`
- `baseline_provider_config_present`
- `baseline_provider_config_missing`

Possible failures:

- stale server port;
- disconnected Blazor circuit;
- missing key files;
- app starts without static assets;
- provider config not passed into child process.

### Stage 1 - Primitive And Form Contract

Estimate: 1.5-2 days.

How:

- Add versioned primitive DTOs in Core or the lowest shared contract layer.
- Define primitive ids, schema, answer schema, allowed value constraints, mood hooks, media hooks, accessibility hints, and stage eligibility.
- Define composite `PlannerFormDefinition` as an ordered tree/list of primitive instances.
- Define `PlannerToolManifest` as the model-facing allowed tool library for a turn.
- Define `PlannerToolSearchRequest` and `PlannerToolGapRequest` for cases where the model cannot express the needed interaction with current tools.

Why:

- The model needs an explicit tool library. The UI needs stable renderer keys. The harness needs something it can validate before mutation. This belongs below providers and UI so both can depend on it without leaking implementation details.

Primitive minimum set:

- `assistant_message`
- `text_input`
- `textarea`
- `date_range`
- `number_range`
- `budget_range`
- `single_select`
- `multi_select`
- `ranked_choice`
- `candidate_deck`
- `confirmation_question`
- `approval_gate`
- `availability_preview`
- `evidence_strip`
- `timeline_anchor`
- `edit_patch_request`
- `tool_search_request`
- `tool_gap_request`

Mood/media minimum set:

- `reflective_culture`
- `soft_nature`
- `lively_food`
- `calm_morning`
- `restorative_downtime`
- `logistics`
- `family_support`
- `neutral`

Valid responses:

- accepted primitive instance;
- accepted composite form;
- rejected unsupported primitive;
- rejected invalid mood token;
- rejected unsafe media reference;
- tool search request accepted as non-mutating;
- tool gap request accepted as non-mutating/review-required.

Possible failures:

- primitive id unknown;
- schema version mismatch;
- primitive not allowed in current stage;
- field path not allowed;
- answer schema mismatch;
- model attempts raw CSS/image URL;
- model sends arbitrary HTML/prose as UI structure.

### Stage 2 - Harness Manifest Compiler And Graph Validation

Estimate: 2-3 days.

How:

- Build a harness-owned planning graph if the existing session state cannot represent dependencies clearly.
- Add a manifest compiler that converts current graph/stage into a bounded `PlannerToolManifest`.
- Add primitive validator that validates a model-produced turn against:
  - manifest version;
  - graph revision;
  - stage;
  - allowed primitive ids;
  - allowed field paths;
  - allowed candidate/slot/task ids;
  - mood/media token allowlists;
  - approval/spend restrictions.
- Add a compiler from validated primitive instances to a UI-agnostic `ValidatedTurnView`.

Why:

- The harness must be the authority. The model proposes; the harness accepts, blocks, or asks for repair. UI should never render an unvalidated model-authored form.

Data flow:

```text
TripSession/PlanningGraph
  -> PlannerManifestCompiler
  -> PlannerToolManifest
  -> provider model
  -> PlannerPrimitiveInstance[]
  -> PlannerPrimitiveValidator
  -> ValidatedTurnView
  -> PlanningSessionService
```

Valid responses:

- `primitive_turn_accepted`
- `awaiting_user_input`
- `tool_search_requested`
- `tool_gap_review_required`
- `primitive_validation_blocked`
- `stale_graph_revision`
- `approval_required`
- `provider_model_blocked`

Possible failures:

- stale graph revision;
- primitive not allowed for stage;
- candidate not owned by slot;
- task id not in graph;
- user answer does not satisfy answer schema;
- edit invalidates downstream nodes;
- approval gate attempted without approval.

### Stage 3 - Provider Primitive Runner

Estimate: 2 days.

How:

- Add provider-local runner that accepts sanitized model input plus `PlannerToolManifest`.
- Generate strict JSON schema from the manifest or a bounded generic schema with manifest ids embedded in prompt/context.
- Prefer OpenAI or OpenRouter models that can produce reliable structured JSON.
- Add one repair turn for malformed JSON/schema invalid output.
- Log sanitized result rows and local debug artifacts.

Why:

- Provider code owns transport, timeouts, credit/key guards, and model id/provider diagnostics. It should not know Blazor, UI cards, or harness mutation internals.

Data flow:

```text
PlannerModelRequest
  -> provider adapter
  -> OpenAI/OpenRouter/Groq/xAI compatible completion
  -> raw completion in memory
  -> strict parse
  -> provider-local PlannerModelResult
  -> sanitized eval/log row
```

Valid responses:

- `planner_model_accepted`
- `planner_model_repaired_json`
- `planner_model_tool_search_requested`
- `planner_model_tool_gap_requested`
- `planner_model_schema_invalid`
- `planner_model_provider_timeout`
- `planner_model_provider_unavailable`
- `planner_model_rate_limited`
- `planner_model_credit_exhausted`
- `planner_model_key_missing`

Possible failures:

- empty completion;
- markdown/fenced JSON;
- malformed JSON;
- model emits unsupported primitive;
- model invents candidate ids;
- model invents URLs/CSS;
- provider timeout;
- upstream 4xx/5xx;
- rate limit;
- key file missing;
- credit guard failure.

### Stage 4 - Server-Side Planning Session Service

Estimate: 1.5-2.5 days.

How:

- Add an application/service layer or a harness-facing service class with DI registration.
- UI calls this service for session start, answer submission, candidate selection, edit request, availability preview, and approval request.
- The service owns provider runner invocation server-side.
- Browser receives only `ValidatedTurnView` and answer-submit endpoints/events.
- Remove or hard-disable client-side fallback that pretends to send live provider turns.

Why:

- Client JavaScript must not own provider calls, prompt assembly, schema decisions, key handling, or harness mutation. The browser requests data needed to render the next turn.

Data flow:

```text
Blazor UI event
  -> PlanningSessionService.Submit(...)
  -> harness manifest/state
  -> provider runner server-side
  -> harness validation/application
  -> ValidatedTurnView
  -> Blazor state/render
```

Valid responses:

- `session_started`
- `turn_rendered`
- `answer_accepted`
- `answer_validation_failed`
- `provider_blocked`
- `harness_validation_blocked`
- `browser_circuit_failed`

Possible failures:

- service not registered in DI;
- per-user session not persisted;
- concurrent turn double-submit;
- stale client revision;
- provider timeout;
- model output invalid;
- browser circuit disconnect.

### Stage 5 - UI Primitive Renderer And Form Builder

Estimate: 2-3 days.

How:

- Add a `PrimitiveRenderer` that switches on validated primitive ids.
- Add a `FormBuilder` that renders composite forms from primitive instances.
- Add component renderers for the minimum primitive set.
- Connect mood tokens to CSS variables/classes.
- Connect mood/category/media tokens to the local UI media manifest.
- Render task rail from harness task/decomposition primitives, not static UI seed data.
- Render no live card unless it exists in `ValidatedTurnView`.

Why:

- UI should be a renderer, not a planner. This keeps UI polish independent from model/harness validity and prevents fake cards from appearing before the model/harness produced them.

Data flow:

```text
ValidatedTurnView.Primitives
  -> PrimitiveRenderer
  -> concrete Blazor component
  -> user answer DTO
  -> PlanningSessionService.SubmitAnswer
```

Valid responses:

- form rendered;
- candidate deck rendered;
- user answer accepted;
- field-level validation error rendered;
- timeline anchor rendered;
- evidence strip rendered;
- missing media placeholder rendered.

Possible failures:

- unknown renderer key;
- primitive missing required UI metadata;
- invalid answer;
- media asset missing;
- accessibility label missing;
- stale turn view.

### Stage 6 - Real In-Context Browser Testing Spikes

Estimate: 1 day per integration pass, shared by lanes.

How:

- Use the in-app browser first.
- Do not count HTTP-only, DOM-only, or fallback-only smoke as pass.
- Verify framework health before every test.
- Drive actual user actions.
- Confirm server-side provider attempts and harness validation codes.
- Export sanitized traces.

Why:

- The last failure was a zombie tab. This stage exists to prevent that exact lie from recurring.

Required spike script:

1. Start app with provider config using key files.
2. Open `/trip`.
3. Verify:
   - no reconnect modal;
   - `window.Blazor === true` or equivalent;
   - root state does not contain `browser_circuit_disconnected`;
   - live mode selected.
4. Submit a real prompt.
5. Confirm:
   - provider request attempted;
   - provider/model logged;
   - model returned primitive/form envelope or fixed provider failure;
   - harness validation code present.
6. If accepted, render a model-produced form/deck.
7. Submit one user answer or option selection.
8. Confirm:
   - answer reached server;
   - graph revision changed;
   - second provider request attempted;
   - second validated turn rendered or fixed failure shown.
9. Save sanitized trace under `artifacts/live-runs/sprint-022/`.
10. Commit only sanitized summary docs, not raw provider data.

Valid pass responses:

- `live_two_turn_accepted`
- `live_first_turn_accepted_second_turn_provider_blocked`
- `live_first_turn_provider_blocked_with_specific_failure`

Invalid pass responses:

- `HTTP 200 only`;
- DOM marker changed by browser fallback;
- deterministic fallback rendered;
- provider request not attempted but UI says live;
- reconnect modal present;
- raw provider data visible in DOM.

### Stage 7 - Documentation Consolidation

Estimate: 1 day.

How:

- Update `docs/MVP_STATUS.md` against actual implementation.
- Update `docs/PLAN_PROGRESS.md`.
- Fold or reference the loose plan docs in `docs/Plan *.md`.
- Add a short architecture decision record for:
  - server-side model calls only;
  - primitive manifest as model/UI contract;
  - UI renders validated primitives only;
  - deterministic mode separated from live mode.

Why:

- The project has drifted through many sprint slices. Workers need one source of truth for what is live, deterministic, mocked, blocked, or synthetic.

Valid responses:

- current MVP status table;
- clear not-yet-working list;
- live smoke result table;
- next failure class list.

Possible failures:

- docs claim live success without browser proof;
- old deterministic docs contradict live path;
- loose addendum docs remain unreferenced.

## Lane A - Harness Primitive Manifest And Validation

Owner: Shellby

Ticket: `sprint-022/harness-planner-tool-manifest`

Branch: `sprint-022/harness-planner-tool-manifest`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/live-failure-reports/**`

How:

- Define primitive/form/manifest contracts.
- Add manifest compiler from current harness state/stage.
- Add primitive validator and validated turn compiler.
- Add graph revision/staleness handling if current `TripSession` cannot support it cleanly.
- Keep provider/UI dependencies out of harness.

Why:

- Harness must own what the model is allowed to ask the user and what the UI is allowed to render.

Estimate:

- 2-4 working days depending on whether a new graph DTO layer is needed.

Tests:

- null/malformed manifest input blocks;
- allowed primitive accepted;
- unsupported primitive blocks;
- stage-disallowed primitive blocks;
- composite form accepted;
- invalid answer schema blocks;
- stale graph revision blocks;
- tool search request accepted without mutation;
- tool gap request accepted as review-required without mutation;
- unsafe prompt/provider/credential/media/CSS/sentinel strings redacted;
- deterministic two-turn fake model fixture compiles to validated turn views.

Real In-Context Browser Testing Spike:

- After Sarah integrates, provide a debug-safe manifest summary in `/trip`:
  - manifest version;
  - allowed primitive count;
  - graph revision;
  - current stage.
- Verify in the in-app browser that the first rendered live primitive references this manifest version.
- If browser circuit is unhealthy, report `browser_circuit_failed`; do not claim pass.

Data Flow:

```text
TripSession/PlanningGraph
  -> PlannerManifestCompiler
  -> PlannerToolManifest
  -> Provider lane
  -> PlannerPrimitiveInstance[]
  -> PlannerPrimitiveValidator
  -> ValidatedTurnView
```

Possible Failures And Valid Responses:

- unknown primitive -> `primitive_not_supported`
- primitive not allowed for stage -> `primitive_not_allowed_for_stage`
- invalid field path -> `field_path_not_allowed`
- invalid answer -> `answer_schema_invalid`
- stale revision -> `stale_graph_revision`
- approval/spend primitive without gate -> `approval_required`
- unsafe media/theme token -> `primitive_metadata_redacted` or block if structural

Acceptance Criteria:

- Harness can produce a manifest for the initial trip prompt stage.
- Harness can validate one model-authored composite form.
- Harness can validate one user answer and produce the next manifest.
- Harness can block unsupported/unsafe primitives without mutation.
- Harness emits no raw prompt/provider/key/display sentinels in serialized results.

## Lane B - Provider Planner Primitive Runner

Owner: Kaneki

Ticket: `sprint-022/provider-planner-primitive-runner`

Branch: `sprint-022/provider-planner-primitive-runner`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`
- `docs/live-failure-reports/**`

How:

- Add provider-local request/result DTOs for primitive/form generation.
- Accept a sanitized manifest summary and strict output schema.
- Send server-side model requests through existing completion clients.
- Prefer OpenAI/OpenRouter first; use cheap strict-JSON-capable models.
- Add one repair turn for malformed JSON/schema invalid output.
- Add sanitized diagnostic logs and gitignored raw-local debug option.

Why:

- Provider layer should own network/model behavior and failure classification, while preserving a clean boundary from harness/UI.

Estimate:

- 2-3 working days.

Tests:

- mocked provider accepted composite form;
- mocked provider tool search request;
- mocked provider malformed JSON then repair accepted;
- mocked provider unsupported primitive blocked;
- provider timeout mapped to fixed outcome;
- provider empty content mapped to fixed outcome;
- provider schema invalid mapped to fixed outcome;
- accepted rows omit raw prompt/completion/body/key;
- rejected rows omit provider metadata/result fields.

Real In-Context Browser Testing Spike:

- Use the app or a thin local harness runner with real key files.
- Make at least one real OpenAI or OpenRouter request with a manifest that asks for a small intake form.
- Record:
  - provider/model;
  - request id if safe;
  - duration;
  - output kind;
  - primitive count;
  - parse/validation result.
- Then run through `/trip` integration once Sarah merges.

Data Flow:

```text
PlannerToolManifest + sanitized graph slice
  -> PlannerModelRequest
  -> OpenAI/OpenRouter/Groq/xAI client
  -> raw response in memory
  -> strict parser/repair
  -> PlannerModelResult
  -> harness validator
```

Possible Failures And Valid Responses:

- key missing -> `planner_model_key_missing`
- credit exhausted -> `planner_model_credit_exhausted`
- rate limit -> `planner_model_rate_limited`
- timeout -> `planner_model_timeout`
- empty content -> `planner_model_empty_content`
- malformed JSON -> repair once, then `planner_model_malformed_json`
- schema invalid -> repair once, then `planner_model_schema_invalid`
- unsupported primitive -> `planner_model_unsupported_primitive`
- provider unavailable -> `planner_model_provider_unavailable`

Acceptance Criteria:

- Provider can produce a valid primitive/form envelope from a real model at least once, or records a specific fixed failure class after bounded attempts.
- Provider never persists raw keys/prompts/bodies/completions in committed artifacts.
- Provider exposes enough diagnostics for coordinator to distinguish model failure from harness/UI failure.

## Lane C - Server Planning Service And End-User Primitive UI

Owner: Sarah

Ticket: `sprint-022/end-user-server-primitive-ui`

Branch: `sprint-022/end-user-server-primitive-ui`

Owns:

- `src/Pch.UI/**`
- service composition/DI registration needed by UI
- `tests/Pch.UI.Tests/**`
- `docs/live-failure-reports/**`

How:

- Add or consume a server-side `PlanningSessionService`.
- Register dependencies through DI; do not `new` provider/harness services in components.
- UI sends user events to the service.
- UI receives `ValidatedTurnView`.
- Add `PrimitiveRenderer` and `FormBuilder`.
- Map mood tokens to CSS classes/variables.
- Map media tokens to local image manifest.
- Remove live-mode seeded cards. Keep deterministic demo only under explicit deterministic mode.
- Task rail reads validated task/decomposition primitives, not static UI arrays.

Why:

- The client should render data, not own planning logic. This prevents key leakage, fake live UI, and primitive/schema drift.

Estimate:

- 3-4 working days.

Tests:

- service registered in DI;
- `/trip` uses service-backed state;
- model-produced form primitive renders;
- answer DTO generated from form;
- invalid answer shows validation error;
- mood token maps to CSS/media asset;
- missing media maps to placeholder;
- task rail renders from validated turn data;
- deterministic seeded cards absent in live mode;
- raw provider/schema/key strings absent from DOM/state.

Real In-Context Browser Testing Spike:

- Start app with OpenAI/OpenRouter key-file config.
- In in-app browser:
  - verify Blazor/server interaction healthy;
  - submit prompt;
  - verify provider request attempted;
  - verify model-produced form/deck rendered;
  - submit answer;
  - verify second provider request attempted;
  - verify second validated turn rendered.
- Save sanitized browser smoke report.

Data Flow:

```text
Blazor component event
  -> PlanningSessionService
  -> harness manifest
  -> provider runner
  -> harness validator
  -> ValidatedTurnView
  -> PrimitiveRenderer
  -> user answer DTO
```

Possible Failures And Valid Responses:

- browser circuit disconnected -> hard visible disconnected state, no fake fallback;
- service missing -> app startup/test failure;
- provider blocked -> provider failure primitive rendered;
- harness validation blocked -> validation notice rendered;
- answer invalid -> field-level error rendered;
- stale revision -> refresh/retry prompt rendered;
- media missing -> placeholder rendered;
- unknown primitive renderer -> `unsupported_primitive_renderer` blocked view.

Acceptance Criteria:

- Live mode renders only validated primitive turn data.
- The first live form/deck comes from the server/model/harness path, not UI seed data.
- User action reaches server and changes session graph/revision.
- In-context browser proves two-turn interaction or records a specific fixed failure.

## Lane D - Coordinator Documentation And Acceptance Gate

Owner: Collin

Ticket: `sprint-022/docs-mvp-and-acceptance-gate`

Branch: coordinator/integration branch or `main` after lane review, depending on workflow.

Owns:

- `docs/MVP_STATUS.md`
- `docs/PLAN_PROGRESS.md`
- `docs/design/**`
- `docs/live-failure-reports/**`
- final integration smoke report

How:

- Consolidate loose plan docs into MVP status/design references.
- Update MVP status with a table:
  - live;
  - deterministic;
  - mocked;
  - synthetic;
  - missing.
- Define a permanent acceptance gate for future sprints.
- Integrate lanes only after each lane provides fixed verification and live/browser report.

Why:

- The project has accumulated many partial truths. Workers need one authoritative source and a pass/fail gate that cannot be satisfied by fallback DOM markers.

Estimate:

- 1-2 working days plus integration time.

Tests/Checks:

- docs reference current sprint plan;
- no unreferenced loose plan docs remain without a status note;
- final smoke uses in-app browser first;
- final smoke records actual provider attempt and harness validation result.

Real In-Context Browser Testing Spike:

- Coordinator final run must:
  - start app with provider config;
  - open `/trip` in in-app browser;
  - verify healthy Blazor/server interaction;
  - complete one user prompt;
  - complete one model-produced form answer or selection;
  - observe second provider attempt;
  - save sanitized trace.

Acceptance Criteria:

- `docs/MVP_STATUS.md` no longer overstates live readiness.
- `docs/PLAN_PROGRESS.md` contains Sprint 022 target/result.
- Future sprint templates include real browser/provider acceptance gate.

## Cross-Lane Data Contracts

### PlannerToolManifest

Must include:

- manifest id;
- schema version;
- graph revision;
- session id;
- current stage;
- allowed primitive definitions;
- allowed field paths;
- allowed node/task/slot/candidate ids;
- allowed mood tokens;
- allowed media source classes;
- answer constraints;
- max primitive count;
- max text lengths;
- approval/spend restrictions.

### PlannerPrimitiveInstance

Must include:

- primitive id;
- primitive kind;
- instance id;
- renderer key;
- label/title/prompt fields with length bounds;
- mood token;
- media token/reference, if allowed;
- input/choice config;
- answer schema;
- dependency refs;
- evidence refs.

### ValidatedTurnView

Must include:

- turn id;
- session id;
- graph revision;
- source: live provider, deterministic demo, provider blocked, harness blocked;
- fixed outcome code;
- primitives;
- task rail items;
- timeline anchors;
- evidence refs;
- provider diagnostic summary;
- raw absence/sanitization status.

## Global Acceptance Criteria

Sprint 022 is not complete unless:

- `docs/sprints/sprint-022.md` is the source plan workers followed.
- Primitive/form contracts exist and are versioned.
- Harness can compile a stage-scoped manifest.
- Provider can call a real model with the manifest server-side.
- Harness validates model-produced primitives before UI render.
- UI renders only validated primitives in live mode.
- A user answer/selection goes back to the server and advances graph revision.
- A second provider turn is attempted in real browser/integration smoke.
- Browser smoke distinguishes healthy Blazor/server interaction from disconnected fallback.
- Deterministic demo mode is visibly and technically separate from live mode.
- No real booking/payment/hold side effects occur.
- Sanitized logs exist for live attempts.
- Raw prompts, keys, provider bodies, raw completions, credentials, approval tokens, booking/payment/hold refs, raw exception text, and secret sentinels are absent from committed artifacts and DOM.

## Explicit Non-Pass Conditions

Do not mark a lane READY if the only evidence is:

- `HTTP 200`;
- DOM marker mutation from browser fallback;
- deterministic cards rendered in live mode;
- provider request not attempted;
- reconnect modal present;
- `window.Blazor` or server interactivity unhealthy;
- single-turn only with no second-turn attempt;
- model output not tied to a manifest;
- UI card/form not tied to a validated primitive instance;
- task rail still static in live mode.

