# Provider Availability Preview Adapter

Sprint 014 adds provider-local availability/quote preview DTOs, evaluator rows, and deterministic mock adapter behavior. The adapter stays dependency-light and does not reference harness assemblies.

The packet owns the trusted slot/candidate/category boundary. Accepted rows derive persisted slot ids, candidate ids, and categories from `AvailabilityPreviewPacket.Candidates` only after exact validation against the adapter result. Provider quote amounts, currencies, expiry times, fare references, booking references, raw payloads, and candidate display values are never persisted in eval/status rows.

Mock behaviors cover:

- quote-ready preview
- unavailable preview
- packet mismatch or stale packet result
- provider timeout
- malformed result
- unsupported result
- unsupported category
- candidate mismatch
- provider unavailable

Future live providers such as Amadeus or other availability/search APIs must remain optional. A live adapter must require explicit enablement, key presence, provider health checks, strict request and body-read timeouts, empty/malformed output blocking, fixed sanitized error codes, and no fallback to another paid provider. Live paths must not perform booking, payment, or hold creation from this preview adapter.
