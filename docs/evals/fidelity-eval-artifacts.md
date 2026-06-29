# Fidelity Eval Artifacts

Provider fidelity eval artifacts support the original Stage 6 bake-off by comparing small-model, strong-model, and harness-only candidate outputs. The comparison is diagnostic only: model output is never trusted for mutation, booking, payment, approval, or harness state changes.

Persisted accepted rows contain only:

- eval case name and trusted packet id
- fixed outcome and error codes
- trusted candidate ids and categories from the packet
- allowlisted candidate decision enums from each source
- source-level candidate counts, include/exclude/defer counts, agreement/disagreement counts
- provider/model/request metadata and response length for accepted source results

Rejected or blocked rows contain only the eval case name, trusted packet id, fixed outcome/error codes, and zero counts. They omit source rows, candidate comparison rows, provider metadata, response length, and provider-returned candidate ids.

Fixed outcomes:

- `fidelity_eval_agreed`: all three sources returned schema-valid output and every trusted candidate decision agreed.
- `fidelity_eval_disagreement`: all three sources returned schema-valid output, but at least one trusted candidate decision differed.
- `fidelity_eval_packet_id_mismatch`: a source result did not match the trusted packet id.
- `fidelity_eval_schema_invalid`: a source result reported schema-invalid output, had missing collections, or used the wrong source kind.
- `fidelity_eval_unsupported_claim`: a source returned a claim code outside the provider-local allowlist.
- `fidelity_eval_missing_candidate_id`: a source result did not exactly cover the trusted packet candidate id set.
- `fidelity_eval_fallback_required`: a source requested fallback; evaluation stops without invoking later sources.
- `fidelity_eval_source_error`: a source threw a timeout/provider/error condition, mapped to a fixed error code.

Persisted rows must not contain raw prompt text, provider payloads, proposal JSON, credentials, approval tokens, hold references, candidate display values, candidate tags, raw exception text, context digests, or secret-like sentinels. Tests use the shared `SanitizedEvalArtifactAssert` policy from `docs/evals/sanitized-artifacts.md`.

Required tests are deterministic and offline. Optional live fidelity checks remain disabled by default and must be guarded by provider health, key presence, credit checks where applicable, strict timeout handling, malformed/empty output blocking, and no paid-provider fallback.
