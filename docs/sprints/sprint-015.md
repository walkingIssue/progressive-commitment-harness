# Sprint 015 - End-User Chat UI and Golden Trace Safety Net

Coordinator: Collin

Planning base: Sprint 014 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Make the harness testable by an end user without discarding the engineering cockpit.

Sprint 015 should introduce a real chat/transcript surface for trying a trip-planning run, plus a deterministic golden turn trace harness so the new UI can evolve without losing the safety and redaction guarantees built so far.

## Stage Targets

| Stage | Sprint 015 target |
| --- | --- |
| Stage 2/3/9 | A user-facing chat/transcript shell over the deterministic trip-flow boundaries |
| Stage 10 | Golden turn trace replay and sanitized regression artifacts |
| Stage 5/6 | Model-role/provider guardrails for offline-first UI behavior |

## Lanes

### Lane A - Golden Turn Trace Harness

Owner: Shellby

Branch: `sprint-015/golden-turn-trace-harness`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/harness/**` if needed

Deliverables:

- Harness-owned trace script DTOs for deterministic user, assistant, harness, decision, blocked, and evidence turns.
- A trace runner that can execute at least one happy-path trip planning script and one blocked/safety script without provider/network calls.
- Golden JSON fixtures with sanitized transcript output, stable turn ids, stage/projection markers, evidence refs, and fixed outcome codes.
- Shared sanitizer behavior for raw prompt/provider payload/credential/approval-token/hold-reference/candidate-display sentinels in trace artifacts.
- Tests proving deterministic replay, no mutation when replay is read-only, and stable golden output.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Model Role Guardrails

Owner: Kaneki

Branch: `sprint-015/model-role-guardrails`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local model role registry or DTOs for deterministic/offline, small-model, strong-model, and live-provider-disabled states.
- Sanitized provider status/eval artifacts that explain which role would be used by the end-user UI without invoking live providers by default.
- Deterministic fakes for role availability, blocked live provider, malformed provider role config, and explicit fallback-disabled behavior.
- Docs explaining how the end-user UI can label model roles without exposing keys, prompts, provider payloads, or raw errors.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

### Lane C - End-User Chat UI V0

Owner: Sarah

Branch: `sprint-015/end-user-chat-ui-v0`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- A first-screen end-user trip-planning UI with a prompt input, visible chat/transcript turns, deterministic send action, and a clear offline/deterministic mode indicator.
- Transcript rendering for user prompt metadata, assistant/harness response, mission facts, itinerary/candidate decisions, blocked approvals, and final status.
- Integration with canonical deterministic harness/provider boundaries already available on `main`; Stage Cockpit remains available as the engineering dashboard but is no longer the only useful try-it surface.
- Stable `data-*` markers and accessibility labels for prompt entry, send, transcript turns, blocked state, final state, raw absence, and deterministic/offline mode.
- UI tests and browser smoke covering at least happy path, blocked approval/safety path, and raw sentinel absence.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for the new end-user chat flow

## Integration Order

1. Shellby first, so the UI has deterministic turn fixtures and golden trace vocabulary.
2. Kaneki second, so the UI can display model role/fallback status without live provider calls.
3. Sarah third, so the end-user UI can consume the canonical trace and model-role vocabulary where available.
4. Collin final verification:
   - `npm run build:ui`
   - `dotnet build src/Pch.UI/Pch.UI.csproj`
   - `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
   - `dotnet test`
   - `dotnet build`
   - interactive UI smoke

## Exit Criteria

- A non-engineer can open the app, enter a prompt, send it, and understand the resulting deterministic trip-planning transcript without reading Stage Cockpit internals.
- Golden traces cover at least one happy path and one blocked/safety path and are stable enough to protect future UI refactors.
- Required tests and smoke remain offline and deterministic.
- No live provider, search, booking, payment, credential, API key, or provider-credit dependency is introduced in the default path.
- No raw prompt text, provider payload, proposal JSON, credential, approval-token value, payment data, live booking reference, candidate display value, raw exception text, or secret-like sentinel is persisted or rendered outside intentionally transient in-memory request channels.

