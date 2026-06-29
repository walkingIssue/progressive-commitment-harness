# Sprint 004 - Model Action Bridge And UI Suggested Step

Coordinator: Collin

Planning base: Sprint 003 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Bridge provider-local model action output into harness-owned action intake safely, then expose one deterministic UI path for a model-suggested next action.

```text
StagePacket
  -> provider-local ModelActionPacket
  -> model action result
  -> sanitized external action proposal
  -> harness decoder/intake
  -> session trace / blocked-or-accepted UI state
```

Default tests stay provider-free. Live Qwen remains optional and must stop on empty content, credit exhaustion, or malformed output without falling back to another paid model.

## Stage Targets

| Stage | Sprint 004 target |
| --- | --- |
| Stage 3 | External action proposals enter only through validated decode and `HarnessActionIntake` |
| Stage 5 | Provider-local model output can be mapped into a harness-owned proposal shape |
| Stage 6 | Golden eval checks include decode/intake outcome, not only action-name validity |
| UI gate | Stage Cockpit can show and apply a deterministic model-suggested action through the harness path |
| Stage 10 precursor | Sanitized error codes and no raw payload persistence hold across bridge failures |

## Lanes

### Lane A - Harness Proposal Decoder

Owner: Shellby

Branch: `sprint-004/harness-action-decoder`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned external action proposal shape for decoded model/provider output.
- Decoder/validator that maps action discriminator plus flat JSON arguments into canonical `HarnessAction` values.
- Schema-level validation for every action kind used in current stages.
- Sanitized decode errors with fixed codes and no raw argument or raw action text echo.
- Tests for malformed JSON, missing required fields, unknown kind, disallowed stage, approval mismatch, and no session mutation on decode/intake failure.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - UI Model Suggested Action Step

Owner: Sarah

Branch: `sprint-004/ui-model-suggested-action`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit panel for one "model suggested action" using deterministic mock data by default.
- Applying a suggestion must route through server-side harness decode/intake or a UI service seam that is ready for that integration.
- Rejected/blocked suggestions must render fixed error codes and `data-blocked-reason` without raw provider payloads.
- Preserve session ID, action kind, candidate/approval IDs, and trace outcome markers in markup.
- Tests for accepted mock suggestion and blocked/disallowed suggestion.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for suggested action accepted/blocked markers

### Lane C - Provider Action Bridge And Eval Rows

Owner: Kaneki

Branch: `sprint-004/provider-action-bridge-eval`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local adapter from `ModelActionRunResult` into the harness proposal shape once Shellby's contract is available, or a provider-local mirror if Shellby is still in review.
- Golden packet eval rows that include action-name validity plus decode/intake outcome code.
- Optional live OpenRouter Qwen smoke wrapper that records only sanitized metadata and blocks on empty content/credit exhaustion.
- Tests proving raw prompt text, raw model response text, raw exception messages, and secrets are not persisted.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live smoke result or explicit blocked reason

## Integration Order

1. Shellby first if the proposal contract or decoder public surface changes.
2. Kaneki after Shellby if he consumes the harness proposal contract; otherwise provider-local eval work can merge independently.
3. Sarah after Shellby if the UI consumes the decoder/intake directly; otherwise UI service seam can merge after review.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A model/provider action cannot mutate state unless it decodes into a valid canonical `HarnessAction` and passes `HarnessActionIntake`.
- Eval rows report decode/intake outcomes without raw payload persistence.
- The UI can display and apply a deterministic suggested action through the same blocked/accepted path users will later exercise with a small model.
- No required test uses provider credentials or network.

## Result

Status: done in Sprint 004.

- Shellby added harness-owned `ExternalActionProposal`, `ExternalActionDecoder`, sanitized decode outcomes, no-mutation decode/intake failure tests, and approval-token dropping for external/provider approval proposals.
- Kaneki added provider-local bridge/eval rows with decode/intake outcome codes and sanitized metadata only; live OpenRouter/Qwen remained blocked after guarded provider failure.
- Sarah repaired the model-suggested UI path so deterministic suggestions route through `ExternalActionDecoder` plus `HarnessActionIntake`, with accepted, blocked, and malformed proposal states rendered through sanitized markers.

Final verification:

- `dotnet test`: 73 tests passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke passed for accepted `defer_slot`, blocked `handoff`, malformed JSON, and no raw sentinel page leakage.
