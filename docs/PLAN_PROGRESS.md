# Progressive Commitment Harness - Stage Progress Marker

This is the tracked progress copy of `docs/PLAN.md`.

Purpose:

- keep the canonical staged plan stable in `docs/PLAN.md`;
- record which sprint advanced which stage, task, and feature;
- make the next sprint visible in Git without committing local coordinator instructions.

Coordinator rule: Collin plans, dispatches, reviews, integrates, and updates docs. Feature code is delegated to worker agents.

## Current Marker

Updated: 2026-06-29

Latest integrated code before this progress update: `8ca17aa23a7a61658af5504bb4c860ad04f4cd04`

Overall position: Stage 2/3 now have deterministic session traversal, approval-gated action intake, replayable trace events, a harness-owned external action decoder, and a server-backed UI suggested-action path that enters through decoder plus `HarnessActionIntake`. Stage 5/6 have provider-local model-action eval scaffolding, sanitized golden packet eval rows, decode/intake outcome codes, and a guarded Qwen smoke result that currently blocks on unusable provider output. The next decisive gap is a runtime UI/server action loop that asks a model source for a proposal and applies it through the same safe intake boundary.

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
| 004 | Stage 3 | External action decoder | Harness-owned `ExternalActionProposal` and `ExternalActionDecoder` map flat JSON into canonical `HarnessAction` values with sanitized decode failures and no mutation on decode/intake failure | done |
| 004 | Stage 3/10 precursor | Approval token hardening | External/provider approval proposals intentionally drop `approval_token`; only trusted user approval flow can supply tokens | done |
| 004 | Stage 5/6 | Provider bridge eval outcomes | Provider-local action bridge and sanitized eval rows record decode/intake outcome codes without raw prompt/model payloads or raw argument values | done as provider-local groundwork |
| 004 | UI gate | Model suggested action path | Stage Cockpit applies deterministic suggested actions through `ExternalActionDecoder` plus `HarnessActionIntake`; accepted `defer_slot`, blocked `handoff`, and malformed JSON paths are visible in UI | done |

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

## Sprint 004 Result

Sprint 004 connected model/provider-shaped action proposals to harness-owned intake without giving the model mutation authority:

- `ExternalActionDecoder` maps action discriminator plus flat JSON arguments into canonical `HarnessAction` values.
- Decode failures use fixed codes/summaries and do not echo malformed JSON, raw action text, provider payloads, prompt text, or secrets.
- Provider-supplied `approval_token` is ignored during external approval decoding; approval tokens still must come from the trusted user approval path.
- Provider eval rows now include decode/intake outcome codes while persisting only sanitized metadata.
- Stage Cockpit has a deterministic model-suggested action panel where accepted `defer_slot`, blocked `handoff`, and malformed JSON proposals are exercised through decoder/intake and rendered with fixed UI codes.

## Sprint 004 Verification

- `dotnet test`: 73 tests passed across core, harness, providers, and UI.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke on `http://localhost:5228/`: accepted `suggestion.accept.defer-slot`, blocked `suggestion.blocked.booking`, and malformed `suggestion.decode.failure`; raw sentinel `RAW_PROVIDER_PAYLOAD_SHOULD_NOT_LEAK` was absent from page text.
- Repair gates enforced:
  - external/provider approval tokens cannot pass through decoder;
  - unknown/malformed provider payloads remain sanitized;
  - model-suggested UI actions cannot mutate state unless decoder and `HarnessActionIntake` accept them;
  - provider eval/runtime diagnostics avoid raw prompt/model payload persistence.

## Sprint 005 Target

Sprint 005 should turn the deterministic suggested-action seam into the first runtime model-action loop inside the UI/server vertical slice.

- add a harness/application result shape for decode plus intake so UI and eval can render the same safe outcome codes;
- add a provider runtime bridge that can pass argument JSON in memory to the harness decoder while persisting only sanitized eval metadata;
- add a Stage Cockpit "run model suggestion" server action using deterministic mock providers by default and optional guarded OpenRouter/Qwen smoke only outside required tests;
- keep all live provider calls optional, credit-guarded, and blocked rather than falling back when output is empty, malformed, or paid credits fail.

## Not Yet Started

- Stage 4 strong-model planner/expander/auditor.
- Stage 5 true small-model structured generation beyond deterministic/mock runtime action loops.
- Stage 6 fidelity bake-off with ownership matrix beyond initial sanitized eval rows.
- Stage 7 real Amadeus availability and pricing adapters.
- Stage 8 day compiler and dependency propagation.
- Stage 9 true end-to-end UI run from rambling prompt through mission, staged forms, model/search expansion, candidate pools, approval queue, mocked holds, and evidence packet.
- Stage 10 hardening.
