# Sprint 002 - Real Session Loop And Feasibility Harness

Coordinator: Collin

Base: `3c9ba1c011ed1caa958198011240ba823b90b7e2`

Coordinator rule: Collin does not write implementation code in this sprint. Collin owns planning, docs, dispatch, merge review, verification, and publication. Workers own implementation in isolated checkouts.

## Objective

Move from fixture-only proof to a deterministic, testable session loop:

```text
Stage Cockpit UI
  -> server-backed session interaction
  -> harness projection/session state
  -> generated packet/form/choice/approval actions
  -> ledgered response/evidence trace
```

No real booking, no Amadeus live calls, and no required paid model call in default tests.

## Stage Targets

| Stage | Sprint 002 target |
| --- | --- |
| Stage 2 - Projection layer | Golden packet corpus plus budget checks for slot collection, choice collapse, approval request, conflict review, funeral downtime, and business trip packets |
| Stage 3 - Stage machine and approval gates | Deterministic traversal for slot collection, choice, approval, defer/escalate, and evidence packet |
| UI feasibility gate | Stage Cockpit can submit form/choice/approval interactions against a server-backed seam instead of only static local fixture data |
| Stage 5/6 groundwork | Model action runner/eval scaffolding over fixed packets with deterministic mocks and optional OpenRouter Qwen smoke |

## Lanes

### Lane A - Harness Session Loop And Packet Corpus

Owner: Shellby

Branch: `sprint-002/harness-session-loop`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**` if Shellby creates a shared fixture corpus

Must not edit:

- `src/Pch.UI/**`
- `src/Pch.Providers/**`

Deliverables:

- Session API/use-case layer that can accept form responses, selected candidate IDs, approval tokens, and defer/handoff decisions.
- Deterministic stage traversal through slot collection, choice collapse, approval queue, mocked booking gate, and evidence packet.
- Golden packet corpus for the early feasibility gates.
- Projection budget checks that keep packet output bounded for synthetic 1, 7, and 14 day trips.
- Tests proving unsupported claims are rejected and commit/spend actions require approval.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - UI Session Interaction Seam

Owner: Sarah

Branch: `sprint-002/ui-session-cockpit`

Owns:

- `src/Pch.UI/Features/StageCockpit/**`
- `src/Pch.UI/Features/Approvals/**`
- `src/Pch.UI/Features/EvidenceTrace/**`
- `src/Pch.UI/ClientApp/**`
- `src/Pch.UI/wwwroot/app.css`
- `tests/Pch.UI.Tests/**` if added

Must not edit:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `src/Pch.Providers/**`
- `ProgressiveCommitmentHarness.slnx`

Deliverables:

- Keep the current fixture UI runnable.
- Add an interaction seam so the Stage Cockpit can submit form values, selected candidate IDs, and approval action intent to a server-backed service abstraction.
- Add UI-visible states for pending, applied, rejected, and approval-required responses.
- Preserve candidate IDs and approval IDs in rendered markup for future fidelity checks.
- Add a UI smoke/test path if practical without widening solution ownership.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- optional `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj` if Sarah adds UI tests

### Lane C - Model Action Runner And Eval Scaffolding

Owner: Kaneki

Branch: `sprint-002/model-action-runner`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**` if needed

Must not edit:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `src/Pch.UI/**`
- `ProgressiveCommitmentHarness.slnx`

Deliverables:

- Provider-local model action runner that can ask a model client for a bounded structured action against a packet-shaped prompt.
- Deterministic mock model responses for form, choice, approval, summarize, and state-patch actions.
- Eval scaffolding that records schema validity, candidate ID preservation, unsupported-claim detection input, latency, and fallback reason.
- Optional OpenRouter `qwen/qwen3-14b` smoke gated by key presence and credit guard; default tests must use mocks only.
- Documentation for how Sprint 003 can connect this runner to `HarnessAction` after Shellby freezes the contracts.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

## Integration Order

1. Shellby first, because Stage 2/3 contract movement affects future wiring.
2. Kaneki can run in parallel because the lane is provider-local and mock-driven.
3. Sarah can run in parallel if she keeps the seam UI-local and fixture-compatible; any direct consumption of new harness APIs waits until Shellby merges.
4. Collin integrates only after READY packets and reruns:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - UI HTTP smoke

## Sprint Exit Criteria

- There is a deterministic non-live session path from user response to updated packet/evidence state.
- The UI can exercise the interaction seam without provider credentials.
- Golden packet corpus exists and is usable by future strong/small model lanes.
- Provider/model runner can be tested fully with mocks.
- No worker edits another worker's surface.
