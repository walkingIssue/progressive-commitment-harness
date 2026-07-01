# Plan 1 — Real End-User Testable Chat UI
Turn the fixture-driven feasibility cockpit into a chat-style surface a real person can use to plan an itinerary, driven by the live harness loop and wired to real models and real read-only provider APIs, with hold/book/pay/spend kept mocked and approval-gated. This is the plan to run first: it produces both the human "feel" signal (including the feel of a small vs strong model in the harness role) and the real, unforeseen model/provider failure modes needed to judge whether the project is worth deeper investment.
## Problem
`src/Pch.UI/Features/StageCockpit/HarnessStageCockpitService.cs` (~54 KB) and `StageCockpit.razor` (~49 KB) render pre-baked `*Fixture` lists, not a live session. A user cannot actually sit down, type a rambling prompt, and collapse an option space. The UI also has no dark mode and a flat green theme, and leans on bordered `.panel`/card backgrounds everywhere, so decorative info reads as clickable.
## Current State
* The harness already exposes a clean live turn engine: `SessionLoop` (`src/Pch.Harness/SessionLoop.cs`) returns a `SessionTurnResult` carrying `Stage`, projected `StagePacket`, `NextAction`, decision, and blocked reason. `RuntimeActionApplication` (`src/Pch.Harness/RuntimeActionApplication.cs`) is the canonical decode+intake path for model-proposed actions.
* `Program.cs:9` registers `HarnessStageCockpitService` as scoped (per circuit) — the right lifetime for a live session, but the service mixes session ownership with ~20 collaborators and a dozen fixture lists.
* `forms.ts` (`src/Pch.UI/ClientApp/forms.ts`) already does draft save/restore, focus, and `data-*` counting; reuse it for chat draft + theme persistence.
* Theme tokens live in `src/Pch.UI/wwwroot/app.css:62` (`:root` with `--pch-*`). No `[data-theme]` / dark variant exists.
* `HarnessStageCockpitServiceTests.cs` (~59 KB) asserts current `data-*` markers; renames must be migrated, not dropped.
* Live seams already exist: `OpenRouterModelCompletionClient` + `ModelCompletionMissionPlannerClient` (`IModelCompletionClient`), `PromptPacketBuilder`, `AvailabilityQuotePreviewApplication`, candidate expansion, and the mission planner runtime — plus `ProviderApiKeyLoader` and a credit guard.
* Commit safety already exists: mock hold/booking adapters, `ApprovalGate`, and `ExternalActionDecoder` dropping model-supplied approval tokens. Reads can go live without touching these.
## Proposed Changes
### 1. Session conductor (decompose the god-object)
Introduce `SessionConductor` (scoped) that owns exactly one `TripSession` and exposes typed turn methods (`SubmitForm`, `SelectCandidates`, `Approve`, `Defer`, `RunSuggestedAction`) returning a UI-agnostic turn outcome. Move the existing fixture/demo flows out of the hot path into a separate `DemoScenarioCatalog` so the live conductor stays small. The conductor delegates to `SessionLoop` / `RuntimeActionApplication`; it does not re-implement harness logic.
### 2. Chat transcript model
Add a `TranscriptEntry` projection: an ordered list of turns where each entry is either a user submission or a rendered harness output derived from `SessionTurnResult.NextAction` (`emit_form`, `emit_choice_set`, `request_approval`, `summarize`, plus blocked notices). The conductor appends to the transcript each turn; the page renders the transcript top-to-bottom in a scrolling column and pins the active input affordance at the bottom.
### 3. Message renderer components
Under `src/Pch.UI/Features/StageCockpit/Transcript/`: `FormCard`, `ChoiceSetCard`, `ApprovalCard` (reuse `Features/Approvals/ApprovalGate.razor`), `SummaryBubble`, `EvidenceList` (reuse `EvidenceTracePreview.razor`), and `BlockedNotice`. Each renders one `TranscriptEntry` and raises a typed callback the conductor feeds back into the loop. Design rule from `docs/architecture/UI.md`: every option preserves its `candidate_id` and claim provenance — keep those as `data-*` attributes on rendered options.
### 4. Theming: light/dark + intent tiers
Replace the flat `:root` block with a token layer driven by `[data-theme="light|dark"]` (default from `prefers-color-scheme`, overridable via a header toggle persisted in `localStorage` through `forms.ts`). Token tiers:
* Base surfaces: soft, low-chroma pastel backgrounds with strong text contrast (WCAG AA). Whimsical/light-hearted, booking.com/airbnb-adjacent but warmer.
* Dark mode: deep marine blue as the primary surface color, with derived elevations (lighter marine for raised cards, desaturated pastel accents tuned for contrast on marine).
* Accents: pastel accent + a clear `accent-contrast` for selected/active states.
* "Hard"/"dangerous" intent (irreversible, spend, booking — i.e. `request_approval`, gated handoffs): a distinct weighty, shiny metallic treatment (subtle gradient + sheen + heavier border/elevation) so commitment actions feel materially different from routine choices.
### 5. Decorative vs interactive separation
Establish two visual classes: interactive surfaces (forms, choice cards, buttons, approval tiles) get card backgrounds, borders, and hover/focus affordances; decorative/informational elements (stage status, load-bearing facts, packet metadata, trace lines) use plain typography, dividers, and spacing — no elevated card background, no button-like framing. This directly addresses the tendency to make everything look like a button.
### 6. Live wiring + test-marker continuity
Point the page at `SessionConductor` for the primary flow; keep `DemoScenarioCatalog` behind a clearly-labeled "demo" affordance for deterministic UI smoke. Preserve existing `data-*` markers (or migrate them with matching test updates) so `HarnessStageCockpitServiceTests` and the browser smoke keep passing. Keep keyboard flow and `data-*` hooks from `forms.ts`.
### 7. Model roles + per-role model picker
Define two independently-fillable model roles: the in-harness role (consumes compiled `StagePacket`/prompt packets and emits `HarnessAction`s — forms, choice framing, summaries) and the strong-planner role (mission planning/expansion via `ModelCompletionMissionPlannerClient`). Add a `ModelRoleRegistry` of selectable model ids from config and surface a picker per role in the header. The active mapping lives in circuit/session state and defaults from config.
### 8. Mid-session model switching
Because the harness owns all durable state and every model call sees only a freshly compiled projection (`ProjectionService` / `PromptPacketBuilder`), the model filling a role can change between turns with zero context migration — the thesis payoff. The conductor reads the current role→model mapping at the start of each turn and builds the `ModelCompletionRequest` accordingly; no conversation history is replayed to the model, so swapping a small model for a stronger one (or back) is safe and recorded in the transcript.
### 9. Live read-only providers, mocked commit gates
Wire real read paths into candidate pools when credentials exist: availability/quote preview (`AvailabilityQuotePreviewApplication`), candidate expansion, and mission planning go through live providers. Hold/book/pay/spend stay on the mock adapters behind `ApprovalGate` + user `ApprovalToken`, and `ExternalActionDecoder` keeps dropping model-supplied tokens. This matches Stage 7 of `docs/PLAN.md`: real search/quote, mocked commit.
### 10. Graceful degradation + failure surfacing
With no key/credits (or on provider/model failure) the surface must not crash: fall back to the deterministic mock/fixture path with a visible "deterministic mode" banner so it stays usable offline and required tests stay live-free. Surfacing real failures is the point — render typed `ProviderException`s, empty-content, schema-invalid decode, intake blocks, availability gaps, and timeouts as first-class, sanitized transcript states (never raw payloads, keys, or tokens).
## Worker Lanes (for the coordinator)
* Lane A — Conductor + transcript model + renderer components (C#/Razor), owns `Features/StageCockpit/*` excluding CSS.
* Lane B — Theme token system, light/dark, intent tiers, decorative/interactive split (CSS + theme toggle JS in `ClientApp`).
* Lane C — Test-marker migration + UI smoke + a11y/contrast checks, owns `tests/Pch.UI.Tests` and browser smoke.
* Lane D — Live model/provider integration: `ModelRoleRegistry`, per-role pickers, mid-session switching, live read-only provider wiring, credit/key guards, graceful degradation, and sanitized failure-state surfacing.
Freeze the `TranscriptEntry` / conductor method contract (including the role→model hook) before Lanes B, C, and D start so they work against a stable seam.
## Verification
* `npm run build:ui`, `dotnet build` (0 warnings), `dotnet test` for `Pch.UI.Tests`.
* Offline: with no key configured, the surface runs in deterministic mode (banner shown) and `Pch.UI.Tests` stay live-free.
* Live (gated, manual): with a key + credits, swap the in-harness model between a small and a strong model mid-session and confirm trip state survives the switch; confirm hold/book/pay/spend stay mocked and approval-gated; confirm real provider/model failures render as sanitized transcript states.
* Manual: type a free-text prompt, advance through form → choice collapse → approval → evidence, confirm interaction count stays roughly flat for 7- vs 14-day trips.
* Contrast check both themes; confirm decorative elements are visually non-clickable and hard/irreversible actions carry the metallic treatment.
## Out of Scope
Real hold/book/pay/spend execution (reads are live, but commit actions stay mocked and approval-gated), systematic model-quality scoring (that is Plan 3), and final consumer polish.
## Risks
* Marker drift breaking the large UI test file — mitigate by migrating markers in the same change.
* Conductor scope creep — keep harness logic in `Pch.Harness`; the conductor only orchestrates calls and builds the transcript.
* Credit burn from interactive use — honor the existing credit guard, default to economical models, and fall back to deterministic mode when exhausted; never silently switch to another paid provider.
* Secret/payload leakage into the rendered UI — never render keys, tokens, or raw provider/model payloads; route every surfaced failure through the shared sanitizer.
