# Progressive Commitment Harness - Staged Build Plan

Progressive Commitment Harness is a time-and-commitment planning engine where a strong model expands the option space, a typed harness compiles global state into small stage packets, and a small local model emits structured forms/options as the primary interface. Travel in one country is the proving ground.

The falsifiable thesis:

> A small model becomes useful for complex planning if the harness keeps global state typed and only feeds the model a compiled, stage-local projection.

## Goals And Non-Goals

- **Goal:** prove the projection layer plus contracts make a small local model reliable inside a real transactional workflow, end to end for one country at arbitrary trip length.
- **Goal:** produce two artifacts per run: a bookable trip packet and an evidence/conversational trace.
- **Goal:** test end to end through a real UI surface, not only CLI JSON. The UI is an ASP.NET Core Blazor Web App with Interactive Server render mode and TypeScript client modules.
- **Non-goal v1:** multi-country, payments, or production booking issuance. Real availability reads; mocked, user-gated commit/spend actions.
- **Non-goal v1:** polished consumer UI. The UI must be usable enough to exercise generated forms, choice cards, approval gates, and trace review.

## Key External Facts

- **Availability source:** Amadeus Self-Service APIs with the Python SDK `amadeus`. Test env is free/cached/limited; production is real-time. Useful surfaces include flight offers/search/pricing, hotel offers/search, and destination experiences in supported cities. Commit/spend endpoints are gated or mocked in v1.
- **Restaurants/dining gap:** no clean public dining-booking API in Amadeus. v1 treats dining as candidate pools sourced from strong-model web-search summaries with mocked reservation holds.
- **Small-model structured output:** Ollama JSON-schema output can be validated through Pydantic. Known failure modes drive design: deeply nested schemas, `""` instead of `null`, large schema context cost, and occasional structured-output fragility. Mitigations: flat schemas, temperature 0, explicit null instructions, and retry-on-validation-error.
- **Strong-model structured output:** OpenAI structured outputs should use strict schemas, check refusal and finish reason, and treat model-inferred changes as proposals unless authority policy says otherwise.

## Architecture

Four layers around one typed state store:

- **Strong model:** planner, expander, auditor, and state-patch proposer.
- **Harness:** typed state store, stage/workflow machine, projection layer, approval gates, evidence ledger, claim ledger, and state authority policy.
- **Small model:** form generation, option compression, summaries, and next-question selection. It never books, invents facts, edits fixed anchors, or mutates state directly.
- **Tools/adapters:** Amadeus availability/pricing, mocked booking/spend, dining/search expansion, and future provider integrations.

Global state can expand; model context cannot. Every model call receives a purpose-built model-facing DTO, not the rich internal state.

## Early Feasibility Gates

Before any provider layer is considered done, create a golden packet corpus:

- `slot_collection_packet`
- `choice_collapse_packet`
- `approval_request_packet`
- `conflict_review_packet`
- `funeral_downtime_packet`
- `business_trip_packet`

Every generated option, claim, summary sentence, and approval prompt must trace to one of:

- user-stated state,
- trusted tool output,
- candidate pool evidence,
- country-pack assumption,
- explicit model inference pending confirmation.

## Build Stages

### Stage 0 - Scaffold And Connectivity

Create the solution, UI shell, provider client skeletons, env loading, and smoke-test harness.

- **Spike:** hit Ollama, OpenAI, and Amadeus once where credentials exist; run Blazor UI locally.
- **Expected result:** provider smoke tests are skippable without credentials, and UI runs.
- **Expected failures:** Amadeus test-data limits, missing Ollama model, missing SSH/API credentials, Windows env-var drift.

### Stage 1 - Core Contracts

Create rich internal contracts and separate flat model-facing DTOs.

Internal state contracts:

- `TripMission`
- `Traveler`
- `Constraint`
- `Commitment`
- `Candidate`
- `CandidatePool`
- `ItineraryGraph`
- `Day`
- `TimeBlock`
- `DecisionLedger`
- `EvidenceTrace`
- `ClaimLedger`
- `SearchCache`
- `StateAuthorityPolicy`

Model/UI contracts:

- `StagePacket`
- `FormRequest`
- `FormField`
- `FormResponse`
- `StatePatchProposal`
- `ApprovalRequest`
- `HarnessAction`

`HarnessAction` is a closed discriminated union: `emit_form`, `emit_choice_set`, `propose_search`, `summarize`, `request_approval`, `state_patch`, `defer_slot`, and `handoff`.

- **Spike:** round-trip every model, generate schemas, lint model-facing schemas for shallow depth and explicit null semantics.
- **Expected result:** rich internal state and flat model DTOs are intentionally distinct.
- **Expected failures:** teams try to reuse internal graph objects as prompt DTOs; reject that at contract review.

### Stage 2 - Projection Layer

Implement `project(state, stage) -> StagePacket`.

The projection must include only:

- current subtask,
- load-bearing facts,
- candidate IDs and compact evidence,
- constraints not to violate,
- authority hints,
- allowed outputs,
- trace requirements.

- **Spike:** build synthetic 1, 7, and 14 day Japan states; project multiple stages; measure token budgets.
- **Expected result:** every packet stays under a fixed target budget regardless of trip length.
- **Expected failures:** candidate pools and constraints grow unbounded; solve with top-k clustering and relevance filtering.

### Stage 3 - Stage Machine And Approval Gates

Workflow graph:

```text
intake
  -> slot_collection
  -> posture
  -> day_skeleton_generation
  -> logistics
  -> meals
  -> activities_downtime
  -> conflict_verify
  -> approval_queue
  -> mocked_booking
  -> evidence_packet
```

Transitions are driven by `FormResponse`, trusted tool output, and applied `StatePatchProposal`.

Hard gate: any irreversible/spend/booking action requires an explicit `ApprovalRequest` plus user token.

- **Spike:** traverse the graph with a stubbed model returning canned `HarnessAction`s; property-test that no commit action fires without approval.
- **Expected result:** deterministic traversal and gate correctness.
- **Expected failures:** oscillation when a stage has no candidates; add `defer_slot` and escalate-to-strong-model paths.

### Stage 4 - Strong-Model Layer

Planner, expander, auditor, patch-proposer.

All strong-model-originated changes become `StatePatchProposal`s. They auto-apply only if `StateAuthorityPolicy` allows the source and field; otherwise they require user confirmation.

- **Spike:** feed messy prompts for vacation, business, funeral plus downtime, helping family move, and medical/admin travel.
- **Expected result:** usable mission skeletons, correct trip posture, risk-flagged patches.
- **Expected failures:** hallucinated slots, overconfident inference, refusals, excessive genericity.

### Stage 5 - Small-Model Layer

Ollama owns form generation, choice framing, lightweight compression, and trace narration only where Stage 6 proves fidelity.

- **Spike:** for fixed packets, generate forms/choice sets across several model sizes.
- **Expected result:** high schema validity and coherent choice cards.
- **Expected failures:** invented options, unsupported claims, `""` for null, empty nested arrays.

### Stage 6 - Fidelity Bake-Off

This is the decisive feasibility gate. Run the same packets through small and strong models.

Metrics:

- schema-validity rate,
- faithfulness,
- unsupported-claim rate,
- candidate-ID preservation,
- latency,
- user decisions to completion,
- UI completion time,
- fallback rate by stage type.

Faithfulness means every user-visible option ID, fact, comparison, and summary claim traces to the `ClaimLedger`.

- **Expected result:** per-stage ownership matrix: small model, strong fallback, or harness-only.
- **Expected failures:** small model fails comparison/summarization. It can still be retained for form wording or trace narration if those pass.

### Stage 7 - Availability And Booking Adapters

Uniform adapter interface:

- `search_availability`
- `quote_price`
- `hold`
- `book`
- `pay`

Search/quote can be real. Hold/book/pay are mocked and approval-gated in v1.

- **Spike:** real flight and hotel search where Amadeus test data allows; assert book/pay fail without approval.
- **Expected result:** real availability into candidate pools and commit actions blocked.
- **Expected failures:** test-env data gaps, price drift, thin dining coverage.

### Stage 8 - Day Compiler And Dependency Propagation

Reusable day compiler:

- sleep,
- meals,
- transit,
- fixed anchors,
- activity clusters,
- downtime,
- contingency blocks.

Trip length is a loop over day templates plus commitments, not N times complexity.

- **Spike:** compile 1, 7, and 14 day trips from the same machinery; inject a fixed anchor like a funeral at 13:00.
- **Expected result:** conflicts detected, buffers preserved, no double-booking.
- **Expected failures:** intercity days break independence; lead-time conflicts surface late.

### Stage 9 - End-To-End UI Slice

One country, arbitrary length:

```text
rambling prompt
  -> mission
  -> staged Blazor forms
  -> strong/search expansion
  -> candidate pools
  -> choice collapse
  -> real availability
  -> approval queue
  -> mocked holds
  -> trip packet
  -> evidence trace
```

- **Spike:** full UI run for 7-day and 14-day Japan trips plus one non-vacation scenario.
- **Expected result:** complete internally consistent trip packet; interaction count stays roughly flat as length grows.
- **Expected failures:** interaction count creeps with trip length, trace gaps, UI cannot express a required harness action.

### Stage 10 - Hardening

Retry/repair loops, schema-divergence CI guard, replayable traces, observability, generalization checks, and worker handoff discipline.

- **Spike:** run CI and replay funeral/downtime plus business-trip scenarios.
- **Expected result:** same machinery handles non-vacation trip types through posture/objective/country-pack config.
- **Expected failures:** trip-type-specific assumptions leak into core.

## Risks And Open Decisions

- **Central risk:** small-model faithfulness.
- **UI risk:** a JSON-only harness hides interaction drag. End-to-end gates must use the Blazor UI.
- **Authority risk:** strong-model inferences silently becoming facts. `StateAuthorityPolicy` prevents this.
- **Provenance risk:** pleasant summaries can become unsupported claims. `ClaimLedger` is mandatory.
- **Coverage risk:** dining/activities have weaker real booking APIs.
- **Data staleness:** re-quote before hold.
- **Country-pack scope:** Japan v1 is config, not harness logic.

## Production Quality Bar

If the thesis works, this project may be published. Treat implementation as production-bound:

- typed errors and explicit failure states instead of silent success;
- no raw secrets, raw provider payloads, or prompt data in logs, docs, tests, status snapshots, traces, or exception messages;
- no live provider calls in required tests;
- no silent fallback to another paid provider if credits fail or run out;
- no hold, book, pay, spend, or irreversible action without approval-token gating;
- deterministic tests for safety-critical behavior;
- narrow changes scoped to owned surfaces, with public-release risk called out in READY reports.

## Orchestration

Local multi-agent coordination state lives outside the repo. Stages 0-3 stay serialized until contracts, projection, workflow, UI shell, and authority surfaces are stable. After that, independent workers can own strong-model, small-model, adapters, and UI-specific implementation lanes behind frozen contracts.
