# Sprint 018 - Progressive Planning History

Coordinator: Collin

Dispatch base: to be set after Sprint 017 publishes.

## Objective

Make the end-user planner easier to understand and safer to revise by separating the live chat interaction from the planning history, moving Stage Cockpit onto its own route, and adding the first harness-owned edit-impact boundary.

The user should be able to plan in the main interaction area, watch choices accumulate in a separate visual history, click a past decision to jump back to its source, and understand which later choices may need repair if they change something.

Read first:

- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/end-user-progressive-history.md`
- `docs/design/sprint-017-japan-card-media-pack.md` when available

## Lane A - Harness Planning Edit Impact

Owner: Shellby

Branch: `sprint-018/harness-planning-edit-impact`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned planning node/dependency snapshot for mission facts, days, slots, selected/deferred decisions, availability previews, and mock hold readiness.
- `EditImpactRequest`/`EditImpactResult` style boundary that reports affected nodes, preserved nodes, and minimal repair prompts for a changed selected candidate or changed itinerary slot.
- Fixed sanitized outcome codes for accepted, stale snapshot, unknown node, unsupported edit, no impact, and repair required.
- No mutation unless explicitly applying a later edit; this lane is impact analysis only.
- Tests for selected-candidate edit, day/slot edit, stale fingerprint, unknown node, no-mutation, and no raw prompt/provider/approval/secret leakage.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

## Lane B - Provider Edit Repair Posture

Owner: Kaneki

Branch: `sprint-018/provider-edit-repair-posture`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local DTOs/eval rows for model-assisted repair suggestion posture, not automatic edits.
- Deterministic mock source that can suggest keep, replan-day, reselect-candidate, or ask-user repair modes from sanitized node metadata.
- Optional guarded live runner shape may exist, but required tests must stay deterministic/offline.
- Sanitized rows persist fixed repair mode enums, counts, provider/model/request metadata only for accepted rows, and fixed codes for rejected/error rows.
- No raw prompt, provider payload, candidate display text, approval token, hold reference, credentials, or exception text in rows.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

## Lane C - End-User Planning Timeline UI

Owner: Sarah

Branch: `sprint-018/end-user-planning-timeline-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`
- `docs/design/**`

Deliverables:

- Split routes: end-user planner on `/` or `/trip`, Stage Cockpit on `/stage-cockpit`.
- Move evidence/decision trace out of the chat transcript into a dedicated image-backed planning timeline below or adjacent to the main interaction surface.
- Timeline supports day mode and task mode with stable `data-*` markers for day/task/slot/candidate/decision/evidence ids.
- Timeline item click scrolls the main interaction window to the originating turn.
- The folded `Ask` composer stays inside the chat view on the right edge, quiet/pastel, same rough height as the original textbox and roughly user-bubble inset width.
- Break the end-user surface into Blazor components: shell, composer drawer, assistant work bubble, choice deck, option card, selected option bubble, planning timeline, task rail, approval plate, provider status.
- Browser smoke proves prompt send, folded composer, Ask drawer, timeline day/task toggle, item click-to-scroll, selected card imagery, raw absence, and Stage Cockpit route separation.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- in-app browser smoke on end-user and Stage Cockpit routes

## Exit Criteria

- The end-user route no longer renders Stage Cockpit inline.
- Planning history is a separate browsable element, not a chat bubble dump.
- A user can click a timeline item and jump to the originating interaction.
- The composer becomes easier to access without stealing attention.
- The first deterministic harness edit-impact result can explain affected vs preserved nodes after a past choice changes.
- Required tests remain deterministic/offline and raw-sentinel scans remain clean.
