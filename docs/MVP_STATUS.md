# MVP Status - July 2026

This document consolidates the original staged MVP plan, the end-user/eval addendum notes, and the current implementation state after the Sprint 021 live-loop failure review and Sprint 022 planning.

## North Star

The project is meant to prove this thesis:

> A small or cheap model becomes useful for complex planning if the harness owns global typed state and only feeds the model compiled, stage-local projections.

The proving-ground MVP is one-country travel planning, end to end:

```text
rambling prompt
  -> mission
  -> staged forms/questions
  -> candidate expansion
  -> choice collapse
  -> availability/quote preview
  -> approval-gated mocked hold/booking
  -> trip packet
  -> evidence trace
```

The UI must be testable by a real person. It is not enough for JSON fixtures to pass.

## What Exists

### Harness

- Typed trip/session state and many canonical boundaries exist.
- Prompt intake, mission proposal application, runtime action application, itinerary compilation, candidate selection, availability preview, edit-impact analysis, trip snapshot, replay audit, fidelity matrix, and live turn projection exist as isolated harness surfaces.
- `LiveSessionConductor` now accepts provider-agnostic live proposal envelopes and can apply or block mission proposals through canonical validation.
- `LiveMultiTurnSessionConductor` owns one `TripSession` across prompt, model proposal, user confirmation, option decision, availability preview, and provider-blocked turns.
- Golden/deterministic traces exist and are useful for required offline regression.

### Providers

- OpenRouter-compatible model completion infrastructure exists with key loading, credit checks, timeout handling, typed provider exceptions, and no raw payload persistence.
- Provider-local runners/evaluators exist for mission planning, candidate expansion, hold prep, evidence export, fidelity, model roles, live preflight, live mission proposal, and live turn diagnostics.
- `LiveMissionProposalRunner` can call a live model using strict JSON/schema output and returns sanitized accepted/blocked status rows.
- `LiveTurnRunner` can attempt server-side provider calls for live turn output, classify provider/model failures, and record sanitized diagnostics.

### UI

- `/trip` is the end-user route and `/stage-cockpit` is the engineering route.
- The end-user UI has usable interaction primitives: composer/Ask affordance, assistant work bubbles, form cards, mood cards, selected-card user echo, planning timeline, task rail, provider status strip, and media hooks.
- The UI can show live/deterministic posture and fixed live proposal states.
- The UI has imported a bounded local prompt-studio media pack and can render PNG card/timeline imagery.
- The UI can attempt configured live mode, but the in-app browser can show stale/fallback DOM when the Blazor circuit is disconnected. This must not count as a working interaction.

## What Is Still Synthetic

- The visible assistant cards and task rail are still not reliably generated from live model output validated against a harness-owned primitive manifest.
- The product does not yet have a canonical planner primitive/tool library. The model cannot currently choose from a stage-scoped set of form and interaction tools.
- The UI can still render prebuilt/deterministic structures that look like live model output.
- Browser-local fallback state can mutate DOM markers while the real Blazor/server circuit is disconnected. This is not product interaction.
- The current live slices are not yet a full prompt -> model-produced form -> user answer -> second model turn -> validated UI loop.
- Real availability/search is not the default end-user path.
- The Stage 6 fidelity matrix is still mostly deterministic and policy-driven rather than measured from live model runs.
- We do not yet have an accepted in-context browser trace proving a real model can drive multiple safe turns through the harness and UI.

## What Must Be Proven Next

Sprint 022 must prove that the harness can run a **primitive-manifest-backed live interaction**, not merely provider attempts or deterministic UI cards:

1. The user sends a prompt.
2. The server-side planning service asks the harness for a stage-scoped `PlannerToolManifest`.
3. A real configured model receives that manifest on the server.
4. The model returns only primitive/form invocations allowed by the manifest.
5. The harness validates, applies, or blocks the primitive output.
6. The UI renders only the validated turn view.
7. The user answers a model-produced form or selects a model-produced option.
8. A second server-side model turn receives updated harness state, not raw browser history.
9. Logs show exactly where a real model/provider/harness/browser run failed, without storing secrets or raw payloads.

This is the missing step between "provider plumbing exists" and "the product works."

## Documentation Consolidation

The end-user/eval addendum notes supplied during planning should be treated as source notes, not active sprint plans. Their actionable content is consolidated here and in `docs/sprints/sprint-022.md` as:

- Plan 1 maps to the current `/trip` UI plus the remaining need for a server-side planning service, validated primitive renderer, and real live session loop.
- Plan 2 maps to golden traces already started, plus the remaining need for multi-turn live trace capture and primitive-level turn fixtures.
- Plan 3 maps to provider/eval infrastructure already started, plus the remaining need for a real model bake-off from captured primitive-manifest live traces.

Going forward, sprint plans should cite this status doc plus `docs/PLAN.md`, `docs/PLAN_PROGRESS.md`, and the current `docs/sprints/sprint-###.md` instead of re-reading historical addendum notes as if they are current truth.

## Sprint 022 Correction

Sprint 022 is the correction sprint. Its detailed plan lives at `docs/sprints/sprint-022.md`.

Required correction:

- define versioned planner primitives and composite forms;
- connect primitives to stage gates, graph revisions, mood tokens, media manifests, and answer schemas;
- build a harness `PlannerToolManifest` compiler and validator;
- run model requests server-side only;
- render validated primitive instances in UI;
- remove deterministic seeded cards from live mode;
- prove a real in-context browser run reaches a second server-side provider turn or records a specific fixed failure.
