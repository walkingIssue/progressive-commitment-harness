# Media Source Registry Eval Notes

Media source registry eval rows are provider-local release artifacts for card media readiness and provenance. They are designed to explain whether trusted itinerary candidates received displayable media without becoming a storage path for search queries, provider payloads, raw URLs, keys, or exception text.

Accepted rows may persist:

- safe eval case name and trusted packet id
- trusted slot id, candidate id, and media candidate category from the packet
- media id
- media source class, source id, and provider name
- media license class and license name
- attribution author name, author URL, and attribution text
- image dimensions
- response content length
- accepted provider/model/request metadata

Accepted rows intentionally do not persist image URLs, thumbnail URLs, source URLs, raw alt text, raw provider payloads, search queries, keys, credentials, or candidate display values.

Rejected rows use fixed identifiers:

- row name `media_registry_rejected`
- packet id `media_registry_packet_redacted`

Fixed outcomes:

- `media_registry_accepted`: result packet id and exact trusted candidate set matched, and media assets had supported source/license metadata.
- `media_registry_packet_mismatch`: result packet id did not match the trusted packet.
- `media_registry_candidate_mismatch`: result candidate rows were missing, duplicated, unknown, or category-mismatched.
- `media_registry_malformed_packet`: packet, candidate collection, candidate id, slot id, or category was malformed.
- `media_registry_malformed_result`: result, mapping collection, asset shape, dimensions, or required metadata was malformed.
- `media_registry_unsupported_source`: media source class or required source metadata was unsupported.
- `media_registry_unsupported_license`: media license class or required license metadata was unsupported.
- `media_registry_timeout`: provider media source timed out.
- `media_registry_provider_unavailable`: provider media source failed with a provider error.
- `media_registry_error`: unexpected source error.

Required tests are deterministic and offline. Optional Pexels, Unsplash, Openverse, Wikimedia, Amadeus, or other provider-media lookups remain disabled by default and must be key/health/rate-limit/timeout/license guarded with no arbitrary scraping and no paid-provider fallback.
