# Sprint 008 - Guarded Prompt Intake Planner

Coordinator: Collin

Planning base: Sprint 007 final main after docs publish.

Coordinator rule: Collin does not write feature code. Collin owns planning, dispatch, review, merge, verification, publication, and next-sprint planning. Workers own implementation in isolated checkouts.

## Objective

Create the first guarded prompt-intake mission planner vertical slice:

```text
raw user ramble
  -> bounded mission planner packet
  -> deterministic mock or guarded live provider client
  -> provider runtime handoff
  -> harness MissionProposalAdapter
  -> MissionIntakeApplication
  -> StructuredMemoryDigest
  -> Stage Cockpit evidence markers
```

Required tests stay deterministic and offline. Live provider smoke is optional and must stop rather than fallback when keys, credits, provider health, timeout, empty output, or malformed output are not safe.

## Stage Targets

| Stage | Sprint 008 target |
| --- | --- |
| Stage 2 | Build planner packets from bounded mission state and structured memory rather than raw history |
| Stage 4 | Add guarded OpenAI/OpenRouter-compatible mission planner client behind the provider abstraction |
| Stage 5 | Prepare small-model-facing packets from prompt-derived structured mission memory |
| Stage 6 | Add sanitized prompt-intake eval rows and live-smoke reporting with no raw prompt/payload persistence |
| UI gate | Stage Cockpit gets a prompt-intake run path with deterministic default and guarded-live swap point |
| Stage 9 precursor | First UI run from a user ramble into mission state through canonical runtime and adapter contracts |

## Lanes

### Lane A - Harness Prompt Packet Boundary

Owner: Shellby

Branch: `sprint-008/harness-prompt-packet-boundary`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness-owned prompt-intake request/result contracts for turning a user ramble plus current `StructuredMemoryDigest` into a bounded mission planner packet shape.
- Prompt packet builder that includes current mission facts, pending confirmations, known constraints, locale, scenario hints, and privacy-safe evidence references.
- No default persistence of raw prompt text in session state, trace, digest, packet projection, or eval-ready results. Store only bounded metadata such as prompt length/hash/category if useful.
- Validation for max prompt length, required session id, max fact/pending counts, and fixed sanitized failure codes.
- Tests for vacation, business, family-support, funeral/downtime prompts, overlong prompt rejection, null/blank prompt rejection, and no raw prompt leakage.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

### Lane B - Provider Live Mission Planner Client

Owner: Kaneki

Branch: `sprint-008/provider-live-mission-planner-client`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- OpenAI/OpenRouter-compatible `IMissionPlannerClient` implementation that emits `MissionPlannerResult` through strict structured JSON.
- Deterministic fake HTTP tests for success, empty content, malformed JSON, unsupported mission kind, packet id mismatch, provider failure, timeout, and credit/provider-health blocked paths.
- Key-file/env loading compatible with existing provider conventions; no raw key material in logs, exceptions, docs, tests, or status.
- Optional guarded live smoke for OpenRouter `qwen/qwen3-14b` if key and credits are available. It must not silently fallback to another paid provider.
- Sanitized eval/live-smoke rows that persist only counts, codes, provider/model/request metadata, response length, and no raw prompt/provider payload.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- optional live smoke result or explicit skipped/blocked reason

### Lane C - UI Prompt Intake Planner

Owner: Sarah

Branch: `sprint-008/ui-prompt-intake-planner`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Stage Cockpit prompt-intake panel for a user ramble, deterministic default planner run, and visible provider/runtime/adapter/intake/digest outcomes.
- Server-side service that routes prompt input through harness prompt packet builder, provider mission planner runtime, `MissionProposalAdapter`, and digest rendering.
- Guarded-live mode may be present only as an explicit disabled-by-default seam; required tests and smoke use deterministic providers.
- UI markers for prompt packet outcome, provider runtime outcome, adapter outcome, intake outcome, digest outcome, error code, blocked reason, and evidence/digest markers.
- Tests and interactive smoke for accepted prompt, pending-confirmation prompt, provider blocked path, adapter blocked path, overlong/blank prompt blocked path, and no raw prompt/provider-payload/sentinel rendering.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- interactive UI smoke for prompt-intake accepted, pending, provider blocked, adapter blocked, validation blocked, and raw prompt absence

## Integration Order

1. Shellby first, because the prompt packet boundary defines what providers and UI may safely consume.
2. Kaneki second, because the live provider client must align with the bounded prompt packet and sanitized result policy.
3. Sarah third, because the UI prompt-intake path consumes both canonical upstream lanes.
4. Collin final verification:
   - `dotnet test`
   - `dotnet build`
   - `npm run build:ui`
   - interactive UI smoke

## Exit Criteria

- A raw user prompt can produce a mission planner packet without persisting raw prompt text by default.
- Deterministic prompt-intake UI run applies mission facts through provider runtime plus `MissionProposalAdapter`.
- Provider-live client exists behind guards and can be skipped/blocked safely without breaking required tests.
- Overlong, blank, malformed, provider-blocked, unsupported, or adapter-blocked paths use fixed sanitized codes and no session mutation.
- No raw prompt, provider payload, proposal JSON, approval token, credential, API key, or secret-like sentinel is persisted or rendered.
