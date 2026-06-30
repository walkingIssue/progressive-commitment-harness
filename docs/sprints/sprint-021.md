# Sprint 021 - Live Multi-Turn Harness Loop

Coordinator: Collin

Dispatch base: pending at sprint start.

## Objective

Prove the project can run a real, model-attached, multi-turn planning interaction through the harness.

Sprint 020 connected a live mission proposal runner, but that is not enough. Sprint 021 must prove a model can participate in more than one turn while the harness owns state, validation, provenance, approval gates, and repair/fallback behavior.

The goal is not itinerary quality yet. The goal is a falsifiable live loop:

```text
user prompt
  -> live model structured output
  -> harness apply/block
  -> UI assistant card/work item
  -> user selection/answer
  -> updated harness projection
  -> second live model structured output
  -> harness apply/block
  -> timeline/evidence/log record
```

If the model fails, that is useful. The failure must be visible, sanitized, logged, and specific enough to debug.

## Non-Negotiable Live Testing Policy

Workers are expected to connect to real models during manual/in-browser development smoke when keys/config are present.

OpenRouter, OpenAI, and Grok/xAI-compatible inference spend is explicitly allowed for Sprint 021. Small and mini models are cheap enough for iterative testing; do not avoid live tests merely to save tiny per-request costs.

Required automated tests must remain deterministic/offline. Manual and coordinator smoke should use live models when configured.

Rules:

- Use real model calls for manual browser smoke when `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `XAI_API_KEY`, or `GROK_API_KEY` is configured.
- Default first provider/model should be OpenRouter with a cheap strict-JSON-capable model. OpenAI mini/cheap models are allowed if configured.
- Keep a small bounded budget per lane unless actively debugging: target 10-30 live requests, stop and report if repeated failures are the same class.
- Never silently fall back to a different paid provider.
- Never run hold/book/pay/spend against a real provider.
- Never persist raw prompts, raw completions, raw provider payloads, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display sentinels, raw exception text, or secret-like values.
- Persist sanitized logs with fixed outcome codes, request ids when safe, provider/model ids, timing, response length, failure class, stage/turn ids, and harness result codes.

## Read First

- `docs/PLAN.md`
- `docs/PLAN_PROGRESS.md`
- `docs/MVP_STATUS.md`
- `docs/sprints/sprint-020.md`
- `docs/design/end-user-chat-interaction-primitives.md`
- `docs/design/end-user-progressive-history.md`
- `docs/providers/openrouter-qwen14b.md`
- `docs/providers/live-preflight-runner.md`
- `docs/providers/live-mission-proposal-runner.md`
- `docs/evals/sanitized-artifacts.md`
- `docs/live-failure-reports/sprint-020-provider-live-proposal.md`
- `docs/live-failure-reports/sprint-020-end-user-live-proposal-ui.md`

## Scope Boundary

In scope:

- one real end-user planning session persisted across turns;
- live model output for at least two sequential turns;
- harness-generated projections between turns, not replayed raw chat history;
- UI rendering of live model-derived work items, not deterministic cards disguised as live output;
- sanitized live interaction logs;
- specific provider failure classification;
- docs consolidation against the MVP plan.

Out of scope:

- real hold/book/pay/spend;
- final itinerary quality;
- broad visual redesign;
- full Amadeus/search integration;
- complete Stage 6 bake-off across every model/stage.

## Lane A - Harness Multi-Turn Live Session Contract

Owner: Shellby

Branch: `sprint-021/harness-live-multiturn-session`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`
- `docs/live-failure-reports/**`

Deliverables:

- Replace the synthetic one-turn live proposal usage with a harness-owned multi-turn live session contract.
- Add or extend a session conductor that owns one `TripSession` across turns and exposes typed methods for:
  - initial prompt;
  - model proposal application;
  - user form/confirmation response;
  - user option selection/defer;
  - approval-required preview;
  - blocked provider/model result.
- Each turn must return a UI-agnostic projection containing:
  - turn id;
  - stage;
  - allowed next operation kinds;
  - assistant work item kind;
  - evidence refs;
  - timeline item refs;
  - fixed result code;
  - mutation/no-mutation status.
- Ensure the second model turn receives a freshly compiled projection from current harness state, not previous raw chat transcript text.
- Add no-mutation tests for malformed, provider-blocked, stale, approval-required, unsupported operation, and invalid candidate/commitment paths.
- Add deterministic fake-model multi-turn tests for at least:
  - prompt -> mission accepted -> pending confirmation;
  - prompt -> mission accepted -> user confirmation -> choice work item;
  - option selection -> availability preview/approval-required work item;
  - provider blocked on second turn.
- Write `docs/live-failure-reports/sprint-021-harness-live-multiturn.md`.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`
- `git diff --check`

## Lane B - Provider Live Turn Runner And Diagnostics

Owner: Kaneki

Branch: `sprint-021/provider-live-turn-runner-diagnostics`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`
- `docs/live-failure-reports/**`

Deliverables:

- Extend provider live mission proposal into a broader live turn runner that can emit the limited work-item/action vocabulary needed for a two-turn UI flow.
- Supported live output kinds for this sprint:
  - mission proposal;
  - pending confirmation question;
  - choice set / option framing from trusted candidates;
  - summary/fallback notice.
- Keep outputs provider-local and map-friendly; do not reference UI or harness projects.
- Add provider failure classification beyond generic unavailable:
  - `provider_http_4xx`;
  - `provider_http_5xx`;
  - `provider_rate_limited`;
  - `provider_timeout`;
  - `provider_empty_content`;
  - `provider_malformed_json`;
  - `provider_schema_invalid`;
  - `provider_upstream_model_unavailable`;
  - `provider_network_error`;
  - `provider_unknown_error`.
- Add sanitized live interaction log rows with:
  - run id;
  - turn id;
  - provider/model;
  - request id if safe;
  - duration bucket or milliseconds;
  - response length;
  - fixed outcome/failure class;
  - no raw prompt/body/completion.
- Run guarded live smoke against OpenRouter and, if configured, OpenAI. Grok/xAI-compatible is allowed when configured.
- Record every live attempt in `docs/live-failure-reports/sprint-021-provider-live-turns.md` and write raw-free local logs under a gitignored `artifacts/live-runs/` directory for coordinator debugging.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`
- `git diff --check`
- guarded live smoke with real model calls when configured

## Lane C - End-User Real Live Multi-Turn UI

Owner: Sarah

Branch: `sprint-021/end-user-real-live-multiturn-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`
- `docs/live-failure-reports/**`

Deliverables:

- Stop using deterministic golden trace output for the primary live-mode assistant cards.
- Keep deterministic mode available, but label it clearly and make live mode the explicit manual-smoke target when configured.
- Replace the fixed/synthetic live proposal session with the harness multi-turn session from Lane A and provider live turn runner from Lane B.
- The UI must support this live browser script:
  1. choose in-harness live role;
  2. send a user prompt;
  3. observe live provider request attempted;
  4. render first harness-applied or harness-blocked assistant work item;
  5. user answers/selects one UI primitive;
  6. observe second live provider request attempted or explicitly blocked by harness state;
  7. render updated timeline/evidence item;
  8. preserve raw absence markers.
- Persist a sanitized browser interaction log under `artifacts/live-runs/` with:
  - UI event id;
  - role;
  - provider request state;
  - live outcome;
  - harness outcome;
  - visible marker ids;
  - no raw prompt/completion/body/key.
- Browser smoke must use the in-app browser when possible. HTTP-only smoke is not enough for the live sprint unless the browser controller fails; if it fails, record that failure separately.
- Keep current interaction design intact: wide assistant interaction space, bottom Ask, cards, timeline, media hooks, selected-card user echo, route split.
- Write `docs/live-failure-reports/sprint-021-end-user-live-multiturn-ui.md`.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- in-app browser deterministic smoke
- in-app browser live smoke with real provider when configured
- raw sentinel scan

## Lane D - MVP Documentation Consolidation

Owner: Collin or delegated doc worker

Branch: `sprint-021/mvp-doc-consolidation`

Owns:

- `docs/**`

Deliverables:

- Convert `docs/MVP_STATUS.md` into the single short truth document for current project status.
- Fold the useful parts of the three addendum notes into official docs or archive them under `docs/archive/`.
- Update `docs/PLAN_PROGRESS.md` with:
  - what stages are genuinely implemented;
  - what stages are fixture/deterministic only;
  - what stages have provider plumbing but no accepted live run;
  - the exact next proof needed.
- Add a short `docs/live-model-testing.md` describing how to run the app with OpenRouter/OpenAI live config, what logs are produced, and what must not be persisted.

Verification:

- doc links resolve;
- no stale "planned only" status lines;
- no raw keys or local secret paths;
- `git diff --check`.

## Integration Order

1. Shellby freezes the multi-turn harness session contract.
2. Kaneki freezes provider live turn runner diagnostics and smoke vocabulary.
3. Sarah integrates both heads and wires `/trip` to a real multi-turn live path.
4. Documentation consolidation lands before final push so the repo accurately says what is live, deterministic, or still synthetic.
5. Collin runs final verification and a real in-app browser smoke using live model config when available.

## Exit Criteria

- A real model is called from the end-user `/trip` live path during manual smoke when keys/config are present.
- At least two sequential turns are attempted through live/harness state, or the second turn is blocked with a specific fixed harness/provider reason.
- The second turn is based on a fresh harness projection, not raw chat history.
- The UI renders model/harness-derived work items rather than only deterministic trace cards.
- Sanitized logs are sufficient to debug provider/model failure classes.
- Deterministic required tests still pass with no live calls.
- No secret, raw prompt, raw completion, raw provider payload, approval token, hold reference, booking/payment ref, candidate display sentinel, raw exception text, or secret-like value is persisted or rendered.
