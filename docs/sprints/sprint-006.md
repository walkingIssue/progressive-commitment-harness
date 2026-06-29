# Sprint 006 - Mission Intake And Structured Memory

Coordinator: Collin

Planning base: Sprint 005 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Start Stage 4 and structured memory by turning a messy user prompt into typed mission state, high-priority commitments, confirmation-ready fields, and a bounded digest that can be projected into small-model stage packets.

```text
rambling prompt
  -> provider/strong-model mission proposal or deterministic mock
  -> harness mission intake application
  -> authority-checked mission fields, constraints, commitments
  -> structured memory digest
  -> UI confirmation form and trace
```

Required tests stay deterministic and offline. Live provider calls remain optional, guarded, and blocked rather than falling back when output is empty, malformed, or credits fail.

## Stage Targets

| Stage | Sprint 006 target |
| --- | --- |
| Stage 2 | Mission/commitment facts appear in bounded projection and memory digest surfaces |
| Stage 3 | Mission intake applies only authority-approved state and records blocked/proposed fields |
| Stage 4 | Strong-planner-shaped proposal contract and deterministic planner mocks |
| Stage 5 | Small-model-facing memory digest remains compact and traceable |
| UI gate | Stage Cockpit or intake view can run a rambling prompt into structured mission fields and confirmation-ready proposals |
| Stage 9 precursor | First prompt-to-structured-state UI slice, still deterministic by default |

## Lanes

### Lane A - Harness Mission Intake And Memory Digest

Owner: Shellby

Branch: `sprint-006/harness-mission-intake-memory`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned mission-intake application/result shape for applying structured mission proposals.
- Authority-aware handling for user-stated facts, model-inferred fields, constraints, and high-priority commitments.
- Compact structured memory digest/projection surface that lists load-bearing mission facts, pending confirmations, and trace references.
- Deterministic tests for vacation, business, funeral/downtime, and helping-family scenarios.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Mission Planner Bridge

Owner: Kaneki

Branch: `sprint-006/provider-mission-planner-bridge`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local mission planner packet/result DTOs for prompt-to-structured-mission proposals.
- Deterministic mock planner outputs for the same scenario set as the harness lane.
- Sanitized eval rows that avoid raw prompt/provider payload persistence by default, while preserving coarse outcome codes and safe metadata.
- Optional guarded live smoke documentation for OpenAI/OpenRouter planner use without consuming credits in required tests.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live smoke result or explicit blocked/skipped reason

### Lane C - UI Mission Intake Slice

Owner: Sarah

Branch: `sprint-006/ui-mission-intake-slice`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- UI mission-intake panel for a rambling prompt with deterministic mock planner output by default.
- Server-side flow through provider mission planner bridge, harness mission intake, and structured memory digest.
- Confirmation-ready display for inferred fields versus user-stated facts.
- Stable `data-*` markers for applied fields, pending confirmations, commitments, memory digest facts, and sanitized blocked/proposed outcomes.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for vacation, non-vacation commitment, pending confirmation, and raw prompt/provider-payload absence in diagnostics

## Integration Order

1. Shellby first, because the mission intake and memory digest contracts become the shared safe state boundary.
2. Kaneki second, to align provider planner output and sanitized eval rows with the harness contracts.
3. Sarah third, to consume the mission intake and digest path in the UI/server vertical slice.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A rambling prompt can become structured mission/commitment state through a deterministic UI/server flow.
- Model-inferred facts are visibly separated from user-stated facts and do not silently become trusted anchors.
- The memory digest is bounded, traceable, and safe to feed into later small-model stage packets.
- No required test uses provider credentials or network.
- No raw provider payload, raw exception message, credential, approval token, or secret-like sentinel is persisted or rendered.

## Result

Status: complete.

Integrated heads:

- Shellby: `5b33c5555c37d80c9a30380dd7131fdd664fa8d4`
- Kaneki: `d2da57fe623e7f359dc9813d749b490f3f3c8f51`
- Sarah: `534f6b545b96db9709b6bf48a495e6f98d2debec`
- Final local integration before docs: `f5c876f42c204e3eb47c4305d48aa2276e5c9d7c`

What landed:

- `MissionIntakeApplication` applies structured mission fields, constraints, and commitments through authority checks.
- `StructuredMemoryDigest` bounds load-bearing mission facts, pending confirmations, and trace references.
- Provider mission planner results now preserve field paths, authority/evidence, structured constraints, and structured commitments while sanitized eval rows avoid raw mission content.
- Stage Cockpit mission intake runs provider DTOs through a UI adapter into `MissionIntakeApplication`, then renders applied fields, pending confirmations, high-priority commitments, and digest facts.

Final verification:

- `dotnet test`: passed, 99 tests.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke on `http://127.0.0.1:5134/`: vacation, high-priority commitment, and pending-confirmation paths passed through canonical mission intake and structured memory digest rendering.

Deferred hardening:

- Replace deterministic UI-owned mission planner source with a guarded provider planner/session endpoint.
- Add adapter-level max length and enum/value validation before accepting live provider output.
- Project `StructuredMemoryDigest` into small-model stage packets and measure budget stability.
