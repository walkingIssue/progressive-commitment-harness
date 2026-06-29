# Sprint 012 - Replay, Release Smoke, And Redaction Hardening

Coordinator: Collin

Planning base: Sprint 011 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Harden the deterministic end-to-end trip run so it can be repeated, inspected, and published safely.

Sprint 012 should not introduce live booking, payment, search, or required provider-credit paths. The goal is release-quality repetition: replay corpora, sanitized eval artifacts, stable UI smoke markers, and redaction audits across the existing canonical flow.

## Stage Targets

| Stage | Sprint 012 target |
| --- | --- |
| Stage 6 | Consolidate sanitized eval artifact behavior across provider lanes |
| Stage 9 | Make the end-to-end UI run repeatable as a release smoke slice |
| Stage 10 | Add replay, redaction, and release-readiness checks around the deterministic trip run |

## Lanes

### Lane A - Harness Replay And Snapshot Audit

Owner: Shellby

Branch: `sprint-012/harness-replay-audit`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned replay/audit corpus for trip-run snapshots across vacation, business, funeral/downtime, family-support, blocked-candidate, pending-confirmation, and missing-approval scenarios.
- Deterministic replay result codes and bounded trace/evidence references for accepted and blocked snapshots.
- Tests proving replay is read-only, deterministic, bounded, and free of raw prompts, provider payloads, approval tokens, hold references, credentials, candidate display text, and sentinels.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Sanitized Eval Artifact Audit

Owner: Kaneki

Branch: `sprint-012/provider-sanitized-eval-artifacts`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-side redaction/audit helper or shared test fixture for sanitized eval/status rows across model action, mission planner, candidate expansion, hold preparation, and evidence export.
- Deterministic tests proving rejected/error rows omit raw provider payloads, prompts, approval tokens, hold references, candidate display values, credentials, raw exception messages, and sentinels.
- Updated eval docs that define the persisted-safe fields and fixed outcome-code policy.
- Optional live smoke remains skipped/blocked by default and must not consume credits in required tests.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

### Lane C - UI Release Smoke And Accessibility Markers

Owner: Sarah

Branch: `sprint-012/ui-release-smoke-a11y`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit release-smoke surface for the deterministic end-to-end run summary, preserving stable `data-*` markers for all accepted/blocked paths.
- UI tests for end-to-end result summary stability, raw-sentinel absence, and keyboard/accessibility-friendly controls around the run cards.
- Browser smoke remains deterministic/offline and covers happy path, pending confirmation, provider mismatch, wrong-slot candidate, missing approval, evidence export markers, and no raw text.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for release-run markers and raw absence

## Integration Order

1. Shellby first, because replay/audit contracts define the release snapshot corpus.
2. Kaneki second, because provider eval artifact audit should align with replay/export outcomes.
3. Sarah third, because the UI release smoke consumes the stable replay/export vocabulary.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- The deterministic end-to-end run is replayable across several trip postures without mutation or raw-data leakage.
- Provider eval artifacts share a documented persisted-safe field policy.
- The UI has stable release-smoke markers and tests around the final trip-run summary.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, proposal JSON, credential, approval token, hold reference, payment data, exception text, or secret-like sentinel is persisted or rendered.
