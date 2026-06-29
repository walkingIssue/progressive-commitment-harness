# Mock Hold Preparation Provider Notes

Sprint 010 adds provider-local mock hold preparation for selected itinerary candidates. This is not real booking, payment, search, or availability inventory.

Provider shape:

- `HoldPreparationPacket` carries a packet id, operation, selected slot/candidate ids, locale, optional approval token, and optional sanitized context digest.
- `IHoldPreparationAdapter` returns `HoldPreparationResult` for preview or hold preparation.
- `MockHoldPreparationAdapter` is deterministic and supports preview, hold success, approval missing/rejected, packet mismatch, provider unavailable, and unsupported-result paths.

Approval-token posture:

- Preview is read-only and does not require approval.
- Hold preparation is commit-like and requires a non-blank approval token matching the configured required token.
- Missing, blank, or mismatched approval tokens block before hold success.
- Approval tokens must never be logged or persisted in eval/status rows.

Future live adapters must remain guarded:

- no required network, search, booking, payment, or API-key dependency
- no silent paid-provider fallback
- strict timeouts and typed provider errors
- no raw provider payload, payment data, credentials, approval tokens, or raw exception text in diagnostics
- live hold/book/pay paths must require explicit approval-token validation before any side-effect-like operation
