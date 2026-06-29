# Sprint 014 - Availability Preview Guardrails

Coordinator: Collin

Planning base: Sprint 013 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Begin Stage 7 availability and quote-preview work without adding real booking, payment, or default live-provider side effects.

Sprint 014 should create the trusted boundaries that make availability data safe to preview: harness ownership validation, provider-local adapter/eval artifacts, and a UI panel that can render deterministic quote states. Required tests and smoke remain deterministic/offline.

## Stage Targets

| Stage | Sprint 014 target |
| --- | --- |
| Stage 7 | Availability/quote preview boundaries and provider-local adapter shape |
| Stage 8 | Preserve slot/candidate ownership when availability data is attached |
| Stage 9/10 | Render and smoke-test availability preview outcomes without live booking/payment side effects |

## Lanes

### Lane A - Harness Availability Quote Boundary

Owner: Shellby

Branch: `sprint-014/harness-availability-quote-boundary`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned availability/quote preview request/result contracts tied to trusted itinerary slot decisions and slot-associated candidate pools.
- Validation that availability results can only apply to known compiled slots, selected candidates, and trusted slot-candidate associations.
- Fixed sanitized blocked outcomes for unknown slot, unknown candidate, wrong slot, stale compilation, unsupported quote kind, approval-required, and malformed input.
- Read-only preview behavior by default; no booking/payment/session mutation unless a later explicit approved hold boundary is invoked.
- Tests proving no mutation on blocked paths, bounded evidence/trace refs, and no raw prompt/provider payload/credential/payment/approval-token/sentinel leakage.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Availability Preview Adapter

Owner: Kaneki

Branch: `sprint-014/provider-availability-preview-adapter`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local availability/quote packet/result DTOs for flight, lodging, activity, dining, and transit preview categories.
- Deterministic mock/fake-HTTP adapter source for quote-ready, unavailable, stale-packet, provider-timeout, malformed, and unsupported-result paths.
- Sanitized eval/status rows that derive slot/candidate ids from trusted packets and never persist raw provider payloads, fare/booking references, payment data, credentials, approval-token values, candidate display text, or raw exception text.
- Optional live adapter notes for Amadeus or other providers remain disabled/skipped by default, with explicit key/health/timeout/no-paid-fallback guardrails before any future smoke.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

### Lane C - UI Availability Preview Panel

Owner: Sarah

Branch: `sprint-014/ui-availability-preview-panel`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit availability preview panel for deterministic selected itinerary candidates.
- UI path should prefer canonical harness/provider availability preview contracts when available.
- Stable `data-*` markers for quote-ready, unavailable, stale packet, approval-required, provider-blocked, harness-blocked, raw absence, slot id, candidate id, quote category, and provider/eval outcome.
- UI tests and browser smoke covering accepted preview, unavailable preview, stale packet/provider mismatch, wrong-slot/harness block, approval-required preview, and raw-sentinel absence.
- No live provider, booking, payment, search, or credential dependency in required tests/smoke.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for availability preview markers and raw absence

## Integration Order

1. Shellby first, because harness ownership validation defines the trusted quote-preview boundary.
2. Kaneki second, because provider eval/status rows should align with trusted packet and ownership rules.
3. Sarah third, because the UI should consume the canonical harness/provider vocabulary where available.
4. Collin final verification:
   - `npm run build:ui`
   - `dotnet build src/Pch.UI/Pch.UI.csproj`
   - `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
   - `dotnet test`
   - `dotnet build`
   - interactive UI smoke

## Exit Criteria

- Availability/quote preview data is attached only through trusted slot/candidate ownership boundaries.
- Provider availability artifacts are deterministic/offline by default and sanitized on accepted, blocked, and error rows.
- The UI can show quote-ready and blocked preview states without implying booking/payment occurred.
- Required tests remain offline and deterministic.
- No raw prompt, provider payload, proposal JSON, credential, approval-token value, payment data, live booking reference, candidate display value, raw exception text, or secret-like sentinel is persisted or rendered.
