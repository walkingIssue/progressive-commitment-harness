# Sprint 017 - Card Imagery And Media Source Pipeline

Coordinator: Collin

Planning base: after Sprint 016 publishes.

## Objective

Make itinerary, hotel, activity, dining, and evidence cards visually rich without turning image sourcing into an unsafe scrape-and-pray layer.

Sprint 017 should produce a reusable visual asset set for Japan trip cards, a provider/media abstraction for real candidate images, and a licensing/attribution policy that keeps the project safe enough for eventual public release.

## Source Posture

Preferred sources, in order:

1. **Provider-supplied media:** if a booking/search/content API returns media URLs and its terms allow display, use those for real hotels/locations.
2. **Free-commercial stock APIs:** Pexels and Unsplash for broad scenic/travel imagery, following their API/license requirements.
3. **Open-license repositories:** Openverse and Wikimedia Commons when explicit license/attribution metadata matters.
4. **Generated mood art:** AI-generated scenic/abstract backdrops for mood decks and non-specific itinerary cards.
5. **Local deterministic placeholders:** only for tests/offline fallback.

Avoid scraping arbitrary hotel/location pages unless the source's terms, robots posture, and license are explicit. Even before public launch, committed assets should be reusable or clearly marked as temporary internal-only references.

Source notes:

- Amadeus hotel APIs advertise hotel search/booking/content data with detailed information and photos: <https://developers.amadeus.com/self-service/category/hotels>
- Pexels license permits free personal/commercial use, with restrictions on endorsement, redistribution, and unmodified resale: <https://www.pexels.com/license/>
- Pexels API docs ask applications to link/credit Pexels and photographers when possible and document rate limits: <https://www.pexels.com/api/documentation/>
- Unsplash license permits free commercial/non-commercial use with restrictions on selling unmodified images or replicating a competing service: <https://unsplash.com/license>
- Unsplash API/display guidelines require attribution links when displaying photos from the API: <https://help.unsplash.com/en/articles/2511315-guideline-attribution>

## Lane A - Media Source And License Registry

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
- Optional guarded clients or fetchers for Pexels, Unsplash, Openverse, and Wikimedia metadata, disabled in required tests.
- Sanitized eval/status rows that persist only media ids, source, license class, attribution fields, candidate/category ids, dimensions, and fixed error codes.
- Docs for free-commercial/open-license media usage and attribution expectations.
- Notes on Amadeus/hotel provider media behavior: use provider-returned media where available, but do not rely on every availability result having images.

Verification:

- `dotnet test tests/Pch.Providers.Tests/Pch.Providers.Tests.csproj`
- `dotnet build src/Pch.Providers/Pch.Providers.csproj`

## Lane B - Generated Japan Card Asset Pack

Owner: Sarah or a UI/design-focused worker

Branch: `sprint-017/japan-card-asset-pack`

Owns:

- `src/Pch.UI/wwwroot/**`
- `docs/design/**`
- `tests/Pch.UI.Tests/**` where asset metadata is rendered/tested

Deliverables:

- A reusable Japan visual pack for mood-backed cards:
  - cultural_immersive
  - scenic_relaxed
  - lively_food
  - calm_morning
  - reflective_culture
  - soft_nature
  - restorative_downtime
  - logistics/transit
- Generated scenic/abstract backdrops suitable for stacked card decks.
- A local asset manifest mapping mood/category to image path, alt text, dominant color, and safe attribution/source fields.
- UI tests proving cards render image paths/alt text from the manifest and preserve candidate ids/evidence ids.

Verification:

- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`

## Lane C - Media Integration Into Card Decks

Owner: Sarah

Branch: `sprint-017/card-media-integration`

Owns:

- `src/Pch.UI/**`
- `tests/Pch.UI.Tests/**`

Deliverables:

- Candidate cards consume media from trusted candidate media metadata or the local asset manifest.
- Same-mood decks use image/backdrop differences while keeping the mood group visually coherent.
- Selected option card bubbles retain thumbnail/backdrop and provenance.
- Evidence/plan trail can show selected card imagery without making evidence look like an active choice.
- Missing media gracefully falls back to generated mood art or deterministic placeholders.

Verification:

- browser smoke for deck scroll/swipe, selected card bubble, evidence trail imagery, missing-media fallback, and raw-sentinel absence.
- `npm run build:ui`
- `dotnet build src/Pch.UI/Pch.UI.csproj`
- `dotnet test tests/Pch.UI.Tests/Pch.UI.Tests.csproj`

## Policy Notes

- Stock/source APIs may require attribution or API-specific display rules even when images are free to use.
- Store source URL, author/photographer, license, and attribution text with every non-generated asset.
- Do not commit raw provider payloads or API keys.
- Do not hotlink in production unless the provider explicitly requires or allows it.
- For generated assets, store the prompt and generation date in docs/design metadata.

## Exit Criteria

- Candidate cards and evidence cards have visually useful imagery in deterministic/offline mode.
- The UI can distinguish generated art, stock photos, and provider-supplied media.
- Every reusable stock/open image has source/license/attribution metadata.
- Real hotel/location cards can accept provider media URLs when available without changing card components.
- Required tests remain offline and deterministic.
