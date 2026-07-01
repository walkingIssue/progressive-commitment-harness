# Sprint 021 End-User Live Multi-Turn UI Report

## Scope

- `/trip` keeps deterministic/offline mode available and clearly labeled.
- Live mode no longer renders deterministic golden trace turns as the primary live-mode assistant cards.
- The UI prepares a two-turn live session surface: first provider/harness result, user selection, and second-turn attempted-or-blocked marker.
- Stage Cockpit remains separate on `/stage-cockpit`.

## Current Integration Status

- Shellby Sprint 021 harness multi-turn session contract: not integrated yet.
- Kaneki Sprint 021 provider live turn runner diagnostics: not integrated yet.
- UI fallback before those heads: first live turn still uses the Sprint 020 live proposal runner; second live turn is explicitly blocked with `live_multiturn_contract_pending`.

## Sanitized UI Outcomes

- First live accepted: `live_model_proposal_accepted`
- First live provider blocked/unavailable: `live_model_proposal_blocked`
- Harness validation blocked: `harness_validation_blocked`
- Second turn pending canonical integration: `live_multiturn_contract_pending`
- No-config guard: `blocked_by_guard`
- Deterministic fallback: `deterministic_fallback`

## Prompt-Studio Media

- Imported local PNG subset: 16 assets.
- Runtime manifest: `src/Pch.UI/wwwroot/media/japan-prompt-studio-pack/manifest.json`
- UI usage: candidate cards, selected-card echoes, planning timeline thumbnails, and fallback state imagery.
- Deferred import: remaining prompt-studio PNGs after the generation set stabilizes.

## Live Smoke Status

- Required tests and deterministic smoke remain offline.
- Real provider browser smoke should be attempted when explicit live config/keys are present.
- Sanitized local browser interaction logs may be written under gitignored `artifacts/live-runs/`.

## Sanitization

The UI must not render or persist raw prompts, raw completions, provider payloads, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display sentinels, raw exception text, or secret-like sentinels. The UI state stores fixed outcome codes, trusted ids, provider/model labels, counts, and media provenance only.
