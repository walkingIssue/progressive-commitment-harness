# Plan 2 — Golden Turn-Based Trace Harness
Deterministic, model-free regression tests that replay a scripted sequence of typed user inputs through the live harness loop and assert the projected packet and turn outcome at every step. Zero model credits, safe for required CI, and it locks the harness behavior in place before the UI and eval work churn it.
## Problem
There is per-component test coverage but no end-to-end, turn-ordered regression: nothing asserts that "prompt → form → choice → approval → evidence" produces a stable sequence of stages, packets, allowed actions, and blocked reasons. Behavior changes can silently alter the interaction path.
## Current State
* `SessionLoop` (`src/Pch.Harness/SessionLoop.cs`) and `RuntimeActionApplication` (`src/Pch.Harness/RuntimeActionApplication.cs`) are pure, input-driven seams: typed input in, `SessionTurnResult` out, no I/O.
* `TripRunReplayAudit` (`src/Pch.Harness/TripRunReplayAudit.cs`) already proves the pattern: build a session, snapshot twice, assert determinism + read-only via `SessionSignature`, and emit a sanitized SHA-256 hash.
* `SyntheticTripFactory` and `GoldenPacketCorpus` (`src/Pch.Harness/GoldenPacketCorpus.cs`) provide canonical starting states (1/7/14-day, business, funeral-downtime).
* Sanitization helpers `SafeText` / `IsSafeReference` are duplicated verbatim in `FidelityMatrix.cs` and `TripRunReplayAudit.cs` — extract once and reuse here.
## Proposed Changes
### 1. Shared sanitizer
Extract the duplicated `SafeText` / `IsSafeReference` / `SafeReferences` logic into a single `Pch.Harness/ArtifactSanitizer.cs` and update `FidelityMatrix` and `TripRunReplayAudit` to use it. The trace harness reuses the same redaction so golden files inherit the sanitized-artifact policy in `docs/evals/sanitized-artifacts.md`.
### 2. TraceScript model
Add `TraceScript` (`Pch.Harness`): a named, ordered list of `TraceStep`s. Each step is a typed input — one of `SubmitForm(values)`, `SelectCandidates(ids)`, `Approve(approvalId,token)`, `Defer(slotId,reason)`, or `ExternalAction(actionId,kind,jsonArgs)` — plus an optional starting-state selector for step 0.
### 3. TraceRunner
Add `TraceRunner` that replays a `TraceScript` from a `SyntheticTripFactory` session, capturing per turn a sanitized `TraceTurnRecord`: turn index, input kind, resulting `Stage`, packet id, `AllowedActions`, `NextAction.Kind`, decision kind, `IsBlocked` + blocked code/reason, and a SHA-256 hash of the projected `StagePacket` (serialized with the same JSON defaults as the audit). It runs each script twice and asserts identical output (determinism guard), mirroring `TripRunReplayAudit`.
### 4. Golden artifacts + regen switch
Serialize each script's `TraceTurnRecord` list to JSON under `tests/fixtures/golden-traces/`. Tests assert replay equals the committed golden; setting an env var (e.g. `PCH_REGEN_GOLDEN_TRACES=1`) rewrites the goldens for intentional updates. Follows the existing `tests/fixtures/golden-packets` convention.
### 5. Scenario corpus
Scripts covering: happy paths for 1/7/14-day vacation, business, and funeral-downtime; and blocked paths — approval attempted before a token, unknown candidate id, form-id mismatch, action-not-allowed-for-stage, and malformed external-action JSON. Reuse the feasibility-gate scenarios named in `docs/PLAN.md` (slot_collection, choice_collapse, approval_request, conflict_review, funeral_downtime, business_trip).
### 6. Tests
Add `tests/Pch.Harness.Tests/GoldenTraceTests.cs` running every script in the required suite (no model calls). Assert determinism, golden equality, and that no `TraceTurnRecord` contains redaction sentinels.
## Worker Lanes (for the coordinator)
* Lane A — Shared sanitizer extraction + refactor of `FidelityMatrix`/`TripRunReplayAudit` to use it (must land first; touches files other lanes read).
* Lane B — `TraceScript` / `TraceRunner` / record types + golden serialization.
* Lane C — Scenario corpus + `GoldenTraceTests` + initial golden files.
## Verification
* `dotnet test` for `Pch.Harness.Tests` (0 warnings); all scripts deterministic and matching goldens.
* Confirm a deliberate behavior change (e.g. reorder allowed actions) makes a golden test fail, then regenerate cleanly with the regen switch.
## Out of Scope
Any live model output (that is Plan 3) and UI-level browser testing (Plan 1 owns its own smoke).
## Risks
* Over-broad packet hashing making goldens brittle to harmless additions — mitigate by hashing a stable, explicitly-projected subset and asserting structured fields separately from the hash.
* Hidden nondeterminism (timestamps) — inputs must use fixed `DateTimeOffset`s as `TripRunReplayAudit` already does.
