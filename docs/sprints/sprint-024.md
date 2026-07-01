# Sprint 024 - Real Primitive Tooling And Live Turn Soak

Coordinator: Collin

Dispatch base: current `origin/main` at worker start. The coordinator dispatch message provides the exact commit hash for branch creation and verification.

## Objective

Correct the Sprint 023 live primitive quality and state-truth failures.

Sprint 023 proved the model can produce prompt-sensitive primitive structures and reach a second provider turn. It did not prove that the primitive tool library is good enough, nor that the UI faithfully renders every validated primitive type.

Current browser evidence from `http://127.0.0.1:5260/trip?live=1`:

- The server returned a field with `rendererKey = select` and `allowedValues = confirm, correct, defer`.
- The UI rendered that field as a plain text input.
- The model defaulted dates, pace, and confirmation-style questions into generic text fields in some runs.
- The task rail displayed `Live provider blocked` even while the server session reported `providerOutcome = planner_model_accepted`, `providerRequestState = second_turn_attempted`, `blockedReason = null`, and `live-turn-attempt-count = 2`.
- The task rail is not yet a real task decomposition view. It is mostly derived from form primitive tasks, not a proper planner decomposition primitive.

Sprint 024 passes only when the live planner behaves like a server-side model/tool system:

```text
user prompt
  -> server-side live planning session
  -> harness-owned primitive/tool manifest with explicit HTML/form primitive options
  -> model selects primitive tools and provides their data
  -> provider output is validated by the harness
  -> UI renders each primitive by its validated renderer/type
  -> user answer/card click emits the same typed PrimitiveAnswerDto
  -> next model turn receives submitted values and updated context
  -> task rail and error dock reflect the latest server session state
  -> no deterministic fallback appears in live mode
```

Real booking, payment, and hold execution remain mocked or approval-gated. Everything else in the live planner path must be live, server-side, and observable.

## Non-Negotiable Rules

- `/trip?live=1` must not fall back to deterministic output.
- Browser JavaScript may post prompt/answer DTOs and render sanitized DTOs. It must not build prompts, call providers, hold keys, or invent model output.
- Provider/model requests remain server-side.
- Every HTML/form primitive is a model-visible tool option.
- The model provides primitive data; the UI chooses only the renderer implementation for an already validated primitive.
- The UI must render by validated primitive/renderer type, not by generic text input fallback.
- The task rail must render the latest validated task decomposition or a fixed explicit missing-task state. It must not keep stale provider-blocked state after accepted server state.
- Development mode must show a fixed bottom status/error dock with the latest provider/harness/UI state. Failures must show fixed codes, turn id, request id when safe, primitive id when available, and stage.
- Blazor instability cannot be used as an acceptance excuse. The product must use one authoritative browser transport for live mode. If HTTP session transport is primary, call it primary. If Blazor Server is used, it must pass the same live soak without disconnect-driven excuses.
- Live browser testing is required. OpenAI and OpenRouter inference/credits are explicitly allowed for manual, smoke, and soak testing.
- Required automated tests stay deterministic/offline unless explicitly marked live/manual.

Known key files:

- OpenAI: `C:\Users\Bartek\Documents\Playground\openai_key.txt`
- OpenRouter: `C:\Users\Bartek\Documents\Playground\openrouter.txt`
- Groq: `C:\Users\Bartek\Documents\Playground\groq_key.txt`
- xAI/Grok: `C:\Users\Bartek\Documents\Playground\grok_key.txt`

## Primitive Tool Library

The model-facing manifest must expose these primitive tool options. Each primitive must have a harness validator and a UI renderer.

### Message And Status Primitives

- `assistant_message`
  - Data: title, body, optional mood/media/evidence/task refs.
  - UI: assistant bubble.
  - Failure: unsafe text or unsupported refs block or redact.
- `status_notice`
  - Data: severity, fixed code, short summary, optional next action.
  - UI: status/error notice.
  - Failure: unknown severity or untrusted free-form error code blocks.

### Basic Form Primitives

- `text_input`
  - Data: field id, label, help text, placeholder, min/max length, required.
  - UI: single-line input.
  - Use only when free text is genuinely required.
- `textarea`
  - Data: field id, label, help text, placeholder, min/max length, required.
  - UI: multi-line input.
- `number_input`
  - Data: field id, label, min/max/step/unit.
  - UI: number input or stepper.
- `slider`
  - Data: field id, min/max/step/unit, labels.
  - UI: slider.
- `date`
  - Data: field id, min/max/default date.
  - UI: date input.
- `date_range`
  - Data: start field id, end field id, min/max/defaults.
  - UI: paired date inputs or date-range control.

### Choice Primitives

- `radio_group`
  - Data: field id, label, options with stable option ids/labels/help, required.
  - UI: visible radio choices.
  - Use for small exclusive choices such as destination confirmation or yes/no confirmation.
- `select`
  - Data: field id, label, options with stable option ids/labels/help, required.
  - UI: dropdown.
  - Use for compact exclusive choices such as pace when there are several known options.
- `multi_select`
  - Data: field id, label, options, min/max selections.
  - UI: checkbox list or multi-select.
- `checkbox`
  - Data: field id, label, true/false value, required.
  - UI: checkbox/toggle.

### Visual Choice Primitives

- `choice_card`
  - Data: option id, label, summary, mood token, media token, evidence refs.
  - UI: one selectable card.
- `candidate_deck`
  - Data: deck id, field id, cards/options, selection mode, mood/media tokens.
  - UI: carousel/deck/cards.
  - Answer: selected option id through the same `PrimitiveAnswerDto` shape as `radio_group` or `select`.

### Planning Structure Primitives

- `task_decomposition`
  - Data: task ids, titles, state, order, dependencies, child steps, evidence refs.
  - UI: task rail and optional timeline.
  - Must be emitted for live planning sessions unless the model is blocked before planning.
- `timeline_item`
  - Data: timeline id, task id/day id/slot id, title, state, evidence/media refs, origin turn id.
  - UI: planning history/timeline.
- `tool_search_request`
  - Data: allowed tool id, purpose, missing context refs, query category.
  - UI: explicit "needs context/tool search" status.
  - No mutation.
- `tool_gap_request`
  - Data: missing tool category, why current tool library cannot satisfy request.
  - UI: review-required status.
  - No mutation.

## Expected Primitive Selection Rules

These are not UI suggestions. They are harness/provider validation expectations:

- Destination confirmation must be `radio_group` or `select`, not `text_input`.
- Known pace choices must be `radio_group`, `select`, or `slider`, not `text_input`.
- Dates must be `date` or `date_range`, not `text_input`.
- Multiple user preferences should be `multi_select`, `checkbox`, `choice_card`, or `candidate_deck`, not a single blob text input.
- Open-ended constraints may use `textarea`.
- The task rail must come from `task_decomposition`, not a static UI array.
- Search/web/booking claims must reference `tool_context_ref` and evidence. If context is unavailable, emit `tool_search_request` or ask the user.

## Target Data Flow

```text
Browser /trip?live=1
  POST /api/planning/session/start { prompt, selectedModelRole, providerPreference }

PlanningSessionStore
  creates session id
  owns PlanningSessionContext
  owns TripSession
  owns latest ValidatedTurnView
  owns sanitized trace entries

PlannerToolManifestCompiler
  emits allowed primitive tool library
  emits allowed field paths
  emits allowed mood/media tokens
  emits task/context/tool refs
  emits graph revision

Provider PlannerPrimitiveRunner
  builds ModelCompletionRequest server-side
  includes transient raw prompt
  includes current submitted answer values on turn 2+
  includes primitive tool library and strict schema
  receives model primitive invocations

Harness PlannerPrimitiveValidator
  validates primitive ids, renderer keys, options, answer schemas,
  task refs, tool refs, graph revision, safe text, media/mood tokens

ValidatedTurnView
  contains only safe renderable primitive data
  contains task decomposition data
  contains trace ids/hash/lineage

UI
  GET/POST server DTOs
  renders by primitive renderer key
  submits PrimitiveAnswerDto only
  displays latest task decomposition
  displays bottom development status/error dock

Next Turn
  answer values update PlanningSessionContext
  provider receives updated context
  no deterministic fallback is allowed
```

## Lanes And Tickets

### Lane 1 - Harness Primitive Tool Manifest

Owner: Shellby

Ticket: `sprint-024/harness-html-primitive-tool-manifest`

How:

- Extend core/harness primitive contracts with the full primitive library above.
- Add explicit `PrimitiveKind`, `RendererKey`, `AnswerValueKind`, and `PrimitiveOption` contracts.
- Add validator rules that tie primitive kind to allowed renderer and answer schema.
- Add `task_decomposition` validation with dependency/order/state checks.
- Add development diagnostic DTOs for latest provider/harness/UI state.

Why:

The model cannot reliably build good forms if the harness only exposes a vague "composite form" shape. The harness owns the contract and must make wrong primitive choices invalid before UI render.

Tests:

- `select` without options blocks with `primitive_options_missing`.
- `radio_group` without options blocks with `primitive_options_missing`.
- `date_range` with text defaults blocks with `primitive_answer_schema_invalid`.
- `text_input` for a manifest-declared enum field blocks with `primitive_renderer_mismatch`.
- `task_decomposition` missing ids/order/dependency refs blocks with `task_decomposition_invalid`.
- accepted task decomposition preserves task ids/titles/states.
- unsafe labels/options/tool refs block or redact and never mutate session context.

Failure points and valid responses:

- Unknown primitive: `primitive_not_supported`.
- Renderer does not match primitive: `primitive_renderer_mismatch`.
- Required options missing: `primitive_options_missing`.
- Answer type wrong: `primitive_answer_schema_invalid`.
- Task graph invalid: `task_decomposition_invalid`.
- Tool/context ref unknown: `tool_context_ref_invalid`.

Acceptance:

- Harness tests prove every primitive type has a validation path.
- Harness compiles a manifest that provider/UI can consume without inventing primitive metadata.
- No live UI can render an unvalidated primitive.

### Lane 2 - Provider Model Tool Invocation Runner

Owner: Kaneki

Ticket: `sprint-024/provider-html-primitive-tool-runner`

How:

- Rewrite provider prompt/schema to present primitives as explicit tool options.
- Require the model to output primitive invocation objects, not prose-form blobs.
- Include primitive selection guidance:
  - destination confirmation: `radio_group` or `select`;
  - dates: `date` or `date_range`;
  - pace: `radio_group`, `select`, or `slider`;
  - multiple preferences: `multi_select`, `choice_card`, or `candidate_deck`;
  - task plan: `task_decomposition`.
- Add prompt/tool examples for at least destination, dates, pace, preference selection, card/deck choice, and task decomposition.
- Reject generic/static output when field ids/options/task ids do not reflect the user prompt/context.
- Preserve server-side transient raw prompt and submitted answers in provider request, but never persist them in eval/log artifacts.

Why:

The model is defaulting to text inputs because the current provider layer permits generic forms. The model needs a constrained tool menu, and the provider layer must reject outputs that ignore the tool library.

Tests:

- Captured `ModelCompletionRequest` contains the full primitive tool list.
- Captured request includes raw prompt transiently and turn 2 submitted values.
- Fake model output for destination confirmation with `text_input` is rejected.
- Fake model output for pace with `text_input` is rejected when options are available.
- Fake model output for date as text is rejected.
- Fake model output with `task_decomposition` validates.
- Prompt A/B fake completions produce different primitive kinds/options/tasks.
- OpenAI and OpenRouter manual live smokes attempt at least one accepted primitive invocation with task decomposition.

Failure points and valid responses:

- Malformed JSON: `planner_model_malformed_json`.
- Schema-invalid primitive: `planner_model_schema_invalid`.
- Wrong primitive for field: `planner_model_primitive_renderer_mismatch`.
- Missing task decomposition after accepted planning turn: `planner_model_task_decomposition_missing`.
- Timeout/rate/provider unavailable: existing fixed provider failure codes.

Acceptance:

- Provider live report includes sanitized examples of accepted primitive invocations from OpenAI and OpenRouter or fixed provider failure codes.
- Accepted output includes at least one non-text primitive and one `task_decomposition`.
- No accepted provider result can be only generic text inputs for dates/pace/destination.

### Lane 3 - End-User UI Primitive Renderer And Truthful State

Owner: Sarah

Ticket: `sprint-024/end-user-html-primitive-renderers`

How:

- Render primitives by validated `rendererKey` and `PrimitiveKind`.
- Add or repair components:
  - `TextInputPrimitive`
  - `TextareaPrimitive`
  - `NumberPrimitive`
  - `SliderPrimitive`
  - `DatePrimitive`
  - `DateRangePrimitive`
  - `RadioGroupPrimitive`
  - `SelectPrimitive`
  - `MultiSelectPrimitive`
  - `CheckboxPrimitive`
  - `ChoiceCardPrimitive`
  - `CandidateDeckPrimitive`
  - `TaskDecompositionRail`
  - `DevelopmentStatusDock`
- Remove generic fallback-to-text for known primitive kinds.
- Clear stale provider-blocked/task-rail state whenever latest server session state is accepted/awaiting input.
- Task rail renders from latest `task_decomposition` data or fixed `task_decomposition_missing`.
- Bottom development status dock renders latest trace:
  - provider request state;
  - provider outcome;
  - provider/model/request id when safe;
  - harness validation state;
  - error/block code;
  - turn id;
  - primitive id.
- `/trip?live=1` must never display deterministic seeded cards/forms/options.

Why:

The server already has enough truth to know the current state. The UI is currently mistranslating that truth by rendering select as text and by keeping stale blocked rail copy after accepted turns.

Tests:

- API `rendererKey=select` renders `<select>` with all allowed values.
- API `rendererKey=radio_group` renders radios.
- API `rendererKey=date_range` renders two date controls or a validated date-range component.
- API `rendererKey=slider` renders slider.
- API `rendererKey=candidate_deck` renders cards, and card click submits `PrimitiveAnswerDto`.
- accepted session state clears stale provider-blocked rail.
- provider-blocked session state shows rail block and bottom status dock.
- deterministic markers are absent from `/trip?live=1` after live start.

Real UI testing spikes:

- OpenAI browser run:
  - starter prompt with destination/date/pace/preferences;
  - verify destination renders radio/select;
  - verify dates render date/date_range;
  - verify pace renders select/radio/slider;
  - verify task rail uses model task ids/titles;
  - submit answers;
  - verify turn 2 appears and rail updates.
- Forced provider malformed run:
  - verify bottom dock shows fixed provider failure;
  - verify no fake deterministic fallback.
- Forced harness validation block:
  - verify bottom dock shows fixed harness failure;
  - verify task rail does not claim accepted state.

Expected results:

- DOM, `GET /api/planning/session/{sessionId}`, and server trace agree on primitive ids, renderer keys, task ids, outcome, and block state.
- No stale `Live provider blocked` appears when `blockedReason = null` and `providerOutcome = planner_model_accepted`.

### Lane 4 - Live End-To-End Soak And Diagnostics

Owner: Collin/coordinator, with support from Sarah if UI repair is needed.

Ticket: `sprint-024/live-200-turn-soak`

How:

- Build a small live-soak script or documented browser-driver path that exercises `/trip?live=1` through the server HTTP session transport or a proven stable Blazor path.
- Run at least 200 live provider turns against OpenAI first.
- Run a smaller OpenRouter comparison pass if OpenAI completes.
- Store raw-free JSONL diagnostics under gitignored `artifacts/live-runs/`.
- Commit only sanitized summary docs.

Why:

One or two accepted turns prove wiring. They do not prove this is usable. The user needs confidence that the loop can keep going without hidden deterministic fallback or stale UI lies.

Required recorded data points:

- run id;
- session id;
- turn index;
- provider;
- model;
- provider request state;
- provider request id when safe;
- provider outcome;
- harness validation outcome;
- primitive ids;
- renderer keys;
- task ids;
- answer ids;
- UI transport;
- latency bucket;
- fixed failure code when present;
- raw absence status.

Acceptance:

- At least 200 live turns attempted.
- 0 deterministic fallback turns in `/trip?live=1`.
- 0 stale provider-blocked rail after accepted server state.
- 0 renderer mismatches between API `rendererKey` and DOM control type.
- Every failed turn has a fixed provider/harness/UI code and appears in the bottom dev status dock.
- If fewer than 200 turns complete, sprint status is BLOCKED with the exact first repeated failure class and sanitized trace ids.

## Real UI Testing Matrix

Workers must run or explicitly block these scenarios:

1. Destination confirmation
   - Prompt: "Plan a low-tourist Japan trip focused on local food and quiet neighborhoods."
   - Expected: destination confirmation is radio/select, not text.
2. Date planning
   - Prompt includes exact dates.
   - Expected: date/date_range primitive, not text.
3. Pace planning
   - Prompt includes "slow", "fast", or "2 days per city".
   - Expected: select/radio/slider primitive.
4. Preference selection
   - Prompt includes multiple interests.
   - Expected: multi_select or card/deck, not one text blob.
5. Task decomposition
   - Any accepted planning prompt.
   - Expected: task rail from `task_decomposition` primitive.
6. Provider block
   - Force provider malformed/timeout.
   - Expected: bottom dock and rail show fixed failure; no deterministic fallback.
7. Harness block
   - Force invalid primitive.
   - Expected: bottom dock shows harness block; UI does not render invalid primitive as form.
8. Long-run soak
   - 200 live turns.
   - Expected: no deterministic fallback, no stale rail, no renderer mismatch.

## Anti-Gaming Pass

These checks exist because previous sprints could report "accepted" while the user saw static or stale UI.

### Anti-Gate 1 - Renderer Truth

How a worker could lie:

- Assert the API returned `rendererKey=select`, but the UI still renders an input.
- Test only service projection, not actual browser DOM.

Required proof:

- Browser test must compare each current primitive field:
  - API `rendererKey`;
  - DOM element tag/type;
  - answer DTO emitted after interaction.
- Select/radio/date/slider/card/deck must each have at least one browser-level assertion.

READY is forbidden if:

- Any known primitive falls back to generic text input without a fixed `primitive_renderer_missing` failure.

### Anti-Gate 2 - State Rail Truth

How a worker could lie:

- Server state is accepted, but task rail still shows a previous block.
- DOM markers update but visible rail text remains stale.

Required proof:

- Browser smoke must capture:
  - `GET /api/planning/session/{sessionId}` provider/harness state;
  - visible task rail text;
  - bottom dock state.
- If server `blockedReason = null`, the visible rail must not contain stale blocked copy.

READY is forbidden if:

- The task rail disagrees with latest server state.

### Anti-Gate 3 - No Deterministic Escape Hatch

How a worker could lie:

- Use deterministic cards/forms after provider failure while keeping live badges.
- Increment live turn count without model request.

Required proof:

- `/trip?live=1` browser smoke checks deterministic fixture markers absent.
- Each accepted live turn has a provider request id or explicit fixed no-provider code.
- Live turn count must equal trace entries that attempted provider or fixed-blocked before provider.

READY is forbidden if:

- Any live flow renders deterministic fixture cards/forms/options as successful live output.

### Anti-Gate 4 - Task Decomposition Is Real

How a worker could lie:

- Keep deriving the rail from form fields and call it decomposition.
- Use static task titles.

Required proof:

- Accepted live planning turn includes a `task_decomposition` primitive or a fixed `task_decomposition_missing` block.
- Task ids/titles in rail match API task ids/titles and sanitized server trace.
- Prompt A/B task structures differ when prompts differ structurally.

READY is forbidden if:

- The rail is hard-coded or only derived from a generic form primitive while claiming live decomposition.

### Anti-Gate 5 - 200-Turn Soak Cannot Be Skipped

How a worker could lie:

- Run two turns and say the loop works.
- Use unit tests instead of live browser/API soak.
- Hide repeated provider or validation failures.

Required proof:

- Sanitized soak report includes 200 attempted turn rows or an explicit BLOCKED result at first repeated failure.
- Report includes accepted count, blocked count, failure classes, renderer mismatch count, stale rail count, deterministic fallback count.

READY is forbidden if:

- No 200-turn attempt exists and no fixed repeated blocker is documented.

### Anti-Gate 6 - Development Errors Must Be Visible

How a worker could lie:

- Log provider/harness failures only to console or files.
- Show generic "something went wrong" without fixed codes.

Required proof:

- Forced provider failure shows bottom dock with fixed provider code.
- Forced harness validation failure shows bottom dock with fixed harness code.
- Forced UI/session error shows bottom dock with fixed UI code.
- The dock never includes raw prompt, raw provider payload, API keys, credentials, approval tokens, hold refs, booking refs, payment refs, or raw exception text.

READY is forbidden if:

- A failure can happen in Development without a visible fixed-code status/error dock.

## Exit Criteria

Sprint 024 is READY only if all of the following are true:

- all primitive types listed above have harness validation and UI rendering or are explicitly blocked as unsupported;
- provider prompt/schema exposes those primitives as model tool options;
- destination/date/pace/preference prompts produce appropriate non-text primitive types in live browser smoke;
- task rail derives from validated `task_decomposition` or shows fixed missing-decomposition block;
- stale provider-blocked UI state is eliminated;
- bottom development status/error dock exists and surfaces fixed provider/harness/UI failures;
- `/trip?live=1` has 0 deterministic fallback success paths;
- live soak attempts 200+ turns or blocks with a fixed repeated failure class;
- all required tests pass;
- docs record exact live outcomes and remaining blockers.

If any item fails, the lane or sprint status is BLOCKED, not READY.
