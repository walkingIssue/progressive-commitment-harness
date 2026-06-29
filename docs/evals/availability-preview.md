# Availability Preview Eval Notes

Availability preview eval/status rows are provider-local release artifacts for deterministic quote previews. They verify quote availability for trusted itinerary slot/candidate selections without performing booking, payment, holds, live search, or live availability calls.

Supported preview categories:

- flight
- lodging
- activity
- dining
- transit

Persisted `quote_ready` rows contain only:

- eval case name and trusted packet id
- fixed outcome and error codes
- trusted slot ids, candidate ids, and categories from the packet
- allowlisted candidate status enums
- candidate, quote-ready, and unavailable counts
- provider/model/request metadata and response length

`unavailable` rows may keep trusted packet-derived candidate rows and counts, but omit provider/model/request metadata and response length. Blocked/error rows contain fixed codes and zero counts only; they omit candidate rows, provider metadata, response length, provider-returned ids, quote refs, and payload values.

Fixed outcomes:

- `availability_preview_quote_ready`: every trusted candidate has an exact provider result and a quote-ready status.
- `availability_preview_unavailable`: every trusted candidate has an exact provider result and an unavailable status.
- `availability_preview_packet_mismatch`: result packet id differs from the trusted packet id.
- `availability_preview_candidate_mismatch`: result candidate ids do not exactly match the trusted packet slot/candidate set or categories.
- `availability_preview_malformed_packet`: trusted packet is null, empty, duplicated, blank, or has an unsupported category.
- `availability_preview_malformed_result`: result is null, malformed, or uses statuses inconsistent with the result kind.
- `availability_preview_unsupported_result`: provider reports an unsupported result kind.
- `availability_preview_unsupported_category`: provider result includes an unsupported category enum.
- `availability_preview_timeout`: provider preview timed out.
- `availability_preview_provider_unavailable`: provider preview failed with a provider-unavailable error.
- `availability_preview_error`: unexpected adapter error.

Persisted rows must not contain raw provider payloads, fare or booking references, payment data, credentials, approval tokens, candidate display values, raw prompts, raw exception text, context digests, or sentinels. Tests use `SanitizedEvalArtifactAssert` from the shared policy in `docs/evals/sanitized-artifacts.md`.

Required tests are deterministic and offline. Optional live previews remain skipped by default and must be guarded by key presence, provider health, strict timeout handling, empty/malformed output blocking, and no paid-provider fallback.
