# Sprint 021 End-User Live Multi-Turn UI Report

## Scope

- `/trip` keeps deterministic/offline mode available and clearly labeled.
- Live mode no longer renders deterministic golden trace turns as the primary live-mode assistant cards.
- The UI prepares a two-turn live session surface: first provider/harness result, user selection, and second-turn attempted-or-blocked marker.
- Stage Cockpit remains separate on `/stage-cockpit`.

## Current Integration Status

- Shellby Sprint 021 harness multi-turn session contract integrated from `origin/sprint-021/harness-live-multiturn-session` at `9b7a2932b9cd361a75e67a357fffc1b147083410`.
- Kaneki Sprint 021 provider live turn runner diagnostics integrated from `origin/sprint-021/provider-live-turn-runner-diagnostics` at `ee06ddeba2486048cee52247b26d8ed0ba7dd5c4`.
- The UI live path now starts `LiveMultiTurnSessionConductor`, builds a provider-local `LiveTurnPacket`, runs `LiveTurnRunner`, maps accepted mission proposals back into the conductor, and surfaces provider-blocked turns with fixed sanitized live-turn outcomes.

## Sanitized UI Outcomes

- First live accepted: `live_model_proposal_accepted` with provider outcome `live_turn_accepted`
- First live provider blocked/unavailable: `live_model_proposal_blocked` with provider outcomes such as `live_turn_provider_unknown_error`
- Harness validation blocked: `harness_validation_blocked`
- Second turn provider blocked diagnostic: `live_turn_provider_unknown_error`
- No-config guard: `blocked_by_guard`
- Deterministic fallback: `deterministic_fallback`

## Prompt-Studio Media

- Imported local PNG subset: 16 assets.
- Runtime manifest: `src/Pch.UI/wwwroot/media/japan-prompt-studio-pack/manifest.json`
- UI usage: candidate cards, selected-card echoes, planning timeline thumbnails, and fallback state imagery.
- Deferred import: remaining prompt-studio PNGs after the generation set stabilizes.

## Live Smoke Status

- Required tests and deterministic smoke remain offline.
- Real provider browser smoke should be attempted when explicit live config/keys are present. Kaneki's guarded provider smoke reported sanitized `live_turn_provider_unknown_error` / `provider_unknown_error`; the UI treats that as a valid blocked-live state instead of silently presenting deterministic cards as live success.
- Sanitized local browser interaction logs may be written under gitignored `artifacts/live-runs/`.

## Browser Smoke Observations

- Repair note: the first integrated browser smoke was misleading because the browser-local fallback intercepted chat actions and the EndUserChat feature folder did not import the Blazor web event directives. The repaired path leaves Blazor clicks unobstructed and adds feature-local imports so `@onclick` / `@oninput` render as server-side event handlers instead of literal attributes.
- In-app browser on the local smoke server rendered the `/trip` page, but the observed tab kept stale `_blazor` negotiation errors from an earlier port. Standalone local Chrome was used for the final interaction smoke so the live page could be verified with an active Blazor runtime.
- Standalone local Chrome smoke against `/trip` confirmed Blazor was active, no literal `@onclick` attributes remained, first prompt send mutated `data-final-state` to `applied`, the composer folded to `collapsed_drawer`, candidate cards rendered real prompt-studio PNGs, and selecting `candidate-japan-classic-highlights` produced a selected user option card turn with trusted evidence ids.
- Standalone Chrome smoke against `/stage-cockpit` confirmed the engineering cockpit route is separate from the end-user chat surface.
- Guarded live smoke was attempted with explicit OpenRouter configuration: live model enabled, live turn enabled, key-available flag true, provider `openrouter`, in-harness/strong-planner model `qwen/qwen3-14b`, timeout 25 seconds, and the API key supplied to the child app process through `OPENROUTER_API_KEY_FILE` plus process environment. No key value was logged or committed.
- The repaired live browser path selected the in-harness role and reached `data-provider-request-state="attempted"` / `data-live-turn-attempt-count="1"`. OpenRouter preflight then returned a sanitized fixed blocker: `data-live-preflight-state="preflight_blocked"`, `data-provider-outcome="live_preflight_malformed_json"`, `data-error-code="PCH_UI_LIVE_MODEL_SANITIZED_FAILURE"`, and `data-blocked-reason="live_preflight_malformed_json"`. The UI did not fall back to the generic disabled guard when live config was present.
- Because preflight blocked with a fixed provider diagnostic, the live turn runner did not apply a model proposal in this smoke. Deterministic fallback cards remained visible and clearly marked as fallback rather than live success. Raw prompt, provider body, credentials, and secret sentinel scans were clean.

## Sanitization

The UI must not render or persist raw prompts, raw completions, provider payloads, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display sentinels, raw exception text, or secret-like sentinels. The UI state stores fixed outcome codes, trusted ids, provider/model labels, counts, and media provenance only.
