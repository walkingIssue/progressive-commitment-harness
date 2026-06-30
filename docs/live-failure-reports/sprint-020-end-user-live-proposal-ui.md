# Sprint 020 End-User Live Proposal UI Report

## Scope

- `/trip` keeps deterministic/offline mode as the required-test default.
- The end-user status strip now has explicit markers for live preflight, live proposal, harness validation, latest turn source, provider request state, and deterministic fallback.
- The UI now routes configured live sends through the canonical `LiveMissionProposalRunner` and maps accepted provider DTOs into a `LiveModelProposalEnvelope` for `LiveSessionConductor`.

## Live Status

- Status: `canonical_integrated`.
- Provider request made from this worker checkout: no live network call during required verification; deterministic tests use local provider clients.
- Providers/models attempted: none from the UI worker checkout after integration. The UI treats sanitized `live_mission_proposal_provider_unavailable` as a valid live-blocked state.
- Default required tests and smoke remain deterministic/offline.

## Sanitized Outcome Codes

- Deterministic default: `deterministic_default`, `deterministic_fallback`.
- No-config guard: `blocked_by_guard`, `not_attempted`, `live_preflight_disabled`.
- Configured preflight plus unavailable provider runner: `preflight_passed`, `live_model_proposal_blocked`, `live_mission_proposal_provider_unavailable`, `not_run`.
- Accepted proposal path markers: `live_model_proposal_accepted`, `accepted`.
- Harness blocked path markers: `live_model_proposal_accepted`, `harness_validation_blocked`, `mission_proposal_blocked`.
- Stale packet/session markers: `stale_packet_or_session`, `live_mission_proposal_packet_mismatch`.

## Harness Behavior Covered By UI Tests

- Accepted provider mission proposal DTOs flow through `LiveMissionProposalRunner` and `LiveSessionConductor`, surfacing `live_model_proposal_accepted`.
- Provider/model-blocked proposal can stop before harness mutation and surface fixed blocked markers, including provider-unavailable and packet mismatch.
- Harness adapter rejection surfaces `harness_validation_blocked` without echoing raw provider fields.

## Sanitization

- UI state and tests do not render or serialize raw prompt text, raw completions, provider payloads, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display sentinels, raw exception text, or secret-like sentinels.
- Raw prompt content remains transient input for the preflight/conductor call; rendered transcript text stores only bounded summaries.

## Integration Notes

- Integrated Shellby's harness live proposal application head `e0d9a468da8e0b706fb18c7054d611cccf3ed001`.
- Integrated Kaneki's provider live mission proposal runner head `46e6f0cf108aef220567ef44bec687e8dfcbf760`.
- Guarded live OpenRouter/OpenAI smoke remains optional and should record only sanitized outcomes.
