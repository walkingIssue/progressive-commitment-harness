# Sprint 017 Japan Card Media Pack

The reusable deterministic media pack lives at `src/Pch.UI/wwwroot/media/japan-card-pack/manifest.json`.

All current assets are generated local SVG mood art for required offline tests and smoke. No stock API, provider media URL, scraping, raw search query, API key, or external provider payload was used.

Manifest coverage:

- `cultural_immersive`
- `scenic_relaxed`
- `lively_food`
- `calm_morning`
- `reflective_culture`
- `soft_nature`
- `restorative_downtime`
- `logistics_transit`
- `mood_placeholder` fallback

Future provider or stock integrations should preserve the same fields: media id, mood/category, path or provider-safe URL, alt text, dominant color, source class, license, attribution text, source URL when applicable, and sanitized error/fallback codes.
