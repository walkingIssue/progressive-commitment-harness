# Sprint 018 End-User UI Live Observation

Owner: Sarah

Status: `blocked_by_guard`

## Provider / Model Roles

- Deterministic offline role: exercised through required UI tests and browser smoke.
- Live provider role: not attempted from the UI lane because no explicit end-user live provider configuration was surfaced to `src/Pch.UI` on the Sprint 018 starting base.

## Sanitized Observations

- The end-user route reports `offline-deterministic` mode and `blocked_by_guard` live provider state.
- Required UI paths remain deterministic and do not require provider keys, live search, booking, payment, or provider credits.
- The planning timeline can be browsed in day mode and task mode without raw prompts or provider payloads.
- Timeline items retain trusted day, slot, task, candidate, decision, evidence, and origin turn ids through stable `data-*` markers.

## Confusing UX / Model-Attached Failure Points

- Live attachment is visible only as a guarded state in this UI lane. A future lane should add an explicit live-run affordance once the provider repair posture and harness edit-impact contracts are integrated into the product surface.
- The timeline currently supports jump-back and browse affordances; edit/repair drawers are intentionally deferred until the canonical edit-impact boundary is available to UI.

## Fixed Codes Observed

- `offline-deterministic`
- `deterministic_fallback_active`
- `blocked_by_guard`
- `approval_required_preview`

## Follow-Up Tickets

- Wire the planning timeline items to the harness edit-impact result once Shellby's Sprint 018 boundary is published.
- Add a disabled-by-default live model turn action that records sanitized provider/model metadata and fixed failure categories.
- Add an edit-detail drawer for affected/preserved nodes after the canonical edit-impact contract lands.
