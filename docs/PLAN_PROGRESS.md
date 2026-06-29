# Progressive Commitment Harness - Stage Progress Marker

This is the tracked progress copy of `docs/PLAN.md`.

Purpose:

- keep the canonical staged plan stable in `docs/PLAN.md`;
- record which sprint advanced which stage, task, and feature;
- make the next sprint visible in Git without committing local coordinator instructions.

Coordinator rule: Collin plans, dispatches, reviews, integrates, and updates docs. Feature code is delegated to worker agents.

## Current Marker

Updated: 2026-06-29

Published main: `3c9ba1c011ed1caa958198011240ba823b90b7e2`

Overall position: Stage 1 first pass complete, Stage 2/3 started, UI feasibility pulled forward, provider gateway groundwork started. The project is not yet a real end-to-end booking agent.

## Sprint Ledger

| Sprint | Stage(s) advanced | Task | Feature/result | Status |
| --- | --- | --- | --- | --- |
| 001 | Stage 0 | Scaffold, build, publish path | Blazor app, TypeScript build, .NET solution, GitHub remote publish, SSH fix | done |
| 001 | Stage 1 | Core contracts | `Pch.Core` internal state contracts, model-facing DTOs, ledgers, authority policy, closed harness action shapes | first pass done, not frozen |
| 001 | Stage 2 | Projection skeleton | `Pch.Harness` projection service with stable fixture packet and bounded synthetic 1/7/14 day projections | started |
| 001 | Stage 3 | Stage machine and approval gates | in-memory trip session, deterministic stage-machine skeleton, approval gate that blocks commit/spend without token | started |
| 001 | UI feasibility gate | UI proof surface | Blazor/TypeScript Stage Cockpit fixture with generated form, choice cards, approval gate, and evidence trace preview | done as fixture |
| 001 | Stage 7 groundwork | Provider gateway | `Pch.Providers` OpenRouter/Qwen client, credit guard, typed provider errors, deterministic mocks, approval-token enforcement for commit adapter | groundwork only |

## Sprint 001 Verification

- `dotnet test`: 22 tests passed.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- UI HTTP smoke: 200, found Stage Cockpit markers.
- `origin/main`: confirmed at `3c9ba1c011ed1caa958198011240ba823b90b7e2`.

## Sprint 002 Target

Sprint 002 should turn the fixture skeleton into a real session loop while preserving the worker boundary:

- advance Stage 2 from synthetic projection to reusable stage packet corpus and projection budget checks;
- advance Stage 3 from skeleton to deterministic session traversal for slot collection, choice, approval, and evidence trace;
- keep the UI as the required feasibility surface by adding server-backed interaction seams and browser-visible submit/approval behavior;
- prepare Stage 5/6 by adding model-action/eval scaffolding against fixed packet fixtures, with live OpenRouter smoke optional and manually gated.

## Not Yet Started

- Stage 4 strong-model planner/expander/auditor.
- Stage 5 true small-model structured generation.
- Stage 6 fidelity bake-off with ownership matrix.
- Stage 7 real Amadeus availability and pricing adapters.
- Stage 8 day compiler and dependency propagation.
- Stage 9 true end-to-end UI run.
- Stage 10 hardening.
