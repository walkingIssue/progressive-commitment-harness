# Sprint 016 - Live End-User Interaction UI

Coordinator: Collin

Planning base: Sprint 015 published main.

Coordinator rule: Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn the Sprint 015 offline chat shell into a genuinely interactive end-user planning surface.

Sprint 016 should fix the current browser click failure, replace the boilerplate Blazor feel with the new interaction primitive system, and wire a live model/provider path for read-only planning work when keys and credits are available. Deterministic mode remains required for CI and fallback, but it must be visibly separate from live mode.

Design reference:

- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/assets/sprint-016-end-user-chat-concept.png`
- `docs/design/assets/sprint-016-agent-first-interaction-concept.png`

## Non-Negotiable Fix

The current Sprint 015 chat button renders but does not invoke `SendPromptAsync` in the browser. Sprint 016 is not READY unless browser automation proves prompt entry and send mutate the transcript/final state.

## Stage Targets

| Stage | Sprint 016 target |
| --- | --- |
| Stage 2/3/9 | Live end-user turn conductor and transcript primitives |
| Stage 4/5 | Model role wiring for live read-only planning and model-generated harness actions |
| Stage 7 | Live read-only preview wiring where safe; commit-like paths remain mocked and approval-gated |
| Stage 10 | Browser-click smoke and sanitized live failure traces |

## Lane A - Harness Live Turn Contract

Owner: Shellby

Branch: `sprint-016/harness-live-turn-contract`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/harness/**` if needed

Deliverables:

- Harness-owned turn/transcript projection DTOs that can describe form, choice, approval, summary, evidence, blocked, and provider-failure turns without UI-specific code.
- A live-turn contract over `SessionLoop`, `RuntimeActionApplication`, `PromptPacketBuilder`, and existing mission/itinerary/availability boundaries.
- A pending-confirmation golden trace so the UI no longer has to invent a local pending state.
- Fixed sanitized outcome codes for turn accepted, awaiting user input, provider/model blocked, intake blocked, approval required, and deterministic fallback.
- Tests proving no mutation on blocked/model-invalid/provider-invalid turns and no raw prompt/provider/approval/secret leakage in serialized turn projections.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

## Lane B - Live Model Role And Provider Runner

Owner: Kaneki

Branch: `sprint-016/live-model-role-runner`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local live model role registry that can map in-harness and strong-planner roles to configured model ids.
- A guarded live model runner for read-only planner/harness-action generation using existing `IModelCompletionClient` and OpenRouter/OpenAI-compatible clients.
- Explicit config/env controls for live mode, model ids, timeout, credit guard, and fallback policy.
- Manual live smoke path that is allowed to spend credits when keys/credits are present, but remains skipped in required tests.
- Sanitized status/eval rows for key missing, credit exhausted, timeout, empty content, malformed schema, packet mismatch, unsupported model output, and successful structured output.
- Docs explaining expected OpenRouter log activity so a user can tell whether the UI actually called a live model.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- Optional guarded live smoke when configured; report BLOCKED rather than silently falling back.

## Lane C - End-User UI Primitive System

Owner: Sarah

Branch: `sprint-016/end-user-live-chat-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Fix Blazor interactivity for the end-user chat page and add a browser-click regression so send cannot silently no-op again.
- Replace the current boilerplate chat shell with the primitive system from `docs/design/end-user-chat-interaction-primitives.md`.
- Add central transcript rendering with assistant work bubbles, form cards, choice cards, candidate option cards, approval plates, provider failure notices, and an evidence strip.
- Add right-side decomposed task rail with collapsible rows and status lights.
- Make the agent work area visually dominant after the first user prompt. The initial start screen may center the prompt input, but after first send the composer should collapse into a compact side `Ask` control that opens a slide-out drawer.
- When a user selects an option, echo that selected option into the transcript as a compact user interaction bubble while keeping the original card selected in the assistant work area.
- Add mood/feel-backed candidate cards and support stacked, floaty, horizontally scrollable decks for multiple options in the same mood.
- Add model/status strip with deterministic/live mode, model role selection, provider health, credit/fallback state, and last sanitized provider failure.
- Add light/dark theme tokens, remove the default Bootstrap feel from the primary end-user surface, and keep Stage Cockpit as an engineering surface below or behind a clear affordance.
- Keep stable `data-*` markers for candidate ids, evidence ids, turn ids, task ids, model role state, provider outcome, approval state, and raw absence.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- browser smoke proving prompt send, one form submission, one choice selection/defer, one approval block, deterministic fallback, and raw-sentinel absence.

## Integration Order

1. Shellby first: freeze turn/transcript contract and pending-confirmation trace.
2. Kaneki second: provide live model/provider role runner and sanitized live status rows.
3. Sarah third: consume canonical contracts and build the end-user interaction system.
4. Collin final verification:
   - `npm run build:ui`
   - `dotnet build src/Pch.UI/Pch.UI.csproj`
   - `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
   - `dotnet test`
   - `dotnet build`
   - browser smoke in deterministic mode
   - guarded live smoke with keys/credits if available

## Exit Criteria

- A user can type a prompt, press Send, and visibly advance the transcript.
- After first send, the prompt input collapses into a side/drawer affordance so the agent interaction is front and center.
- Selected options appear as user interaction bubbles and retain trusted ids in the DOM.
- Candidate choices can communicate mood/feel with distinct backdrops and same-mood options can be browsed as a stacked deck.
- The UI clearly says whether the run is deterministic/offline or live-model-backed.
- With live mode configured, at least one read-only planning/model call reaches the provider and returns a sanitized transcript state.
- Provider failures are visible as typed, sanitized UI states rather than silent no-ops.
- Hold, book, pay, spend, and irreversible actions remain mocked and approval-gated.
- The UI no longer looks like boilerplate Blazor; it uses the new primitive system, task rail, and visual hierarchy.
- Required tests do not require live providers, keys, network, bookings, payments, or credits.
- No raw prompt text, provider payload, proposal JSON, credential, approval-token value, payment data, live booking reference, candidate display sentinel, raw exception text, or secret-like sentinel is rendered or persisted.

## Live-Credit Policy

Sprint 016 manual/live verification may spend provider credits for interactive model testing when keys and credits are available. It must still:

- check key presence before calling;
- check provider/credit health where supported;
- use strict timeout handling;
- block on empty or malformed output;
- never silently fall back to another paid provider;
- keep raw model/provider payloads out of logs, docs, eval rows, and UI output.
