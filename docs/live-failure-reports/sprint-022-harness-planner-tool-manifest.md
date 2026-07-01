# Sprint 022 Harness Planner Tool Manifest Report

## Status

live/browser spike status: `not_configured`

Shellby implemented the harness/Core planner primitive manifest and validation boundary with deterministic/offline tests only. UI integration and in-context browser live smoke are owned by the UI/provider integration lanes, so this lane did not run browser or provider calls.

## Changed Surfaces

- `src/Pch.Core/PlannerPrimitiveContracts.cs`
- `src/Pch.Harness/PlannerToolManifestCompiler.cs`
- `tests/Pch.Core.Tests/PlannerPrimitiveContractsTests.cs`
- `tests/Pch.Harness.Tests/PlannerToolManifestCompilerTests.cs`

## Contract Names

- `PlannerPrimitiveIds`
- `PlannerMoodTokens`
- `PlannerAnswerSchemaKinds`
- `PlannerAnswerSchema`
- `PlannerPrimitiveDefinition`
- `PlannerCompositeFormDefinition`
- `PlannerToolManifest`
- `PlannerPrimitiveInstance`
- `PlannerPrimitiveTurnProposal`
- `ValidatedTurnView`
- `ValidatedPrimitiveView`
- `PlannerToolManifestCompiler`
- `PlannerPrimitiveValidator`
- `PlannerPrimitiveValidationResult`

## Fixed Outcome Codes

- `primitive_turn_accepted`
- `awaiting_user_input`
- `tool_search_requested`
- `tool_gap_review_required`
- `primitive_validation_blocked`
- `invalid_manifest`
- `primitive_not_supported`
- `primitive_not_allowed_for_stage`
- `field_path_not_allowed`
- `answer_schema_invalid`
- `stale_graph_revision`
- `ownership_invalid`
- `primitive_metadata_redacted`
- `approval_required`

## Data Flow Summary

```text
TripSession + PlanningDependencySnapshot
  -> PlannerToolManifestCompiler
  -> PlannerToolManifest
  -> provider/model primitive proposal
  -> PlannerPrimitiveValidator
  -> ValidatedTurnView
  -> UI/provider lanes render or diagnose only after validation
```

The compiler derives graph revision from `PlanningEditImpactAnalyzer`, bounds allowed primitives by harness stage, and includes allowed field paths, slot ids, candidate ids, task ids, mood tokens, media tokens, and approval/spend restrictions.

## Failures And Valid Responses Implemented

- null or malformed manifest/proposal -> `invalid_manifest`
- unknown primitive id -> `primitive_not_supported`
- known primitive not allowed for current stage -> `primitive_not_allowed_for_stage`
- invalid field path -> `field_path_not_allowed`
- invalid answer schema or missing required answer -> `answer_schema_invalid`
- stale graph revision -> `stale_graph_revision`
- unknown slot/candidate/task or candidate not owned by slot -> `ownership_invalid`
- unsafe prompt/provider/credential/media/CSS/sentinel value -> `primitive_metadata_redacted`
- approval/spend-adjacent primitive without approval/spend allowance -> `approval_required`
- model requests tool search -> `tool_search_requested`, no mutation
- model reports tool gap -> `tool_gap_review_required`, no mutation

## Security Notes

- No provider/UI references were added.
- No provider/network/browser/search/booking/payment calls were added.
- Unsafe raw prompts, provider payload sentinels, credentials, CSS/media URLs, approval tokens, hold refs, booking/payment refs, and secret-like values block before a `ValidatedTurnView` can carry a primitive.
- `ValidatedTurnView` carries sanitized ids, renderer keys, bounded evidence refs, task/timeline refs, and sanitized primitive fields only.

## Deferred Hardening

- Provider lane should map strict JSON output into `PlannerPrimitiveTurnProposal`.
- UI lane should render only `ValidatedTurnView` and should not treat deterministic seeded cards as live output.
- Browser spike remains blocked by scope here and should be completed after Sarah/Kaneki integrate this contract.
