# Provider Media Source Registry

Sprint 017 adds a provider-local media source registry shape for candidate card media. The provider project stays dependency-light and exposes DTOs that can later be mapped into the harness media provenance contract without referencing harness or UI assemblies.

Core DTOs:

- `MediaRegistryPacket` carries trusted packet id, locale, and candidate identity rows.
- `MediaRegistryCandidate` carries trusted slot id, candidate id, category, and optional sanitized mood token.
- `CandidateMediaMapping` attaches media assets to a trusted candidate.
- `MediaAsset` carries media id, source, license, attribution, dimensions, optional runtime URLs, alt text, and color token.
- `MediaSource` records source id, source class, provider name, and optional source URL.
- `MediaLicense` records license class, license name, license URL, attribution requirement, and commercial-use flag.

Required tests use `MockMediaRegistrySource` only. It returns deterministic media for flight, lodging, activity, dining, transit, and downtime candidates without network, API keys, scraping, booking, payment, or live provider calls.

Optional provider metadata clients are represented by disabled descriptors for Pexels, Unsplash, Openverse, and Wikimedia. Any future implementation must be opt-in and guarded by key presence when required, provider health, rate limits, strict timeout handling, empty or malformed response blocking, license validation, attribution capture, and no paid-provider fallback.

Provider-supplied media posture:

- Availability, hotel, location, or booking providers may return media URLs and source terms as part of their own content APIs.
- Amadeus hotel/content responses can be accepted when the provider result includes photos and terms permit display, but the availability preview path must not assume every hotel or result has images.
- Provider-supplied images should be attached only when source/license/attribution/dimensions can be represented in `MediaAsset`.
- Missing provider media is a normal state; downstream UI should fall back to generated art, stock/open-license media, or deterministic placeholders.

No arbitrary scraping is allowed in this provider lane. A future scraper would need explicit source terms, robots posture, license handling, strict guards, and no default live execution.

Sanitization posture:

- Accepted eval rows persist media ids, source class/id/provider name, license class/name, attribution fields, trusted slot/candidate/category ids, dimensions, response length, and accepted provider/model/request metadata.
- Eval rows do not persist image URLs, thumbnail URLs, alt text, search queries, provider payloads, API keys, credentials, raw exceptions, failed-response URLs, or sentinel strings.
- Rejected/error rows use fixed redacted row identifiers and omit provider result metadata.
