# Sprint 020 - Live Model Proposal Bridge

Coordinator: Collin

Planning base: `cc04b2084d25b4310ad9a2f79342b898c8dc9233`

Dispatch status: planned only. Do not start worker lanes until Collin explicitly dispatches Sprint 020.

## Objective

Turn Sprint 019's live preflight into a real model-to-harness planning turn.

Sprint 019 proved that a guarded live provider call can be made and that the end-user UI can display live/deterministic posture honestly. Sprint 020 should make the next narrow vertical slice work: a live model returns a strict structured mission proposal, provider code validates and sanitizes it, the harness maps it into `LiveModelProposalEnvelope`, `LiveSessionConductor` applies or blocks it, and the end-user UI renders the accepted or blocked result without pretending deterministic fixtures are live planning.

This is still not a full autonomous trip planner. The target is one safe model-generated mission/planning proposal path plus excellent diagnostics when it fails.

Read first:

- `docs/PLAN.md`
- `docs/PLAN_PROGRESS.md`
- `docs/sprints/sprint-019.md`
- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/end-user-progressive-history.md`
- `docs/providers/openrouter-qwen14b.md`
- `docs/providers/live-preflight-runner.md`
- `docs/providers/live-model-role-runner.md`
- `docs/evals/sanitized-artifacts.md`
- `docs/live-failure-reports/sprint-019-provider-live-preflight.md`
- `docs/live-failure-reports/sprint-019-harness-live-conductor.md`
- `docs/live-failure-reports/sprint-019-end-user-live-ui.md`

## Scope Boundary

In scope:

- strict provider model output schema for a first mission/planning proposal;
- provider-to-harness proposal mapping into the existing live conductor seam;
- one model-generated prompt-to-mission path through `LiveSessionConductor`;
- fixed sanitized failure states for empty, malformed, unsupported, packet-mismatched, unsafe, provider-unavailable, timeout, and harness-blocked results;
- end-user UI state that clearly distinguishes deterministic fallback, live preflight, live proposal accepted, and live proposal blocked;
- live smoke allowed with OpenRouter/OpenAI/Grok-compatible providers when explicit keys/config/credits are present;
- per-lane live failure reports documenting discovered real-model issues.

Out of scope:

- booking, hold, payment, spend, irreversible actions, or real provider handoff;
- live search/availability media ingestion;
- full itinerary optimization quality;
- automatic repair application after timeline edits;
- broad visual redesign or image replacement, except preserving current media hooks and avoiding regressions.

## Live-Credit Policy

Sprint 020 may spend OpenRouter, OpenAI, and Grok/xAI-compatible credits during manual development/smoke when explicit keys/config are present.

Rules:

- required tests stay deterministic/offline;
- live calls are opt-in through environment/config;
- check key presence before calling;
- use credit/provider-health guards where available;
- enforce strict request and body-read timeouts;
- block on empty, malformed, packet-mismatched, unsupported, unsafe, or harness-rejected output;
- never silently fall back to a different paid provider;
- never render, persist, or commit raw prompts, raw completions, provider payloads, API keys, approval tokens, hold references, candidate display sentinels, credentials, booking/payment references, or raw exception text.

Every lane must write a sanitized report:

- Shellby: `docs/live-failure-reports/sprint-020-harness-live-proposal.md`
- Kaneki: `docs/live-failure-reports/sprint-020-provider-live-proposal.md`
- Sarah: `docs/live-failure-reports/sprint-020-end-user-live-proposal-ui.md`

The report should include:

- live status: `not_configured`, `blocked_by_guard`, `attempted`, `passed_with_findings`, or `failed_safely`;
- providers/models attempted;
- sanitized fixed outcome/failure codes;
- whether a real provider request was made;
- what the harness accepted, blocked, or could not yet express;
- proposed follow-up tickets.

## Lane A - Harness Live Proposal Application

Owner: Shellby

Branch: `sprint-020/harness-live-proposal-application`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/live-failure-reports/**`

Deliverables:

- Harden the provider-agnostic proposal seam in `LiveSessionConductor` so a decoded model proposal can be applied without any `Pch.Providers` dependency.
- Define or extend canonical DTOs for a live mission proposal envelope with fixed operation/result codes, packet/session correlation, mission proposal mirror, pending-confirmation signals, and deterministic fallback state.
- Preserve runtime-only raw prompt behavior: raw prompt may be transient request input, but it must not appear in serialized result DTOs, traces, snapshots, or diagnostics.
- Ensure accepted mission proposal output flows through canonical validation boundaries, especially `MissionProposalAdapter`, `MissionIntakeApplication`, itinerary compilation, and `LiveTurnProjector`.
- Add blocked/no-mutation coverage for malformed proposal, unsupported operation, stale packet/session, invalid commitment, unsupported field path, approval-required, deterministic fallback, and provider-model-blocked envelopes.
- Add golden/fake tests for one live proposal accepted turn, one pending-confirmation turn, one proposal blocked by adapter validation, and one provider-model-blocked turn.
- Write `docs/live-failure-reports/sprint-020-harness-live-proposal.md` with `not_configured` unless a coordinator-supplied live artifact is available.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

## Lane B - Provider Live Mission Proposal Runner

Owner: Kaneki

Branch: `sprint-020/provider-live-mission-proposal-runner`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`
- `docs/live-failure-reports/**`

Deliverables:

- Add a provider-local live mission proposal runner over `IModelCompletionClient` plus optional `IProviderCreditClient`.
- Use strict JSON/schema response shape for the first end-user mission proposal path. The output should map cleanly into the harness live proposal envelope without provider-to-harness references.
- Reuse the guarded OpenRouter/Qwen path first; add OpenAI-compatible and Grok/xAI-compatible descriptors only where they can stay disabled-by-default and redacted.
- Validate packet id, session id, role, output kind, mission kind, authority source, field paths, commitment kind, priority, pending confirmation shape, and unsupported/unsafe values before producing accepted rows.
- Fixed sanitized outcomes for disabled, key missing, credit exhausted, timeout, empty content, malformed JSON, schema invalid, packet mismatch, unsupported proposal value, unsafe value redacted, fallback disabled, provider unavailable, and accepted proposal.
- Add fake HTTP/unit coverage for all rejected paths and at least one accepted model proposal.
- If keys/config/credits are present, run one guarded live OpenRouter mission proposal smoke and record only sanitized metadata/outcomes. If not configured, report `blocked_by_guard`.
- Write `docs/live-failure-reports/sprint-020-provider-live-proposal.md`.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional guarded live smoke when configured; report fixed/sanitized outcome.

## Lane C - End-User Live Proposal UI

Owner: Sarah

Branch: `sprint-020/end-user-live-proposal-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`
- `docs/live-failure-reports/**`

Deliverables:

- Integrate Sprint 020 harness/provider heads after they are READY.
- Replace the UI-local "live role blocked/preflight only" send path with the live mission proposal runner plus `LiveSessionConductor` application path.
- Keep deterministic/offline as the default required-test path.
- In live mode, show whether the latest turn was `live_preflight`, `live_model_proposal_accepted`, `live_model_proposal_blocked`, `harness_validation_blocked`, or deterministic fallback.
- Preserve the current end-user interaction primitives: wide assistant interaction space, mood cards, selected-card user echo, compact planning timeline, bottom Ask affordance, `/trip` and `/stage-cockpit` route split, and media manifest hooks.
- Do not send raw transcript history to the model. The UI should send only the bounded request/projection data selected by the provider/harness contract.
- Browser smoke should prove deterministic send, live guard blocked state when config is absent, and one attempted live proposal path when config is present.
- Write `docs/live-failure-reports/sprint-020-end-user-live-proposal-ui.md`.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- in-app browser smoke on `/trip` and `/stage-cockpit`
- guarded live smoke when configured

## Integration Order

1. Shellby first: freeze the harness live proposal envelope/application behavior.
2. Kaneki second: freeze provider live mission proposal runner/result vocabulary and fake/live guard behavior.
3. Sarah third: merge both dependency heads and wire `/trip` through the live proposal path.
4. Collin final:
   - merge lanes into `main`;
   - run focused harness/provider/UI tests;
   - run full serial build/test if local locks permit;
   - browser smoke deterministic mode;
   - guarded live OpenRouter smoke if keys/credits are present;
   - publish sanitized findings.

## Exit Criteria

- The app can make one guarded live model call that returns a structured proposal for the harness to apply or reject.
- The harness owns validation and durable planning state; UI does not decide model proposal validity.
- The UI clearly labels deterministic fallback, live accepted, and live blocked states.
- At least one real provider attempt is either executed or blocked with an honest fixed guard reason.
- Model failures produce useful sanitized reports for the next sprint.
- No booking, hold, payment, spend, or irreversible side effect is possible.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, raw model completion, API key, credential, approval token, hold reference, candidate display sentinel, booking/payment reference, raw exception text, or secret-like value is persisted or rendered.
