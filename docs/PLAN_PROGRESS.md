# Progressive Commitment Harness - Stage Progress Marker

This is the tracked progress copy of `docs/PLAN.md`.

Purpose:

- keep the canonical staged plan stable in `docs/PLAN.md`;
- record which sprint advanced which stage, task, and feature;
- make the next sprint visible in Git without committing local coordinator instructions.

Coordinator rule: Collin plans, dispatches, reviews, integrates, and updates docs. Feature code is delegated to worker agents.

## Current Marker

Updated: 2026-06-29

Latest integrated code before this progress update: `fdce541f66baed70ccd83c54b6b5bbdd3f284d42`

Overall position: Stage 2/3 now have deterministic session traversal, approval-gated action intake, replayable trace events, a harness-owned external action decoder, a reusable runtime action application result, an authority-checked mission intake application, and a validated provider-shaped mission proposal adapter. Stage 4/5 now have provider-local mission planner runtime handoff, mission-kind allowlisting, and bounded mission memory projection into `StagePacket`. Stage 5/6 now have sanitized model-action and mission-planner eval rows, decode/intake/runtime outcome codes, and in-memory proposal handoffs that avoid persisted raw payloads. The UI now runs deterministic server-side model-action and mission-planner loops through canonical provider runtime plus harness adapter boundaries. The next decisive gap is a guarded live provider planner client plus a real prompt-intake UI path, so a user ramble can flow through the same contracts without making required tests depend on network or credits.

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
| 005 | Stage 3 | Runtime action application | `RuntimeActionApplication` composes decode plus intake into one sanitized result shape with decode/intake codes, stage, packet id, summary, and replayable trace | done |
| 005 | Stage 5/6 | Runtime provider proposal bridge | Provider bridge carries raw argument JSON only in memory as `ProviderRuntimeActionProposal`; persisted diagnostics keep action/argument-key/provider metadata and fixed codes only | done |
| 005 | UI/Stage 9 precursor | Server model suggestion loop | Stage Cockpit can run a deterministic server-side model action through provider bridge, runtime application, UI response state, and trace markers | done |
| 006 | Stage 3/4 | Mission intake application | Harness applies structured mission proposals through authority checks; user/trusted facts apply, model-inferred facts become pending confirmations, high-priority commitments can anchor planning | done |
| 006 | Stage 4/6 | Provider mission planner bridge | Provider-local mission planner DTOs, deterministic mocks, structured field/constraint/commitment mirrors, and sanitized eval rows aligned to harness mission intake | done |
| 006 | Stage 5 | Structured memory digest | `StructuredMemoryDigest` bounds load-bearing mission facts, pending confirmations, and trace references for future small-model packets | done |
| 006 | UI/Stage 9 precursor | Mission intake UI slice | Stage Cockpit runs provider DTOs through a UI adapter into `MissionIntakeApplication`, renders applied facts, pending confirmations, high-priority commitments, and digest facts | done |
| 007 | Stage 2/5 | Mission memory projection | `StagePacket.LoadBearingFacts` includes bounded memory and pending-confirmation facts while preserving core counters | done |
| 007 | Stage 4 | Mission proposal adapter | Harness-owned `MissionProposalAdapter` validates provider-shaped mission mirrors, applies through `MissionIntakeApplication`, and blocks malformed/null/unsupported data with fixed codes | done |
| 007 | Stage 4/6 | Provider mission planner runtime | Provider-local `MissionPlannerRuntimeBridge`, runtime handoff DTOs, mission-kind allowlist, and sanitized runtime eval rows | done |
| 007 | UI/Stage 9 precursor | Canonical runtime mission planner UI | Stage Cockpit runs deterministic provider planner output through provider runtime handoff -> harness mission proposal adapter -> mission intake/digest with accepted, pending, provider-blocked, adapter-blocked, and unknown-kind blocked paths | done |

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

## Sprint 005 Result

Sprint 005 turned the deterministic suggested-action seam into the first runtime model-action loop inside the UI/server vertical slice.

- `RuntimeActionApplication` is the canonical decode-plus-intake boundary for externally proposed runtime actions.
- `ProviderActionBridge` rejects packet/result mismatches, rejects disallowed action names, and exposes raw arguments only through a non-serialized in-memory runtime proposal.
- Stage Cockpit now runs accepted, blocked-intake, and runtime decode-failure model suggestions through `ProviderActionBridge -> ExternalActionProposal -> RuntimeActionApplication`.
- UI markers distinguish provider bridge outcome from runtime decode and runtime intake outcome, so provider acceptance is not confused with harness acceptance.

## Sprint 005 Verification

- `dotnet test`: 84 tests passed across core, harness, providers, and UI.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke on `http://127.0.0.1:5130/`: accepted `server-model.accept.defer-slot`, blocked `server-model.block.form-mismatch`, and decode-failed `server-model.decode.missing-argument`.
- Repair gates enforced:
  - stale/crossed provider `PacketId` values cannot become runtime proposals;
  - UI model-run path uses `RuntimeActionApplication` instead of duplicating decoder/intake;
  - raw proposal JSON, provider payloads, prompt text, approval tokens, and sentinel values were absent from rendered UI.

## Sprint 006 Result

Sprint 006 started Stage 4 and structured memory: a messy or rambling prompt can now be represented as provider-shaped mission planner output, mapped into harness-owned mission intake, and rendered as authority-checked mission state plus a bounded digest.

- `MissionIntakeApplication` applies structured mission proposals through authority checks.
- `StructuredMemoryDigest` bounds load-bearing facts, pending confirmations, and trace references.
- Provider mission planner DTOs preserve field paths, authority/evidence, structured constraints, and structured commitments while sanitized eval rows persist only counts/codes/metadata.
- Stage Cockpit mission intake now uses provider DTOs -> UI adapter -> `MissionIntakeApplication` -> `StructuredMemoryDigest` as the primary path.
- User-stated vacation facts, high-priority non-vacation commitments, and model-inferred pending confirmations are visible in the UI with stable markers.

## Sprint 006 Verification

- `dotnet test`: 99 tests passed across core, harness, providers, and UI.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator interactive UI smoke on `http://127.0.0.1:5134/`: vacation mission applied purpose/destination/start/end; high-priority family-support commitment applied; pending pace/traveler-need/constraint confirmations remained model-inferred proposals.
- Raw provider sentinel, raw prompt sentinel, prompt snippet, credential-like text, approval tokens, and provider payloads were absent from rendered UI.

## Sprint 007 Result

Sprint 007 replaced the UI-local mission adapter seam with a canonical runtime path and hardened the provider-shaped mission boundary.

- `MissionProposalAdapter` validates provider-shaped mission mirrors without referencing `Pch.Providers`, enforces bounds/allowlists, and applies through `MissionIntakeApplication`.
- Malformed/null mirrors, unsupported field paths, invalid commitments, overlong payloads, and unknown provider commitment kinds block with fixed sanitized codes and no session mutation.
- `ProjectionService` includes bounded memory and pending-confirmation facts in `StagePacket.LoadBearingFacts` while preserving core counters such as traveler count, selected candidates, and deferred slots.
- `MissionPlannerRuntimeBridge` keeps rich planner results in memory only, emits sanitized metadata/eval rows, rejects packet mismatches, malformed results, and unsupported mission kinds, and avoids raw mission-kind persistence.
- Stage Cockpit now runs mission planning through `MissionPlannerRuntimeBridge -> ProviderMissionProposalMirror -> MissionProposalAdapter.Apply`, with deterministic accepted, proposed, provider-blocked, adapter-blocked, and unknown-kind blocked paths.

## Sprint 007 Verification

- `dotnet test`: 122 tests passed across core, providers, harness, and UI.
- `dotnet build`: passed, 0 warnings, 0 errors.
- `npm run build:ui`: passed.
- Coordinator browser smoke on `http://127.0.0.1:5150/`: clicked mission vacation, non-vacation commitment, pending confirmation, provider packet mismatch, adapter unsupported field, and unknown commitment kind.
- Smoke verified:
  - accepted vacation and high-priority commitment paths applied;
  - pending destination confirmation stayed proposed;
  - provider packet mismatch blocked before adapter/intake/digest;
  - unsupported field blocked at adapter;
  - unknown commitment kind blocked as `invalid_commitment`;
  - raw prompt, provider payload, packet id, unknown kind/title, unsupported mirror code, unsupported field text/value, proposal JSON, credentials, and approval-token sentinels were absent from rendered UI.

## Sprint 008 Target

Sprint 008 should turn the deterministic runtime mission planner into a guarded prompt-intake vertical slice.

- add a harness-owned prompt-to-planner packet boundary that builds bounded mission planner requests from a raw user ramble plus current structured memory, without persisting raw prompt text by default;
- add a provider mission planner client for OpenAI/OpenRouter-compatible JSON output, using deterministic mocks for required tests and strict key/credit/timeout/empty/malformed guards for optional live smoke;
- add a Stage Cockpit prompt-intake panel that can run deterministic provider output now and optionally guarded live provider output later through the same provider runtime and harness adapter path;
- add sanitized eval/smoke artifacts proving raw prompts, provider payloads, credentials, proposal JSON, approval tokens, and secret-like sentinels are not persisted or rendered.

## Not Yet Started

- Stage 4 live strong-model planner/expander/auditor beyond guarded mission planner adapter/runtime work.
- Stage 5 true small-model structured generation beyond deterministic/mock runtime action loops.
- Stage 6 fidelity bake-off with ownership matrix beyond initial sanitized eval rows.
- Stage 7 real Amadeus availability and pricing adapters.
- Stage 8 day compiler and dependency propagation.
- Stage 9 true end-to-end UI run from rambling prompt through mission, staged forms, model/search expansion, candidate pools, approval queue, mocked holds, and evidence packet.
- Stage 10 hardening.
