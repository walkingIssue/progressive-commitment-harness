# Evidence Export Provider Notes

Sprint 011 adds provider-local evidence/export DTOs for deterministic final trip-plan outputs. This bridge is mock-only and does not perform booking, payment, search, availability lookup, or live provider calls.

Provider shape:

- `EvidenceExportPacket` carries a packet id, safe final plan summary counts, evidence items, hold outcomes, locale, and an optional sanitized context digest.
- `IEvidenceExportProvider` returns `EvidenceExportResult` with a provider-local `TripPlanEvidenceExport`.
- `MockEvidenceExportProvider` deterministically emits export-ready, packet mismatch, result mismatch, unsupported, malformed, and provider-unavailable paths for tests.

The provider project remains dependency-light. These DTOs are provider-local mirrors until a coordinator-owned integration scope maps them into a harness trip-run snapshot contract.

Future live/export/search adapters must remain guarded:

- required tests must not use network, provider credits, API keys, booking, payment, live search, or live availability
- no silent paid-provider fallback
- strict timeout and typed provider error handling
- malformed, unsupported, or mismatched provider output must block
- credentials, approval tokens, hold references, raw provider payloads, prompt text, candidate display values, raw exception text, and sentinels must not be logged or persisted
