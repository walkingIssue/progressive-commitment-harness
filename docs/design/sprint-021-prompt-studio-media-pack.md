# Sprint 021 Prompt Studio Media Pack

## Source

- Source directory: `C:\Users\Bartek\Documents\Playground\pch-prompt-studio\artifacts`
- Metadata source: `catalog.json`
- Runtime copy: `src/Pch.UI/wwwroot/media/japan-prompt-studio-pack/`
- UI manifest: `src/Pch.UI/wwwroot/media/japan-prompt-studio-pack/manifest.json`

## Import Summary

- Source catalog entries at import time: 240
- PNGs available at import time: 240
- PNGs imported for Sprint 021 Lane C: 16

The imported set is deliberately bounded and representative. It favors broad mood/domain coverage over near-duplicate variants while the prompt-studio workspace is still changing.

## Manifest Shape

Each manifest asset keeps the prompt-studio metadata needed by the UI without hotlinking to the prompt-studio workspace:

- `assetId`
- `label`
- `domain`
- `category`
- `variant`
- `mood`
- `imageType`
- `season`
- `anchors`
- `path`
- `sourceClass`
- `license`
- `attribution`
- `state`

## Coverage

Imported moods include cultural immersive, scenic relaxed, food cozy, budget practical, family easy, wellness restorative, arts design, coastal breezy, spiritual serene, romantic slow, urban kinetic, seasonal festival, night social, active outdoors, rural nostalgic, and refined premium.

The current `/trip` UI uses these PNGs for candidate cards, selected-card echoes, planning timeline thumbnails, and fallback media.

## Deferred Queue

The remaining prompt-studio PNGs are deferred for a later bulk import pass after generation stabilizes. A future pass should add richer per-destination/season matching while preserving the same manifest schema and no-hotlink runtime rule.

## Safety Notes

The imported assets are treated as project-generated local media. The UI does not render prompt text, negative prompts, raw provider payloads, credentials, approval tokens, hold references, booking/payment references, or raw exception text from the prompt-studio catalog.
