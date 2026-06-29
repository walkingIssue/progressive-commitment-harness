# Evidence Export Eval Notes

Evidence export eval/status rows are sanitized by default. They verify final deterministic trip-plan summary/export readiness without persisting raw provider, prompt, booking, or payment content.

All persisted eval/status artifacts follow the shared provider policy in `docs/evals/sanitized-artifacts.md`.

Persisted accepted rows contain only:

- eval case name and packet id
- fixed outcome and error codes
- trusted packet plan id
- selected/deferred/prepared-hold/evidence counts
- trusted evidence ids, slot ids, and candidate ids
- provider/model/request metadata and response length

Rejected rows contain only fixed codes and the trusted packet id. Packet/result id mismatch, result-id/count mismatch, unsupported result, and malformed result paths omit provider result metadata.

Persisted rows must not contain raw prompt text, provider payloads, approval tokens, hold references, candidate display values, credentials, payment data, raw exception text, context digests, or secret-like sentinels.

Future live/export/search seams are skipped by default. They must block on timeout, provider failure, malformed output, unsupported output, credential/credit unavailability, or mismatch, and must not fall back to another paid provider.
