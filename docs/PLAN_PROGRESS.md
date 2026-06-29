# Progressive Commitment Harness - Stage Progress Marker

This is the tracked progress copy of `docs/PLAN.md`.

Purpose:

- keep the canonical staged plan stable in `docs/PLAN.md`;
- record which sprint advanced which stage, task, and feature;
- make the next sprint visible in Git without committing local coordinator instructions.

Coordinator rule: Collin plans, dispatches, reviews, integrates, and updates docs. Feature code is delegated to worker agents.

## Current Marker

Updated: 2026-06-29

Latest integrated code before this progress update: `792fc576dfac867bc1136964004c3dd9657a5860`

Overall position: Stage 2/3 now have deterministic session traversal, approval-gated action intake, replayable trace events, and a server-backed UI session path. Stage 5/6 have provider-local model-action eval scaffolding, sanitized golden packet eval rows, and a guarded Qwen smoke result that currently blocks on empty provider content. The next decisive gap is the safe bridge from provider-local action JSON into harness-owned `HarnessAction` intake.

## Sprint Ledger

| Sprint | Stage(s) advanced | Task | Feature/result | Status |
| --- | --- | --- | --- | --- |
| 001 | Stage 0 | Scaffold, build, publish path | Blazor app, TypeScript build, .NET solution, GitHub remote publish, SSH fix | done |
| 001 | Stage 1 | Core contracts | `Pch.Core` internal state contracts, model-facing DTOs, ledgers, authority policy, closed harness action shapes | first pass done, not frozen |
| 001 | Stage 2 | Projection skeleton | `Pch.Harness` projection service with stable fixture packet and bounded synthetic 1/7/14 day projections | started |
| 001 | Stage 3 | Stage machine and approval gates | in-memory trip session, deterministic stage-machine skeleton, approval gate that blocks commit/spend without token | started |
| 001 | UI feasibility gate | UI proof surface | Blazor/TypeScript Stage Cockpit fixture with generated form, choice cards, approval gate, and evidence trace preview | done as fixture |
| 001 | Stage 7 groundwork | Provider gateway | `Pch.Providers` OpenRouter/Qwen client, credit guard, typed provider errors, deterministic mocks, approval-token enforcement for commit adapter | groundwork only |
| 002 | Stage 2 | Golden packet corpus | Provider-free golden packets for slot collection, choice collapse, approval request, conflict review, funeral downtime, and business trip scenarios | done |
| 002 | Stage 3 | Session loop | Deterministic `SessionLoop`, form response handling, candidate selection, defer/handoff recording, explicit blocked results, and approval-token matching against pending approval requests | done for provider-free loop |
| 002 | UI feasibility gate | Interaction seam | Stage Cockpit response seam with pending/applied/rejected/approval-required states, approval/evidence components, preserved candidate/approval ids, and UI smoke markers | done as UI-local seam |
| 002 | Stage 5/6 groundwork | Model-action runner | Provider-local model action runner/evaluator, strict JSON action parsing, deterministic mock, sanitized diagnostics, and no raw model response exposure | groundwork done |
| 003 | Stage 2/3 | Harness action intake | `HarnessActionIntake` validates known kind, allowed stage actions, pending form/approval IDs, approval gates, no partial mutation on block, and sanitized unknown action traces | done |
| 003 | Stage 3/UI gate | Server-backed UI session | Stage Cockpit uses scoped `Pch.Harness` session service for form, choice, approval, blocked state, IDs, and trace projection | done |
| 003 | Stage 5/6 | Golden packet eval rows | Provider golden packet loader and sanitized eval rows avoid raw prompt/model payload persistence; legacy evaluator now stores coarse error codes | done for deterministic eval path |
| 003 | Stage 5 live smoke | Qwen hosted smoke | OpenRouter key and credit guard passed, but `qwen/qwen3-14b` returned empty content; no fallback provider used | blocked by provider output |
| 003 | Stage 9 precursor | UI E2E invariant | Interactive Blazor smoke proved Request approval -> Apply form produces blocked response and does not advance out of approval queue | done |

## Sprint 001 Verification

- `dotnet test`: 22 tests passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- UI HTTP smoke: 200, found Stage Cockpit markers.
- `origin/main`: confirmed at `3c9ba1c011ed1caa958198011240ba823b90b7e2`.

## Sprint 002 Target

Sprint 002 turned the fixture skeleton into a deterministic session loop while preserving the worker boundary:

- Stage 2 advanced from synthetic projection to reusable golden packet corpus and bounded packet checks.
- Stage 3 advanced from skeleton to deterministic session traversal for form, choice, approval, defer, handoff, and blocked outcomes.
- The UI feasibility surface gained a local interaction seam and visible response states.
- Stage 5/6 gained provider-local model-action/eval scaffolding with deterministic mocks.

## Sprint 002 Verification

- `dotnet test`: 43 tests passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- UI HTTP smoke: 200, found `pch-shell`, `Session Response Seam`, and `approval.mock_hold`.
- Repair gates enforced:
  - model action runner no longer exposes raw model response content;
  - disallowed model action errors no longer echo untrusted model output;
  - approval tokens must match a pending approval request in an approval-bearing stage.

## Sprint 003 Result

Sprint 003 created the first deterministic UI run over real harness session services and hardened the action/eval boundaries:

- Stage Cockpit now uses a scoped server-side `Pch.Harness` session service for form, choice, approval, blocked, and trace behavior.
- `HarnessActionIntake` is the harness-owned boundary for externally proposed actions.
- Blocked action intake does not mutate session state, and unknown action kinds are sanitized before trace persistence.
- Provider eval rows are sanitized and do not persist packet prompt text, raw model output, raw exception messages, or secrets.
- The optional OpenRouter `qwen/qwen3-14b` smoke was safely blocked when the provider returned empty content after key and credit guard.
- The UI test and interactive smoke prove Apply form cannot advance out of `ApprovalQueue`; it renders `data-blocked-reason="Cannot apply form while pending harness action is request_approval."`

## Sprint 003 Verification

- `dotnet test`: 59 tests passed across core, harness, providers, and UI.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke: passed on `http://127.0.0.1:5119/`.
- Repair gates enforced:
  - unknown model/provider action kind strings do not persist into replay traces;
  - legacy and sanitized provider eval paths avoid raw exception/message payload persistence;
  - UI apply-form cannot bypass approval stage.

## Sprint 004 Target

Sprint 004 should connect the provider-local model action runner to the harness-owned action intake without giving the small model mutation authority.

- define a harness-owned external action proposal/decoder boundary for provider JSON arguments;
- map provider-local action results into that proposal shape with sanitized failures;
- add a UI "model suggested action" step that uses deterministic mocks by default and sends accepted proposals through `HarnessActionIntake`;
- keep live Qwen smoke optional, credit-guarded, and blocked rather than falling back when provider output is unusable.

## Not Yet Started

- Stage 4 strong-model planner/expander/auditor.
- Stage 5 true small-model structured generation beyond provider-local scaffolding/action bridge.
- Stage 6 fidelity bake-off with ownership matrix beyond initial sanitized eval rows.
- Stage 7 real Amadeus availability and pricing adapters.
- Stage 8 day compiler and dependency propagation.
- Stage 9 true end-to-end UI run.
- Stage 10 hardening.
