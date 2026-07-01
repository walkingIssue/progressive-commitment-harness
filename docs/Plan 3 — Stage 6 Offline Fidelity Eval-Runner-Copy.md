# Plan 3 — Stage 6 Offline Fidelity Eval-Runner
Actually measure the project's central thesis: does a small model produce faithful structured output when fed only a compiled stage packet, versus a strong model versus harness-only? This replaces the deterministic stand-in in `FidelityMatrix` with real model output. It is the only plan that spends credits, so it is credential- and credit-gated and never runs in required CI.
## Problem
The decisive Stage 6 "fidelity bake-off" in `docs/PLAN.md` is the falsifiable core of the project, but `FidelityMatrix` (`src/Pch.Harness/FidelityMatrix.cs`) scores deterministic fixture packets, not live model output — `ResolveStageOwnership` hardcodes which stages "need" a small/strong model. The one real attempt (Sprint 003) had `qwen/qwen3-14b` return empty content. So the per-stage ownership matrix is asserted, not measured.
## Current State
* `IModelCompletionClient` (`src/Pch.Providers/ModelCompletion/IModelCompletionClient.cs`) + `ModelCompletionRequest` (with `JsonSchemaName`/`JsonSchema` strict-schema fields) is the model seam. `OpenRouterModelCompletionClient` implements it with a credit guard, timeout/cancellation, typed errors, and no secret/raw-body logging.
* `ExternalActionDecoder` + `HarnessActionIntake` are the real trust boundary that decides whether a model action is schema-valid, allowed for the stage, and non-mutating. Scoring model output by running it through these is more faithful than a bespoke validator.
* `GoldenPacketCorpus` (`src/Pch.Harness/GoldenPacketCorpus.cs`) yields the canonical packets to feed the model. `docs/evals/sanitized-artifacts.md` defines exactly what an eval row may persist.
## Proposed Changes
### 1. Eval entrypoint (gated)
Add a small runnable surface — a `Pch.Evals` console project (preferred) or an env-gated xUnit theory — that reads model ids and the target provider from config/env and is skipped by default. It must key/credit-guard before any call, use strict timeouts, and never fall back to another paid provider (production bar in `docs/PLAN.md`).
### 2. Structured-output preflight
Before spending budget, send one tiny `json_schema` strict request per configured model and assert non-empty, schema-valid content. Models that fail the preflight (the `qwen3-14b` empty-content failure mode) are reported as `structured_output_unsupported` and excluded, so a plumbing failure is never mistaken for a faithfulness failure.
### 3. Bake-off runner
Add `FidelityBakeOffRunner` that, for each golden packet × each model tier (small, strong) and a harness-only baseline:
* builds a `ModelCompletionRequest` from the packet (system instruction + flat packet JSON + a strict `json_schema` for the packet's `AllowedActions`),
* calls `IModelCompletionClient`,
* routes the raw output through `ExternalActionDecoder` → `HarnessActionIntake` against a throwaway session, and scores: schema-validity (decode success), candidate-ID preservation (every emitted id ∈ `packet.Candidates`), unsupported-claim rate (claim/summary references resolve to packet/`ClaimLedger` evidence), intake acceptance, latency, and token usage from `ModelUsage`.
### 4. Sanitized report + ownership matrix
Emit a `FidelityBakeOffReport`: per packet × model row with fixed codes, trusted ids, and counts only — no raw model text, per `docs/evals/sanitized-artifacts.md` and the shared `SanitizedEvalArtifactAssert` helper. Raw artifacts (if any) go to `artifacts/` (gitignored); the committed output is the sanitized matrix. The resulting per-stage ownership (harness-only / small-model-candidate / strong-required / blocked) is the real input that should later replace the hardcoded assumptions in `FidelityMatrix.ResolveStageOwnership`.
### 5. Model configuration
Document candidate small models that reliably honor OpenAI-style `json_schema` strict output, and record results in `docs/evals/`. Keep model ids in config/env, not code.
## Worker Lanes (for the coordinator)
* Lane A — `Pch.Evals` entrypoint, config/env loading, preflight, credit/key guards.
* Lane B — `FidelityBakeOffRunner` + scoring (reuses decoder/intake) + `FidelityBakeOffReport` types.
* Lane C — Sanitized-artifact tests (rejected/error rows through `SanitizedEvalArtifactAssert`) + `docs/evals` writeup.
Lanes A/B/C can proceed against fakes; live model runs happen only after merge, manually, with credits available.
## Dependencies
* Benefits from Plan 2's packet/scenario fixtures but does not block on it.
* Should be sequenced after Plan 1 if you want to first judge usefulness before spending credits.
## Verification
* Required CI: runner is skipped/guarded; sanitized-artifact tests pass with fakes; no live calls in required tests.
* Manual gated run: preflight passes for chosen models; bake-off produces a populated matrix; assert no raw prompt/model text, tokens, or secrets appear in persisted rows.
## Out of Scope
Real availability/booking calls, automatic rewrite of `FidelityMatrix` ownership (a follow-up once real data exists), and any non-gated execution path.
## Risks
* Small-model structured-output fragility — mitigated by the preflight and retry-on-validation, but a model may still fail comparison/summarization; report it as strong-required rather than forcing it.
* Credit exhaustion mid-run — honor the existing credit guard and stop cleanly; never switch providers.
* Accidental sensitive persistence — every row goes through the shared sanitizer + assert helper before being written.
