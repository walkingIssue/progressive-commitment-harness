# Sprint 017 - Visual Card Media And End-User Delight

Coordinator: Collin

Dispatch base: `08c13de25af7deec16496bd128ac23ff95ae33dd`

## Objective

Make the end-user trip planner feel like a product a human can actually enjoy trying, while preserving the typed harness boundaries that keep it honest.

Sprint 017 should add reusable Japan card imagery, a media/provenance contract, and a polished card/deck/evidence-trail integration. The goal is not only "pretty cards"; the goal is a user-facing planning surface where recommendations feel vivid, selections feel tangible, and provenance remains visible without reading like a debug console.

## Visual Target

Use the Sprint 016 reference as the product grammar:

- `docs/design/assets/sprint-016-web-gpt-reference.png`
- `docs/design/end-user-chat-interaction-primitives.md`

Keep the structure:

- narrow left rail;
- warm off-white main canvas;
- dark task rail;
- agent work bubble as the visual center after first prompt;
- collapsed `Ask` drawer after first send;
- mood-backed stacked candidate decks;
- selected option echoed as a compact card bubble, not a text sentence;
- evidence/plan trail as a browsable memory of planned, selected, deferred, approved, and blocked items.

Color/art direction:

- reflective culture: cherry, indigo, paper, lantern glow;
- scenic relaxed: sea glass, sky, moss, mist;
- lively food: vermilion, warm amber, market neon, ceramic blue;
- calm morning: pale sun, rice paper, soft green, low contrast;
- restorative downtime: lavender grey, warm wood, bathhouse steam;
- logistics/transit: crisp blue, charcoal, signal green, clean linework;
- approval/commitment: heavier coral/amber treatment with clear gated language.

Avoid generic Bootstrap surfaces, stock-photo darkness, decorative orbs, and debug-looking metadata.

## Source Posture

Preferred sources, in order:

1. Provider-supplied media, if a booking/search/content API returns media URLs and its terms allow display.
2. Free-commercial stock APIs, especially Pexels and Unsplash, following API/license/attribution requirements.
3. Open-license repositories, especially Openverse and Wikimedia Commons when explicit license metadata matters.
4. Generated mood art for scenic/non-specific cards and deck backdrops.
5. Local deterministic placeholders for tests/offline fallback only.

Avoid scraping arbitrary hotel/location pages unless the source's terms, robots posture, and license are explicit. Even before public launch, committed assets should be reusable or clearly marked temporary/internal-only.

Source notes:

- Amadeus hotel APIs advertise hotel search/booking/content data with detailed information and photos: <https://developers.amadeus.com/self-service/category/hotels>
- Pexels license permits free personal/commercial use, with restrictions on endorsement, redistribution, and unmodified resale: <https://www.pexels.com/license/>
- Pexels API docs ask applications to link/credit Pexels and photographers when possible and document rate limits: <https://www.pexels.com/api/documentation/>
- Unsplash license permits free commercial/non-commercial use with restrictions on selling unmodified images or replicating a competing service: <https://unsplash.com/license>
- Unsplash API/display guidelines require attribution links when displaying photos from the API: <https://help.unsplash.com/en/articles/2511315-guideline-attribution>

## Lane A - Provider Media Source Registry

Owner: Kaneki

Branch: `sprint-017/provider-media-source-registry`

Owns:

- `src/Pch.Providers/**`
- `tests/Pch.Providers.Tests/**`
- `docs/providers/**`
- `docs/evals/**`

Deliverables:

- Provider-local DTOs for `MediaAsset`, `MediaLicense`, `MediaSource`, and candidate-to-media mapping.
- Deterministic mock media source for tests.
- Optional guarded clients/fetchers for Pexels, Unsplash, Openverse, and Wikimedia metadata, disabled in required tests.
- Amadeus/provider-media notes describing how to accept provider-returned media when available without assuming every availability result has images.
- Sanitized eval/status rows that persist only media ids, source, license class, attribution fields, candidate/category ids, dimensions, and fixed error codes.
- Rejected/error rows must not persist raw search queries, provider payloads, API keys, image URLs from untrusted failed responses, raw exception text, credentials, or sentinel strings.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

## Lane B - Harness Media Provenance Boundary

Owner: Shellby

Branch: `sprint-017/harness-card-media-contract`

Owns:

- `src/Pch.Core/**`
- `src/Pch.Harness/**`
- `tests/Pch.Core.Tests/**`
- `tests/Pch.Harness.Tests/**`
- `tests/fixtures/**`

Deliverables:

- Harness/core DTOs for card-safe media references that can attach to candidate options, selected-option echoes, and evidence/plan-trail items without referencing provider implementation types.
- Validation for allowed media source classes, bounded attribution fields, safe alt text, safe dominant-color tokens, image dimensions, and unsafe/sentinel string redaction.
- A deterministic media manifest fixture for Japan mood cards that UI can consume before live/provider media is available.
- Projection support so selected/deferred cards and evidence trail items can carry safe media references while preserving candidate ids and evidence ids.
- No network, scraping, live media calls, or UI/provider edits.

Verification:

- `dotnet test tests/Pch.Core.Tests/Pch.Core.Tests.csproj`
- `dotnet test tests/Pch.Harness.Tests/Pch.Harness.Tests.csproj`
- `dotnet build`

## Lane C - End-User Card Imagery And Deck Integration

Owner: Sarah

Branch: `sprint-017/end-user-card-media-ui`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`
- `docs/design/**`

Deliverables:

- A reusable Japan visual pack under `src/Pch.UI/wwwroot/` or `docs/design/assets/` with manifest entries for:
  - cultural_immersive
  - scenic_relaxed
  - lively_food
  - calm_morning
  - reflective_culture
  - soft_nature
  - restorative_downtime
  - logistics_transit
- Generated scenic/abstract backdrops suitable for stacked card decks.
- A local asset manifest mapping mood/category to image path, alt text, dominant color, attribution/source, and generated/stock/provider source class.
- Candidate cards use images/backdrops from trusted media metadata or the local manifest.
- Same-mood decks become visibly floaty/stacked/scrollable with partial neighboring cards.
- Selected option card bubbles retain thumbnail/backdrop and provenance.
- Evidence/plan trail can show selected card imagery without making evidence look like an active choice.
- Missing media gracefully falls back to generated mood art or deterministic placeholders.
- Remove any remaining visual traces that make the end-user UI look like a Blazor template or internal fixture board.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`
- browser smoke for first prompt, Ask drawer, deck scroll/swipe, selected card bubble, evidence trail imagery, missing-media fallback, scaffold absence, and raw-sentinel absence.

## Policy Notes

- Store source URL, author/photographer, license, attribution text, and source class with every non-generated asset.
- Do not commit API keys, raw provider payloads, or untrusted failed-response URLs.
- Do not hotlink in production unless the provider explicitly requires or allows it.
- For generated assets, store prompt, generation date, mood/category, and intended usage in docs/design metadata.
- Real hotel/location images should preferably come from the booking/search provider's content API or a licensed media API, not an arbitrary scraper.
- If a future scraper becomes necessary, it must be a guarded provider lane with explicit terms/robots/license handling and no default live execution.

## Exit Criteria

- End-user candidate cards and selected-option bubbles are visibly image-backed in deterministic/offline mode.
- The evidence/plan trail can display selected imagery as memory/provenance, not active choices.
- The UI can distinguish generated art, stock photos, and provider-supplied media.
- Every reusable stock/open/generated image has source/license/prompt/attribution metadata.
- Real hotel/location cards can accept provider media URLs when available without changing card components.
- Required tests remain offline and deterministic.
