# Candidate Expansion Eval Notes

Candidate expansion eval rows are sanitized by default. They are intended to verify that provider candidate pools exist for itinerary slots without persisting raw search/model/provider content.

Persisted eval rows contain only:

- eval case name and packet id
- fixed outcome and error codes
- slot ids
- candidate categories
- candidate counts
- total candidate count
- provider/model/request metadata and response length

Persisted eval rows must not contain raw prompt text, provider payloads, provider-returned slot ids outside the trusted packet, candidate display names, candidate descriptions, candidate tags, context digests, secrets, credentials, approval tokens, or raw exception messages.

Accepted eval rows derive persisted slot ids and categories from the trusted packet slot map and require the provider result slot set to exactly match the packet slot set. Provider results with missing slot ids, unknown slot ids, duplicate slot ids, or category mismatches use the fixed `candidate_expansion_slot_mismatch` outcome and omit result slot metadata. Packet/result id mismatches use the fixed `candidate_expansion_packet_id_mismatch` outcome and omit result slot metadata. Exceptions use fixed error codes and do not echo raw exception messages.

Optional guarded-live seams are skipped by default. Future strong-model or web-search expansion must block on provider failure, timeout, malformed output, empty output, or credit exhaustion, and must not fall back to another paid provider.
