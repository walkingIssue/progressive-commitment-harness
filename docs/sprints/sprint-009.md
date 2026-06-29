# Sprint 009 - Itinerary Slot Compiler And Candidate Pools

Coordinator: Collin

Planning base: Sprint 008 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn prompt-derived mission memory into the first itinerary planning surface:

```text
structured mission memory
  -> bounded day/slot skeleton
  -> deterministic candidate expansion
  -> candidate pools with evidence
  -> UI slot planner cards
  -> selected/deferred/blocked outcomes
```

Required tests stay deterministic and offline. Live search, booking, pricing, and external availability are explicitly deferred unless a lane adds a disabled-by-default guarded seam.

## Stage Targets

| Stage | Sprint 009 target |
| --- | --- |
| Stage 2 | Project day slots and candidate pool counters into bounded packet facts |
| Stage 4 | Introduce provider-shaped candidate expansion outputs for dining/activity/transit options |
| Stage 5 | Give the small-model-facing harness compact slot context rather than raw itinerary history |
| Stage 6 | Add sanitized candidate expansion eval rows with counts/codes/provider metadata only |
| Stage 8 | First day/slot compiler with sleep, meals, commitments, downtime, and conflict markers |
| UI gate | Stage Cockpit can run a deterministic itinerary day planner slice with slot/candidate/blocked markers |

## Lanes

### Lane A - Harness Itinerary Slot Compiler

Owner: Shellby

Branch: `sprint-009/harness-itinerary-slot-compiler`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned day/slot compiler contracts for turning mission facts, date windows, traveler requirements, pending confirmations, and commitments into bounded planning slots.
- Slot types for sleep, meal, transit, fixed commitment, downtime, activity, and unresolved confirmation.
- Conflict/blocked result shape for impossible or underspecified day skeletons.
- Projection update so bounded slot counts and conflict counts can appear in `StagePacket.LoadBearingFacts`.
- Deterministic tests for vacation day, family-support day, business day, funeral/downtime day, fixed commitment conflicts, missing date window, and no raw prompt/provider payload persistence.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Candidate Expansion Bridge

Owner: Kaneki

Branch: `sprint-009/provider-candidate-expansion-bridge`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local candidate expansion packet/result DTOs for dining, activity, transit, and downtime options tied to itinerary slot ids.
- Deterministic candidate source for required tests; no network/search/booking dependency by default.
- Sanitized candidate expansion eval rows that persist slot ids, candidate counts, categories, fixed outcome/error codes, provider/model/request metadata, and no raw prompt/provider payload/content values by default.
- Guard documentation for future strong-model/web-search expansion, including no paid-provider fallback and no credential/payload leakage.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional guarded-live seam skipped/blocked by default

### Lane C - UI Itinerary Day Planner

Owner: Sarah

Branch: `sprint-009/ui-itinerary-day-planner`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit itinerary day planner panel that starts from deterministic prompt-derived mission memory.
- Server-side service path through harness day/slot compiler, deterministic provider candidate expansion, and rendered slot/candidate outcomes.
- Stable markers for day id, slot id/type, slot state, candidate pool id, candidate id/category, selected/deferred/blocked outcome, evidence ids, and sanitized error codes.
- Tests and interactive smoke for accepted day skeleton, candidate pool rendering, fixed commitment conflict, missing/blocked date window, and raw prompt/provider payload absence.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for day planner accepted, candidate pools, conflict blocked, missing date blocked, and raw sentinel absence

## Integration Order

1. Shellby first, because slot compiler contracts define what provider candidate expansion and UI may consume.
2. Kaneki second, because candidate expansion should align to slot ids and sanitized outcome policy.
3. Sarah third, because the UI day planner consumes both upstream lanes.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A structured mission can produce a bounded day/slot skeleton without raw prompt history.
- Slot conflicts and missing prerequisites block with fixed sanitized codes and no partial mutation.
- Deterministic candidate pools can attach to itinerary slots with stable candidate ids and evidence markers.
- The UI can render slot skeletons, candidate pools, selected/deferred/blocked outcomes, and digest/evidence markers.
- No required test uses provider credentials, network, booking APIs, or live search.
- No raw prompt, provider payload, proposal JSON, credential, approval token, or secret-like sentinel is persisted or rendered.
