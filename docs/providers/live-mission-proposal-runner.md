# Live Mission Proposal Runner

Sprint 020 adds a provider-local live mission proposal runner for the first end-user mission proposal path. The runner is dependency-light and uses the existing `IModelCompletionClient` plus optional `IProviderCreditClient` seams. It does not reference harness or UI projects.

The runner returns provider-local DTOs that can be mapped into the harness live proposal envelope later:

- `LiveMissionProposalPacket` carries trusted packet id, session id, role, locale, allowed output kinds, and fallback posture.
- `LiveMissionProposalResult` carries packet/session/role, output kind, mission kind, field/commitment/pending-confirmation proposals, response length, and provider/model/request metadata.
- Field values, commitment titles, locations, and evidence id collections are runtime-only JSON-ignored properties. Eval/status artifacts persist only allowlisted metadata, counts, and fixed codes.

Configuration follows the live model vocabulary:

- `PCH_LIVE_MODEL_ENABLED` or `PCH_LIVE_MISSION_PROPOSAL_ENABLED` enables this runner.
- `PCH_LIVE_MODEL_KEY_AVAILABLE` or a recognized key env var marks key availability.
- Recognized key env vars are `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `XAI_API_KEY`, and `GROK_API_KEY`.
- `PCH_LIVE_MODEL_PROVIDER` selects `openrouter`, `openai`, or `grok-xai` style provider kind.
- `PCH_LIVE_MODEL_SKIP_CREDIT_GUARD=true` disables credit guard for providers without a credit endpoint.
- `PCH_LIVE_MODEL_SCHEMA_UNSUPPORTED=true` blocks with `live_mission_proposal_schema_invalid`.
- `PCH_LIVE_MODEL_ALLOW_PAID_FALLBACK=true` blocks with `live_mission_proposal_fallback_disabled`; this lane does not silently fall back to another paid provider.
- `PCH_LIVE_MODEL_TIMEOUT_SECONDS` sets the provider operation timeout.
- `PCH_LIVE_IN_HARNESS_MODEL` and `PCH_LIVE_STRONG_PLANNER_MODEL` override role model ids.

The strict JSON output shape includes packet id, session id, role, output kind `mission_proposal`, mission kind, fields, commitments, and pending confirmations. Accepted rows require exact packet id, session id, role, and allowed output-kind validation before provider metadata is retained.

Validated proposal vocabularies are provider-local mirrors:

- mission kinds: `vacation`, `business`, `funeral`, `helping_family`
- authority sources: `user_stated`, `model_inference_pending_confirmation`, `trusted_provider`
- field paths: canonical `/mission/...` paths
- commitment kinds: `travel`, `lodging`, `dining`, `activity`, `family_support`, `work`
- priorities: `normal`, `high`, `critical`
- pending reasons: `needs_user_confirmation`, `needs_date_confirmation`, `needs_budget_confirmation`, `needs_location_confirmation`

OpenRouter expected sequence when enabled:

1. Call `/api/v1/credits` when credit guard is enabled.
2. If credits are available, call `/api/v1/chat/completions` with strict JSON schema response format.
3. Persist only sanitized mission proposal status fields: fixed outcome, trusted packet/session metadata, allowlisted enums, counts, response length, provider/model/request metadata, and no raw model content.

Manual live smoke may spend credits only when explicitly configured. Required tests remain deterministic/offline and do not use network, keys, or provider credits.
