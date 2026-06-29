# Sprint 011 - End-To-End Trip Run And Evidence Export

Coordinator: Collin

Planning base: Sprint 010 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn the separate deterministic slices into one visible end-to-end trip-planning run:

```text
prompt packet
  -> mission intake and structured memory
  -> itinerary slot compilation
  -> candidate expansion and slot decisions
  -> approval-gated mock hold preparation
  -> final evidence/export packet
```

Required tests stay deterministic and offline. No real booking, payment, live search, or live availability API is allowed in required paths.

## Stage Targets

| Stage | Sprint 011 target |
| --- | --- |
| Stage 3 | Compose existing harness boundaries into one safe trip-run application result |
| Stage 5 | Feed compact structured memory and itinerary facts forward instead of raw prompt/history |
| Stage 6 | Add sanitized export/evidence rows for end-to-end trip runs |
| Stage 8 | Summarize selected/deferred itinerary decisions and mock hold status into final plan state |
| Stage 9 | First deterministic end-to-end UI run from prompt fixture to evidence packet |

## Lanes

### Lane A - Harness Trip Run Snapshot

Owner: Shellby

Branch: `sprint-011/harness-trip-run-snapshot`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned trip-run snapshot/result contracts that compose mission facts, memory digest, itinerary compilation, selected/deferred itinerary decisions, approval/hold status placeholders, and replay trace references.
- Deterministic result codes for complete, pending-confirmation, blocked-candidate, blocked-approval, and blocked-compiler outcomes.
- Bounded evidence/export references with no raw prompt, provider payload, approval token, hold reference, credential, or candidate display text by default.
- Tests proving accepted and blocked trip-run snapshots are replayable, bounded, sanitized, and mutation-safe.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Evidence Export Bridge

Owner: Kaneki

Branch: `sprint-011/provider-evidence-export-bridge`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local evidence/export DTOs for final trip-plan summaries, selected/deferred candidate counts, mock hold outcomes, provider/model/request metadata, and fixed outcome codes.
- Deterministic exporter/mock source for required tests; no network, booking, or payment side effects.
- Sanitized eval/status rows proving raw prompts, provider payloads, approval tokens, hold references, candidate display names, credentials, and sentinel values are not persisted.
- Mapping notes for future live/search/export adapters and no paid-provider fallback.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live path skipped/blocked by default

### Lane C - UI End-To-End Trip Run

Owner: Sarah

Branch: `sprint-011/ui-end-to-end-trip-run`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit end-to-end run panel that executes a deterministic prompt fixture through canonical prompt packet, mission runtime/intake, itinerary compiler, provider candidate expansion, canonical slot decisions, mock hold preparation, and final evidence/export summary.
- Stable markers for run id, prompt packet outcome, mission outcome, itinerary outcome, selected/deferred counts, hold outcome, approval id, evidence packet id, sanitized error code, and raw sentinel absence.
- Tests and browser smoke for happy path, pending-confirmation path, provider candidate mismatch, wrong-slot candidate, missing approval, and no raw prompt/provider payload/approval-token/hold-reference leakage.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for end-to-end happy path, blocked variants, evidence packet markers, and raw absence

## Integration Order

1. Shellby first, because the trip-run snapshot defines the harness result and trace shape.
2. Kaneki second, because provider export rows should align to the snapshot and hold/candidate outcome vocabulary.
3. Sarah third, because the UI consumes both upstream boundaries.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A deterministic prompt fixture can produce a final trip-run result through canonical prompt, mission, itinerary, candidate, and hold-prep boundaries.
- The UI can render a final evidence/export bundle without implying real booking/payment happened.
- Blocked cases are explicit, fixed-code, replayable, and no-mutation where relevant.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, proposal JSON, credential, approval token, hold reference, payment data, or secret-like sentinel is persisted or rendered.

## Result

Status: done.

Worker heads:

- Shellby: `2a80f76a8fb9463f20d8691e3a9da26f3a166a13`
- Kaneki: `f475bd223c7d2b6854b587f431b42d3c966ec949`
- Sarah: `e40b92e1466962b05d51b1f358e96f784f0e816f`

Coordinator merges:

- `cb32cbf` - provider evidence export bridge
- `598d36e` - harness trip-run snapshot
- `3704af6` - UI end-to-end trip run

Completed:

- Harness-owned `TripRunSnapshotBuilder` for final deterministic trip-run snapshots.
- Provider-local evidence export DTOs, deterministic mock provider, evaluator, and sanitized evidence-export docs/evals.
- Stage Cockpit end-to-end run panel through prompt packet, mission intake, itinerary compiler, candidate application, mock hold preparation, trip-run snapshot, and evidence export.
- Happy, pending-confirmation, provider candidate mismatch, wrong-slot candidate, missing-approval, and raw-sentinel absence paths.

Repairs before merge:

- Snapshot ids and top-level session ids are sanitized before serialization.
- Evidence export rows derive persisted final evidence metadata from the trusted packet after consistency validation.
- UI final run consumes canonical snapshot/export boundaries instead of a local final-export seam.

Verification:

- `npm run build:ui`: passed.
- `dotnet build src/Pch.UI/Pch.UI.csproj`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`: 36 tests passed.
- `dotnet test`: 232 tests passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- Coordinator browser smoke on `http://127.0.0.1:5163/`: all six end-to-end cards clicked and passed expected markers: happy path, pending confirmation, provider candidate mismatch, wrong-slot candidate, missing approval, raw-sentinel export, evidence marker presence, and raw sentinel absence.
