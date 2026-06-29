# Hold Preparation Eval Notes

Hold preparation eval/status rows are sanitized by default. They verify provider-local preview and hold preparation behavior without persisting raw booking/search/provider content.

All persisted eval/status artifacts follow the shared provider policy in `docs/evals/sanitized-artifacts.md`.

Persisted eval rows contain only:

- eval case name and packet id
- fixed outcome and error codes
- trusted packet slot ids and candidate ids for accepted rows
- candidate categories
- candidate count
- provider/model/request metadata and response length for accepted rows only

Persisted eval rows must not contain raw provider payloads, credentials, approval tokens, payment data, candidate titles, hold reference ids, context digests, secrets, sentinels, or raw exception messages.

Packet/result id mismatch, selected candidate mismatches, missing approval, approval mismatch, and unsupported provider results use fixed outcomes and omit provider result metadata. Provider exceptions use fixed error codes and do not echo raw exception messages.

Optional live hold preparation is skipped by default. Future live adapters must block on provider failure, timeout, malformed output, missing approval, approval mismatch, or credential/credit unavailability, and must not fall back to another paid provider.
