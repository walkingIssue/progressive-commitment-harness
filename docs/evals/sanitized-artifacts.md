# Sanitized Provider Eval Artifacts

Provider eval/status artifacts are release-facing diagnostics. They must be useful for debugging fixed-code outcomes without becoming a persistence path for raw prompts, provider payloads, credentials, approval tokens, booking references, payment data, or user-derived sensitive values.

Persisted-safe fields are limited to:

- eval case name, trusted packet id, scenario labels, and expected values supplied by deterministic test cases
- fixed outcome, decode, intake, and error codes
- trusted packet ids such as slot ids, candidate ids, evidence ids, and plan ids when the lane explicitly validates them against packet-owned data
- counts for fields, constraints, commitments, candidates, evidence, holds, pending confirmations, and response content length
- provider/model/request metadata only on accepted rows or lane-documented safe paths

Rejected, blocked, malformed, unsupported, mismatch, and exception rows must use fixed outcome/error codes and must not echo raw provider result fields. They must omit raw provider result metadata when the result itself is untrusted or failed validation.

Persisted artifacts must not contain:

- raw prompt text or packet context digests
- raw model/provider response payloads
- raw exception messages
- approval tokens, hold references, payment data, credentials, or secret-like values
- candidate display values, candidate tags, commitment titles, constraint values, field values, memory digests, or provider-returned ids outside trusted packet maps
- test sentinels used to prove redaction behavior

The shared provider test fixture `SanitizedEvalArtifactAssert` serializes eval/status artifacts with the same JSON defaults used by lane tests and asserts that sensitive sentinels are absent. New provider eval lanes should add at least one rejected/error-path test through this helper before becoming release artifacts.

Optional live smoke paths remain skipped or blocked by default. A live smoke must be key and health/credit guarded where applicable, use strict timeout/error handling, and must not fall back to another paid provider when blocked.
