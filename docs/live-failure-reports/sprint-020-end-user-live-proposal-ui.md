# Sprint 020 End-User Live Proposal UI Report

## Scope

- `/trip` keeps deterministic/offline mode as the required-test default.
- The end-user status strip now has explicit markers for live preflight, live proposal, harness validation, latest turn source, provider request state, and deterministic fallback.
- The UI-owned seam is ready to receive a canonical live mission proposal runner result and pass a provider-agnostic `LiveModelProposalEnvelope` through `LiveSessionConductor`.

## Live Status

- Status: `not_configured`.
- Provider request made from this worker checkout: no.
- Providers/models attempted: none from the UI worker checkout.
- Default required tests and smoke remain deterministic/offline.

## Sanitized Outcome Codes

- Deterministic default: `deterministic_default`, `deterministic_fallback`.
- No-config guard: `blocked_by_guard`, `not_attempted`, `live_preflight_disabled`.
- Configured preflight without Sprint 020 proposal runner: `preflight_passed`, `live_preflight`, `proposal_runner_deferred`, `deterministic_fallback`.
- Prepared proposal path markers: `live_model_proposal_accepted`, `live_model_proposal_blocked`, `harness_validation_blocked`.

## Harness Behavior Covered By UI Tests

- Accepted provider-agnostic mission proposal can flow through `LiveSessionConductor` and surface `live_model_proposal_accepted`.
- Provider/model-blocked proposal can stop before harness mutation and surface fixed blocked markers.
- Harness adapter rejection surfaces `harness_validation_blocked` without echoing raw provider fields.

## Sanitization

- UI state and tests do not render or serialize raw prompt text, raw completions, provider payloads, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display sentinels, raw exception text, or secret-like sentinels.
- Raw prompt content remains transient input for the preflight/conductor call; rendered transcript text stores only bounded summaries.

## Deferred Integration

- Merge Shellby's Sprint 020 harness live proposal application head and Kaneki's provider live mission proposal runner head when Collin dispatches them.
- Replace the UI-owned deferred proposal gateway with the canonical provider runner plus `LiveSessionConductor` application path.
- Run a guarded live OpenRouter/OpenAI smoke only when explicit configuration and coordinator-approved key/credit posture are present.
