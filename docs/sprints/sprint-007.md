# Sprint 007 - Guarded Mission Planner Runtime

Coordinator: Collin

Planning base: Sprint 006 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn the deterministic mission-intake seam into a guarded provider-backed mission planner runtime, while keeping required tests offline and preserving the authority boundary from Sprint 006.

```text
rambling prompt
  -> provider mission planner packet
  -> deterministic mock or guarded live provider result
  -> validated mission proposal adapter
  -> MissionIntakeApplication
  -> StructuredMemoryDigest
  -> StagePacket mission memory projection
```

Live provider smoke remains optional. It must check key presence, credits/provider health, strict timeouts, empty/malformed output, and no paid fallback.

## Stage Targets

| Stage | Sprint 007 target |
| --- | --- |
| Stage 2 | Project mission digest facts and pending confirmations into bounded `StagePacket` surfaces |
| Stage 4 | Add a guarded mission planner runtime path with deterministic mocks and optional live provider smoke |
| Stage 5 | Feed compact structured memory into small-model-facing packets without raw prompt history |
| Stage 6 | Add sanitized mission-planner eval rows for adapter/runtime outcomes |
| UI gate | Stage Cockpit can run mission planning through the runtime path and show adapter/provider/intake outcomes |
| Stage 9 precursor | First prompt-to-mission runtime loop with future live-provider swap point |

## Lanes

### Lane A - Harness Mission Proposal Adapter And Projection

Owner: Shellby

Branch: `sprint-007/harness-mission-proposal-adapter`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned mission proposal adapter/application boundary that accepts provider-shaped mission proposals without depending on `Pch.Providers`.
- Explicit validation for max string lengths, allowed mission field paths, evidence id count, constraints, commitments, priority/source translation, and fixed sanitized failure codes.
- Projection update so `StructuredMemoryDigest` facts and pending confirmations appear in bounded `StagePacket` facts for the current stage.
- Deterministic tests for accepted vacation, high-priority non-vacation commitment, model-inferred pending fields/constraints, overlong payload rejection, unknown field path, and no mutation on adapter failure.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Mission Planner Runtime

Owner: Kaneki

Branch: `sprint-007/provider-mission-planner-runtime`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider mission planner runtime/client surface that returns the existing provider-local mission planner DTOs.
- Deterministic mock runtime remains the required-test default.
- OpenAI/OpenRouter-compatible optional live smoke wrapper with key/credit/timeout guards, empty/malformed output blocking, no fallback, and no raw prompt/payload persistence.
- Sanitized eval rows for runtime/adapter handoff outcomes, including provider/model/request metadata, response length, counts, and fixed error codes only.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live smoke result or explicit skipped/blocked reason

### Lane C - UI Runtime Mission Planner

Owner: Sarah

Branch: `sprint-007/ui-runtime-mission-planner`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit mission intake panel uses a server-side runtime mission planner service as the primary path.
- Deterministic mock provider output still powers required tests and smoke.
- Render distinct provider runtime, adapter validation, mission intake, and memory digest outcome markers.
- Preserve the existing vacation, high-priority commitment, pending-confirmation, and raw-sentinel absence smoke paths.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for runtime mission planner accepted, pending-confirmation, validation-blocked, and raw prompt/provider-payload absence

## Integration Order

1. Shellby first, because adapter validation and digest projection become the safe shared boundary.
2. Kaneki second, to align provider runtime/eval outputs with the adapter boundary.
3. Sarah third, to consume the runtime path in the UI/server vertical slice.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A mission planner result can be validated and applied through a reusable runtime boundary.
- Overlong, unsupported, or malformed mission proposal data blocks with fixed sanitized codes and no session mutation.
- `StagePacket` projection includes bounded mission memory facts and pending confirmations.
- The UI can run the mission planner runtime path with deterministic mocks and stable outcome markers.
- No required test uses provider credentials or network.
- No raw prompt, provider payload, proposal JSON, approval token, credential, or secret-like sentinel is persisted or rendered.
