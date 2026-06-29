# UI Architecture

The UI is not optional for feasibility. A terminal JSON loop can prove contracts, but it cannot prove the user can collapse a large option space quickly.

## Stack

- ASP.NET Core Blazor Web App.
- Interactive Server render mode for fast iteration and server-owned state.
- TypeScript browser modules for client-side form helpers, draft persistence, focus management, keyboard flow, and future richer widgets.

## UI Contracts

The UI renders model/harness output, not free-form markdown:

- `FormRequest`
- `EmitChoiceSet`
- `ApprovalRequest`
- `EvidenceTrace`
- `TripPacket`
- `ClaimLedger`

The UI submits:

- `FormResponse`
- `ChoiceSelection`
- `ApprovalToken`
- `Correction`
- `DeferSlot`

## Feasibility Screens

Minimum end-to-end screens:

- mission intake,
- active stage packet inspector,
- generated form renderer,
- choice-card collapse view,
- approval queue,
- itinerary graph/day timeline,
- evidence/claim trace.

## TypeScript Ownership

TypeScript owns browser-local behavior only. It does not decide travel logic, mutate harness state, or invent options.

Initial modules:

- `forms.ts`: draft save/restore, focus, lightweight field metadata helpers.
- Future: `choiceCards.ts`, `timeline.ts`, `traceInspector.ts`.

## Design Rule

Every user-visible option must preserve its `candidate_id` and claim provenance. Nice wording is allowed; unsupported new facts are not.

