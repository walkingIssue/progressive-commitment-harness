# Model Action Runner Eval Scaffold

Sprint 002 adds a provider-local model action runner for packet-shaped prompts. It does not consume or define core harness contracts; Sprint 003 can map these provider-local outputs into `HarnessAction` after the core contract owner freezes that surface.

Default evaluation uses deterministic mocks:

- packet-shaped prompt input
- a constrained list of allowed action names
- strict JSON action output with `action`, `arguments`, and optional `summary`
- pass/fail rows comparing the selected action to an expected action
- golden packet JSON loaded from files into provider-local eval cases
- sanitized eval rows with packet id, expected/actual action names, provider/model/request id, response length, and error codes only
- provider-local bridge rows include `decodeOutcomeCode` and `intakeOutcomeCode`; intake is marked `intake_not_run_provider_local_mirror` until the harness-owned decoder contract is available to providers

Provider-dependent evals may use OpenRouter `qwen/qwen3-14b` only when a key is present and `/api/v1/credits` reports usable credit. If credit is exhausted or provider checks fail, pause and report `BLOCKED`; do not silently fall back to a different hosted model.

Production-readiness notes:

- Required tests do not use live network calls, API keys, or provider credits.
- Exceptions and docs must not include raw key material, raw prompts containing secrets, or provider credentials.
- Runner results expose response content length and provider diagnostics, not raw model response text.
- Sanitized eval rows do not persist packet prompt text, raw model payloads, or exception messages.
- Disallowed model action failures use sanitized messages and do not echo the untrusted action text.
- The runner rejects action names outside the packet's allowed action list before any harness commit side effect is possible.
- Booking/hold/pay actions still require separate approval-token checks in commit adapters.
- Provider-local bridge proposals persist action kind and argument key names only; they do not persist raw argument values or raw model payload text.
- Runtime proposals mirror harness `ExternalActionProposal` with `ActionId`, `Kind`, and in-memory JSON `Arguments`. That raw argument JSON is ignored by JSON serialization and is intended only for immediate handoff to the harness/application layer.

Sprint 003 assumptions for mapping into `HarnessAction`:

- `action` maps to a future harness-owned action discriminator.
- `arguments` remain provider-local JSON until the harness owns typed argument schemas.
- Packet action definitions should become the bridge between harness-owned stage contracts and provider-local response schemas.
- The current provider runtime proposal mirrors the harness `ExternalActionProposal` shape available on the Sprint 005 base without adding a provider-to-harness project reference.
