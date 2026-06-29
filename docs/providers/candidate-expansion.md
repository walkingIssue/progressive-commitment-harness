# Candidate Expansion Provider Notes

Sprint 009 adds provider-local candidate expansion DTOs and a deterministic source for itinerary slot candidate pools. This is not real search, booking, or availability lookup.

Provider shape:

- `CandidateExpansionPacket` carries a packet id, locale, optional sanitized context digest, and itinerary slots.
- Each `CandidateExpansionSlot` is tied to a slot id and one category: dining, activity, transit, or downtime.
- `ICandidateExpansionSource` returns `CandidateExpansionResult` with slot-scoped `ItineraryCandidate` values.
- `MockCandidateExpansionSource` is deterministic and is the default source for required tests.

Future live expansion through a strong model or web/search provider must remain guarded:

- no required network, API-key, search, booking, or provider-credit dependency
- no silent fallback to another paid provider
- strict timeouts and typed provider errors
- empty, malformed, or mismatched provider output must block
- credentials, raw provider payloads, prompts, approval tokens, and raw exception text must not be logged or persisted

Candidate DTOs may contain explicitly safe display fields for immediate in-memory UI/harness mapping. Sanitized eval rows must not persist candidate display names, descriptions, tags, raw prompts, provider payloads, secrets, credentials, or raw exception messages.
