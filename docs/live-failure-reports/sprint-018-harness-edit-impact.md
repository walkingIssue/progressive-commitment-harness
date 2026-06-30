# Sprint 018 Harness Edit Impact Report

## Status

live status: `not_configured`

Shellby lane required tests remained deterministic and offline. No live provider, browser, booking, hold, payment, or network path was executed in this harness lane.

## Provider Or Model Roles Attempted

- `none`: no sanitized live traces were available to this worker lane at implementation time.
- `none`: no OpenRouter/OpenAI/Grok-compatible live call was required for the read-only harness edit-impact boundary.

## Sanitized Failure Categories

- `not_configured`: live trace input was not provided to the harness lane.
- `blocked_by_scope`: edit application, provider repair suggestions, and UI timeline behavior are intentionally outside this lane.

## What The Harness Handles

- Builds a deterministic planning dependency snapshot from trusted trip session state.
- Includes mission facts, itinerary days, slots, selected/deferred itinerary decisions, availability previews, and mock hold readiness.
- Reports stale fingerprints, unknown nodes, unsupported edit kinds, no-impact edits, and repair-required edits with fixed codes.
- Returns affected nodes, preserved nodes, stale context, and minimal repair prompt codes without mutating session state.

## Failure Points To Handle Later

- Live model repair suggestions need a provider-lane posture before the harness should accept model-authored repair text.
- UI edit drawers should pass stable node ids and observed snapshot fingerprints back to this boundary before asking for repair.
- If future provider traces include stale availability or quote previews, the harness should keep using fixed stale-context codes rather than echoing provider payloads.

## Proposed Follow-Up Tickets

- Add provider-local repair suggestion eval rows that consume `PlanningNodeRef` metadata and emit fixed repair modes.
- Add UI timeline edit drawer integration that calls the edit-impact boundary before any replan or reselection flow.
- Add a guarded live smoke that records only sanitized model/provider role names, schema validity, response lengths, and fixed failure codes.
