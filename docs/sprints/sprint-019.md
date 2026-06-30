# Sprint 019 - Model-Attached End-User Planning Loop

Coordinator: Collin

Dispatch base: `50b586d69123b158885ac6aafe2c064ae39d3894`

## Objective

Attach real models to the end-user planner through the harness, narrowly and safely.

Sprint 019 is not a full autonomous trip-planning MVP. It is the first serious model-attached vertical slice: a user submits a prompt, the app builds trusted harness packets, a configured live model may propose read-only planning output, the harness validates or blocks that output, and the UI renders the result or failure as a sanitized transcript state.

The default path must remain deterministic/offline for required tests and local smoke. Live model use is opt-in, guarded, and allowed to spend credits during manual development and smoke when keys/credits are configured.

Read first:

- `docs/PLAN.md`
- `docs/PLAN_PROGRESS.md`
- `docs/sprints/sprint-018.md`
- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/end-user-progressive-history.md`
- `docs/providers/openrouter-qwen14b.md`
- `docs/providers/live-model-role-runner.md`
- `docs/evals/sanitized-artifacts.md`

## Scope Boundary

In scope:

- live/read-only model turns for end-user planning;
- model role selection and preflight;
- prompt/session conductor boundary;
- sanitized model failure states in the UI;
- proof that at least one live model call can reach the harness path when configured;
- live failure reports documenting what breaks.

Out of scope:

- real booking, hold, payment, spend, or irreversible side effects;
- automatic edit application after history changes;
- production-grade itinerary quality;
- final image/media polish.

Visual note: Sprint 017 media and Sprint 018 UI polish are good enough for live testing. Better generated/stock destination imagery should be a follow-up visual-assets lane once the live loop has a spine. Sarah should preserve the existing media manifest hooks so improved images can drop in later without changing harness behavior.

## Live-Credit Policy

Sprint 019 may spend OpenRouter, OpenAI, and Grok/xAI-compatible credits during manual development/smoke when explicit keys/config are present.

Rules:

- required tests stay deterministic/offline;
- live calls must be opt-in through environment/config;
- check key presence before calling;
- use credit/provider-health guards where available;
- use strict send/body timeout handling;
- block on empty, malformed, packet-mismatched, or unsupported output;
- never silently fall back to a different paid provider;
- never render, persist, or commit raw prompts, raw completions, provider payloads, API keys, approval tokens, hold references, candidate display sentinels, credentials, or raw exception text.

Every lane must write a sanitized live/failure report:

- Shellby: `docs/live-failure-reports/sprint-019-harness-live-conductor.md`
- Kaneki: `docs/live-failure-reports/sprint-019-provider-live-preflight.md`
- Sarah: `docs/live-failure-reports/sprint-019-end-user-live-ui.md`

The report should include:

- live status: `not_configured`, `blocked_by_guard`, `attempted`, `passed_with_findings`, or `failed_safely`;
- providers/models attempted;
- sanitized fixed outcome/failure codes;
- whether a real provider request was made;
- what the harness accepted, blocked, or could not yet express;
- proposed follow-up tickets.

## Lane A - Harness Live Session Conductor

Owner: Shellby

Branch: `sprint-019/harness-live-session-conductor`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/live-failure-reports/**`

Deliverables:

- Add a harness-owned, UI-agnostic live session conductor boundary. Suggested shape: `LiveSessionConductor`, `LivePlanningTurnRequest`, `LivePlanningTurnResult`, and typed result fragments for prompt intake, runtime action application, mission planner handoff, itinerary/candidate state, approval-required blocks, and provider/model blocked states.
- The conductor owns one `TripSession` lifecycle and composes existing harness boundaries instead of reimplementing them: `PromptPacketBuilder`, `SessionLoop`, `RuntimeActionApplication`, `MissionProposalAdapter`, `ItinerarySlotCompiler`, `ItineraryCandidateApplication`, `AvailabilityQuotePreviewApplication`, `LiveTurnProjector`, and `PlanningEditImpactAnalyzer` where relevant.
- Define a provider-agnostic input seam for model proposals. The harness lane must not reference `Pch.Providers`; it should accept trusted, already-decoded proposal/result shapes or sanitized proposal envelopes that the provider/UI lane can map into.
- Preserve runtime-only raw prompt behavior: raw user prompt may be transient input, but must not appear in serialized results, trace records, snapshots, or diagnostics.
- Fixed sanitized outcome codes for accepted, awaiting user input, provider/model blocked, decode blocked, intake blocked, mission proposal blocked, approval required, deterministic fallback, and unsupported live operation.
- No mutation on rejected, stale, malformed, packet-mismatched, or approval-required model output.
- Golden/fake tests for one happy prompt-to-mission turn, one model malformed/blocked turn, one approval-required turn, and one deterministic fallback turn.
- Live/failure report at `docs/live-failure-reports/sprint-019-harness-live-conductor.md`.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

## Lane B - Provider Live Preflight And Model Turn Runner

Owner: Kaneki

Branch: `sprint-019/provider-live-preflight-runner`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`
- `docs/live-failure-reports/**`

Deliverables:

- Add a provider-local live preflight runner for OpenRouter/OpenAI-compatible and Grok/xAI-compatible model endpoints. It should verify key/config presence, optional credit health, timeout behavior, non-empty content, and strict structured-output support before the UI tries to use a model.
- Extend or compose `LiveModelRoleRunner` so end-user turns can request two roles: `InHarnessActionGenerator` and `StrongPlanner`.
- Expose model-role configuration from environment/config without hardcoding secrets. Suggested env/config vocabulary may build on existing `PCH_LIVE_MODEL_*` and `OPENROUTER_API_KEY`.
- Add sanitized preflight/eval rows for: key missing, disabled by config, credit exhausted, timeout, empty content, malformed JSON, schema unsupported, packet mismatch, fallback disabled, provider unavailable, and accepted structured output.
- For OpenRouter, document expected log/network sequence: `/api/v1/credits` then `/api/v1/chat/completions` only when enabled.
- If keys are present, run at least one manual guarded live preflight and record sanitized findings. If not configured, report `blocked_by_guard`.
- No raw provider request/response body, raw model completion, prompt text, API key, credential, approval token, hold reference, or candidate display value may be persisted in rows or docs.
- Live/failure report at `docs/live-failure-reports/sprint-019-provider-live-preflight.md`.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- Optional guarded live smoke when configured; report fixed/sanitized outcome rather than silently falling back.

## Lane C - End-User Live Model UI Integration

Owner: Sarah

Branch: `sprint-019/end-user-live-model-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`
- `docs/live-failure-reports/**`

Deliverables:

- Replace the current deterministic-only end-user flow with a UI adapter over the canonical Sprint 019 conductor and provider preflight/runner contracts once Shellby/Kaneki heads land.
- Keep deterministic/offline mode as the default required-test path.
- Add explicit Live/Deterministic posture in the end-user header/status strip: selected model role, provider, preflight state, last sanitized failure, and whether the latest turn used live model output or deterministic fallback.
- Add a guarded model picker or config-derived role display for at least in-harness and strong-planner roles. Mid-session role switching may be simple, but it must not replay raw chat history to a model.
- On first prompt send in live mode, attempt exactly one read-only model-attached turn when preflight passes. If preflight fails, render a typed sanitized blocked/fallback state instead of a silent no-op.
- Render accepted model/harness output through the existing assistant work bubble primitives, option cards, selected-card echoes, and planning timeline. Do not degrade selected choices into plain text.
- Preserve current UI polish: separated `/trip` and `/stage-cockpit`, bottom/focus-loss Ask behavior, compact planning timeline, rounded/pastel interaction space, and media manifest hooks for future better images.
- Browser smoke must prove:
  - deterministic prompt send still works;
  - live guard/preflight state is visible;
  - when live config is absent, the UI says `blocked_by_guard` or equivalent fixed state;
  - when live config is present, at least one provider request is attempted and rendered as accepted or sanitized failure;
  - raw sentinel scan remains clean.
- Live/failure report at `docs/live-failure-reports/sprint-019-end-user-live-ui.md`.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- in-app browser smoke on `/trip` and `/stage-cockpit`
- guarded live smoke when configured

## Integration Order

1. Shellby first: freeze conductor/result contracts and fake tests.
2. Kaneki second: freeze provider preflight/model turn result vocabulary and fake/live guard tests.
3. Sarah third: integrate both upstream heads and wire `/trip` through the live/deterministic adapter.
4. Collin final:
   - merge lanes into `main`;
   - run UI/provider/harness focused tests;
   - run full serial build/test if time/locks permit;
   - browser smoke deterministic mode;
   - guarded live smoke if keys/credits are present;
   - publish sanitized findings.

## Exit Criteria

- The end-user planner has a real model-attached path, not only deterministic fixtures.
- The harness, not the UI, owns durable planning state and validation.
- At least one manual live provider attempt is either executed or blocked with an honest fixed guard reason.
- The UI makes it obvious whether a turn was deterministic, live accepted, or live blocked.
- Model failures become useful product/harness signals, not invisible no-ops.
- Hold, booking, payment, spend, and irreversible work remain mocked/approval-gated.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, raw model completion, API key, credential, approval token, hold reference, candidate display sentinel, booking/payment reference, raw exception text, or secret-like value is persisted or rendered.
