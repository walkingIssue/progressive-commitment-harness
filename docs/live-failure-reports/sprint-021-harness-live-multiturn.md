# Sprint 021 Harness Live Multi-Turn Report

## Status

live status: `not_configured`

Shellby implemented the harness-owned multi-turn live session contract with deterministic/offline tests only. No provider request, network call, booking, hold, payment, spend, or credit-consuming operation was executed in this lane.

## Providers Or Models Attempted

- `none`: provider execution belongs to the provider live turn runner lane.
- `none`: no coordinator-supplied sanitized live multi-turn artifact was available to this worker lane.

## Harness Boundary Added

- `LiveMultiTurnSessionConductor` owns one `TripSession` across turns.
- Typed methods cover initial prompt packet creation, fresh model input projection, decoded model proposal application, user confirmation response, option select/defer, availability quote preview, and provider/model blocked results.
- Each turn returns a UI-agnostic `LiveMultiTurnSessionResult` with fixed code, mutation status, turn id, stage, allowed next operation kinds, assistant work item kind, bounded evidence refs, timeline refs, and a compact model-input fragment.

## Sanitized Outcome Codes Covered

- `awaiting_model_proposal`
- `accepted`
- `awaiting_user_input`
- `confirmation_applied`
- `provider_model_blocked`
- `decode_blocked`
- `intake_blocked`
- `mission_proposal_blocked`
- `approval_required`
- `deterministic_fallback`
- `unsupported_live_operation`
- `stale_packet_or_session`
- `availability_quote_preview_accepted`
- `availability_quote_preview_unavailable`
- `approval_required_preview`

## What The Harness Accepted Or Blocked

- Accepted prompt to mission proposal to pending confirmation while preserving a typed form work item.
- Applied user confirmation and produced a choice work item from the current harness state.
- Applied selected itinerary option decisions and projected selected-card echoes.
- Blocked spend-adjacent quote preview with approval-required preview semantics and no hold/book/pay side effect.
- Blocked malformed proposals, provider/model blocked rows, stale packet/session correlation, unsupported operations, approval-required runtime actions, invalid candidates, and invalid commitments without mutation.

## Safety Notes

- Raw prompt text is transient input only and is absent from serialized results.
- Later model inputs are built from current harness projections and structured memory, not from previous raw chat transcript text.
- Persisted result fragments contain only fixed operation/result codes, trusted packet/session/stage ids, counts, evidence refs, timeline refs, and sanitized projection objects.
- No raw completion, provider payload, API key, credential, approval token, hold reference, booking/payment reference, candidate display sentinel, exception text, or secret-like value is persisted.

## Proposed Follow-Up Tickets

- Wire provider live turn runner rows into `LiveMultiTurnSessionConductor` operations.
- Add guarded live smoke artifacts once provider and UI lanes supply sanitized live logs.
- Extend the model-operation vocabulary beyond mission proposal and runtime action only after multi-turn live smoke identifies the next safe work item.
