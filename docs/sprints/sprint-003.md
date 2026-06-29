# Sprint 003 - UI-Backed Session And First Model Smoke

Coordinator: Collin

Planning base: `b20ca29b47ca840e1108eeab0ed5a232eff653c5`

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn Sprint 002's deterministic pieces into the first UI-backed harness run, and begin the small-model feasibility test over golden packets.

```text
Stage Cockpit UI
  -> server-side session service
  -> Pch.Harness SessionLoop
  -> StagePacket / blocked result / decision ledger
  -> UI response states and trace preview
  -> provider-local model-action smoke/eval over golden packets
```

No real booking, no Amadeus live calls, no production payments, and no required live-provider calls in default tests.

## Stage Targets

| Stage | Sprint 003 target |
| --- | --- |
| Stage 2/3 | Freeze a narrow action-intake/session-service boundary and expose replayable deterministic session turns |
| UI feasibility gate | Stage Cockpit uses server-side harness session state for form, choice, approval, blocked, and trace behavior |
| Stage 5 | Run the first credit-guarded hosted small-model action smoke over packet-shaped prompts if provider checks pass |
| Stage 6 | Produce the first eval rows for schema validity, allowed-action preservation, latency, and fallback/block reason |
| Stage 9 precursor | Demonstrate a provider-free end-to-end UI session path from user response to updated packet/evidence state |

## Lanes

### Lane A - Harness Action Intake And Replay Boundary

Owner: Shellby

Branch: `sprint-003/harness-action-intake`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned action intake DTO or mapper boundary for model/provider-selected actions.
- Validation that only allowed action kinds and stage-appropriate arguments can enter `SessionLoop`.
- Replayable session turn result shape suitable for UI and eval traces.
- Tests for disallowed action kind, mismatched candidate IDs, mismatched approval IDs, and no partial state writes on blocked turns.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Blazor Harness Session Wiring

Owner: Sarah

Branch: `sprint-003/ui-harness-session-wiring`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**` if added

Deliverables:

- Server-side session service inside `Pch.UI` that wraps current harness session loop.
- Stage Cockpit reads from server-backed session state instead of UI-only response fixtures for form/choice/approval/blocked transitions.
- UI surfaces blocked results explicitly and preserves candidate/approval/session IDs in markup.
- Fixture fallback may remain, but the primary smoke path must use the server-side harness service.
- Add component/server tests if practical; otherwise provide a stronger HTTP smoke marker set.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- optional `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj` if tests are added
- UI HTTP smoke

### Lane C - Qwen Packet Eval Smoke

Owner: Kaneki

Branch: `sprint-003/qwen-packet-eval-smoke`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Eval runner can load golden packet JSON and produce sanitized eval rows using deterministic mocks.
- Credit-guarded OpenRouter `qwen/qwen3-14b` smoke over one or two golden packets if key and credits are available.
- Live smoke output must be summarized/sanitized; do not persist raw model payloads or prompt content that could contain personal data.
- If credits are exhausted or provider checks fail, report `BLOCKED` for live smoke instead of falling back to another paid provider.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- live smoke result or explicit skipped/blocked reason

## Integration Order

1. Shellby first if action-intake contracts change.
2. Sarah after Shellby if she consumes new harness boundary.
3. Kaneki can run in parallel if he remains provider-local and consumes golden packets read-only.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - UI HTTP smoke

## Exit Criteria

- UI can drive a deterministic server-backed session turn without provider credentials.
- Blocked outcomes are explicit and cause no partial state writes.
- Golden packet eval can run with mocks.
- Optional Qwen smoke is either completed safely or blocked with a clear provider/credit reason.
- Production quality bar remains satisfied.

## Outcome

Status: completed.

- Shellby added `HarnessActionIntake`, stage-aware action validation, replayable `SessionTraceEvent` output, and sanitized unknown-action trace handling.
- Sarah wired Stage Cockpit to a scoped server-side `Pch.Harness` session service and added a UI test plus interactive smoke for the approval-bypass repair.
- Kaneki added golden packet eval loading, sanitized eval rows, and repaired legacy eval errors so raw exception text is not persisted.
- Collin added `tests/Pch.UI.Tests` to the solution so root `dotnet test` covers the UI invariant.

## Verification Result

- `dotnet test`: passed, 59 tests.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Interactive UI smoke: passed. `Request approval stage` followed by `Apply form` produced one blocked response with `data-blocked-reason="Cannot apply form while pending harness action is request_approval."`
- Optional OpenRouter Qwen smoke: blocked after key and credit guard because `qwen/qwen3-14b` returned empty content; no fallback provider used.
