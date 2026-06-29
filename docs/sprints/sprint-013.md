# Sprint 013 - Fidelity Bake-Off And Ownership Matrix

Coordinator: Collin

Planning base: Sprint 012 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Start the original Stage 6 fidelity bake-off using the deterministic trip-run machinery now available.

Sprint 013 should not add required live provider, search, booking, payment, or credit-consuming paths. The goal is to produce a trustworthy ownership matrix: which stages can be handled by small models, which need strong-model fallback, and which should stay harness-only.

## Stage Targets

| Stage | Sprint 013 target |
| --- | --- |
| Stage 5 | Define deterministic small-model candidate/eval surfaces without trusting them for mutation |
| Stage 6 | Produce fidelity metrics and a first stage ownership matrix |
| Stage 9/10 | Render and smoke-test the release-readiness/fidelity summary in the Stage Cockpit |

## Lanes

### Lane A - Harness Fidelity Matrix

Owner: Shellby

Branch: `sprint-013/harness-fidelity-matrix`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned fidelity/ownership matrix contracts over deterministic stage packets and trip-run replay cases.
- Metrics for schema validity, faithfulness, candidate-id preservation, unsupported-claim count, fallback need, read-only behavior, and mutation safety.
- Fixed ownership outcomes such as `harness_only`, `small_model_candidate`, `strong_model_required`, and `blocked_until_review`.
- Tests proving deterministic matrix output, bounded evidence/trace refs, no mutation, and no raw prompt/provider payload/credential/sentinel leakage.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Fidelity Eval Artifacts

Owner: Kaneki

Branch: `sprint-013/provider-fidelity-eval-artifacts`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-side sanitized fidelity eval rows that can compare small-model, strong-model, and harness-only candidate outputs.
- Deterministic mock eval sources for schema-valid, schema-invalid, unsupported-claim, missing-candidate-id, timeout/provider-error, and fallback-required cases.
- Shared redaction coverage using the Sprint 012 sanitized artifact policy.
- Optional Ollama/OpenRouter/OpenAI live checks remain disabled/skipped by default and must be key/health/credit/timeout guarded.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

### Lane C - UI Fidelity Release Dashboard

Owner: Sarah

Branch: `sprint-013/ui-fidelity-release-dashboard`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit release-readiness/fidelity panel showing the ownership matrix, replay coverage, fallback counts, blocked stages, and sanitized eval artifact status.
- Stable `data-*` markers for browser smoke: matrix state, stage ownership, fallback count, schema-validity count, unsupported-claim count, release gate state, and raw-absence state.
- UI tests for stable rendering, keyboard-accessible controls, and raw-sentinel absence.
- Deterministic/offline smoke covering at least one harness-only, one small-model-candidate, one strong-model-required, and one blocked-until-review row.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for fidelity/release markers and raw absence

## Integration Order

1. Shellby first, because the ownership matrix is the canonical harness contract.
2. Kaneki second, because provider eval artifacts should align with the matrix vocabulary.
3. Sarah third, because the UI should consume the stable harness/provider vocabulary where available.
4. Collin final verification:
   - `npm run build:ui`
   - `dotnet build src/Pch.UI/Pch.UI.csproj`
   - `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
   - `dotnet test`
   - `dotnet build`
   - interactive UI smoke

## Exit Criteria

- A deterministic Stage 6 ownership matrix exists and is tested.
- Provider fidelity eval rows follow the Sprint 012 sanitized artifact policy.
- The UI exposes release-readiness/fidelity markers suitable for browser smoke and later CI.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, proposal JSON, credential, approval token, hold reference, exception text, candidate display value, or secret-like sentinel is persisted or rendered.
