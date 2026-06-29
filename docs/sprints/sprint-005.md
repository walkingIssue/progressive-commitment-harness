# Sprint 005 - Runtime Model Action Loop

Coordinator: Collin

Planning base: Sprint 004 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Turn the deterministic suggested-action seam into a real UI/server action loop:

```text
StagePacket
  -> provider/model action packet
  -> model action result or deterministic mock result
  -> runtime external action proposal
  -> harness decoder/intake
  -> safe UI result, trace, and evidence marker
```

Required tests stay deterministic and offline. Live OpenRouter/Qwen smoke remains optional, guarded by key and credit checks, and must block on empty content, malformed output, request failure, or credit exhaustion without falling back to another paid provider.

## Stage Targets

| Stage | Sprint 005 target |
| --- | --- |
| Stage 3 | One reusable decode-plus-intake result shape for UI/runtime orchestration |
| Stage 5 | Provider/model action output can be passed to harness decoder in memory without raw payload persistence |
| Stage 6 | Offline evals can replay runtime action-loop outcomes against golden packets |
| UI gate | Stage Cockpit can request a model suggestion from a server-side service and apply/render the safe outcome |
| Stage 9 precursor | First vertical UI/server/model loop, still deterministic by default |

## Lanes

### Lane A - Harness Runtime Action Application

Owner: Shellby

Branch: `sprint-005/harness-runtime-action-application`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- A harness-owned application/result service that takes `ExternalActionProposal`, runs `ExternalActionDecoder`, then runs `HarnessActionIntake` only when decode succeeds.
- A compact result DTO with fixed decode/intake codes, blocked/accepted state, replayable trace events, and no raw argument JSON.
- Tests for accepted `defer_slot`, blocked disallowed `handoff`, malformed JSON, unknown kind, provider-supplied approval token dropping, and no mutation on failure.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Runtime Proposal Bridge

Owner: Kaneki

Branch: `sprint-005/provider-runtime-proposal-bridge`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Runtime bridge from `ModelActionRunResult` into a proposal object that can be handed to the harness/application layer in memory.
- Preserve argument JSON only in memory for the immediate decode path; persisted eval rows and diagnostics must contain only action name, argument keys, provider/model/request metadata, response length, and fixed outcome/error codes.
- Deterministic mock model-action source for accepted, blocked, and malformed proposal cases.
- Optional live OpenRouter/Qwen smoke wrapper remains guarded and records sanitized metadata only.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live smoke result or explicit blocked reason

### Lane C - UI Server Model Suggestion Loop

Owner: Sarah

Branch: `sprint-005/ui-server-model-suggestion-loop`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit server-side "run model suggestion" action using deterministic mock provider output by default.
- The server action must call the runtime bridge/application path rather than hard-coded local suggestions.
- Render accepted, blocked, and decode-failure outcomes with stable `data-*` markers, response state, trace outcome, and evidence preview.
- Do not render or persist raw proposal JSON, provider payloads, approval tokens, secrets, prompts, or credentials.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for run-model accepted, blocked, and decode-failure markers

## Integration Order

1. Shellby first, because the runtime result shape becomes the shared safe outcome contract.
2. Kaneki second, to align provider runtime bridge metadata with the harness/application result shape.
3. Sarah third, to consume the runtime path in the UI/server vertical slice.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- One UI button can request and apply a model-suggested action through the server-side runtime path.
- Accepted, blocked, and decode-failure outcomes are deterministic, tested, and visible in the UI.
- No required test uses provider credentials or network.
- No raw prompt, provider payload, proposal JSON, approval token, secret, or raw exception message is persisted or rendered.
