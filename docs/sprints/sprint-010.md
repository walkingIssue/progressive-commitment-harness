# Sprint 010 - Itinerary Candidate Application And Mock Holds

Coordinator: Collin

Planning base: Sprint 009 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn compiled itinerary slots and candidate pools into applied itinerary state plus a mocked, approval-gated hold preparation path:

```text
compiled slot skeleton
  -> trusted candidate pool
  -> selected/deferred slot decisions
  -> itinerary state snapshot
  -> mocked availability/hold preparation
  -> approval-gated hold result
  -> evidence trace
```

Required tests stay deterministic and offline. No real booking, payment, live search, or availability API is allowed in required paths.

## Stage Targets

| Stage | Sprint 010 target |
| --- | --- |
| Stage 3 | Apply selected/deferred itinerary candidate decisions through a harness-owned boundary |
| Stage 6 | Add sanitized eval/status rows for mocked hold preparation and candidate application outcomes |
| Stage 7 | Add mock availability/hold preparation adapters with approval-token gating only |
| Stage 8 | Preserve itinerary state as selected/deferred slots plus evidence references |
| UI gate | Stage Cockpit can select a candidate, defer a slot, request approval for a mock hold, and show blocked hold paths |

## Lanes

### Lane A - Harness Itinerary Application State

Owner: Shellby

Branch: `sprint-010/harness-itinerary-application-state`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned itinerary state/application contracts for applying selected or deferred candidates to compiled slot ids.
- Validation that selected candidates belong to the trusted slot and category; unknown slots/candidates block without partial mutation.
- Replayable trace/evidence entries for selected, deferred, and blocked slot decisions.
- Projection updates for bounded selected/deferred itinerary counts.
- Deterministic tests for accepted selection, defer, unknown slot, candidate-slot mismatch, category mismatch, blocked no-mutation, and sanitized diagnostics.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Mock Hold Preparation

Owner: Kaneki

Branch: `sprint-010/provider-mock-hold-preparation`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local mock availability/hold preparation DTOs tied to selected itinerary candidates.
- Deterministic mock adapter for hold preview, hold success, approval missing, packet mismatch, and provider unavailable paths.
- Approval-token gating for hold/commit-like operations; no real booking/payment/network side effects.
- Sanitized eval/status rows that persist fixed outcome/error codes, slot/candidate ids, provider/model/request metadata, and no raw payload, credential, approval token, or candidate display value by default.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live path skipped/blocked by default

### Lane C - UI Itinerary Selection And Mock Hold Flow

Owner: Sarah

Branch: `sprint-010/ui-itinerary-selection-hold-flow`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit itinerary panel can apply a candidate to a slot and defer a slot through harness itinerary application state.
- UI can request a mock hold preview/hold result through provider mock hold preparation and show approval-required/approved/blocked states.
- Stable markers for slot id, candidate id, selected/deferred state, approval id, hold outcome, evidence ids, and sanitized error codes.
- Tests and interactive smoke for accepted selection, defer, approval-required hold, approved mock hold, blocked missing approval, provider mismatch, and raw sentinel absence.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for select/defer/approval-required/approved-hold/blocked-hold/raw absence

## Integration Order

1. Shellby first, because itinerary application state defines what selected/deferred slot decisions mean.
2. Kaneki second, because mock hold preparation should bind to accepted itinerary state and approval-token rules.
3. Sarah third, because the UI consumes both upstream boundaries.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A compiled slot can receive a selected candidate through harness-owned validation.
- Deferred slots and blocked selection attempts are explicit, replayable, and mutation-safe.
- Mock hold preparation is approval-gated and deterministic/offline.
- The UI can show selected/deferred itinerary state and mocked hold outcomes without implying real booking happened.
- No raw prompt, provider payload, proposal JSON, credential, approval token, payment data, or secret-like sentinel is persisted or rendered.
