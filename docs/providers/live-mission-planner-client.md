# Live Mission Planner Client Notes

Sprint 008 adds `ModelCompletionMissionPlannerClient`, an `IMissionPlannerClient` implementation that adapts an OpenAI/OpenRouter-compatible model completion client into provider-local `MissionPlannerResult` DTOs.

Client shape:

- `ModelCompletionMissionPlannerClient` depends on `IModelCompletionClient`, not on harness/UI assemblies.
- OpenRouter usage is provided by composing `OpenRouterModelCompletionClient`, which preserves existing API-key loading, credit checks, timeout handling, optional headers, and typed provider errors.
- The default OpenRouter model remains `qwen/qwen3-14b` through `OpenRouterOptions.DefaultModel`.
- The request uses `MissionPlannerJsonSchema` with strict structured JSON response format.

Guard behavior:

- Required tests use fake HTTP handlers only; no network, API key, or provider credits are required.
- OpenRouter credit checks run before completion when enabled, and credit exhaustion blocks without falling back to another paid provider.
- Empty model content maps to `ProviderEmptyResponseException`.
- Malformed planner JSON, packet id mismatch, unsupported mission kind, unsupported authority source, and unsupported priority map to `ProviderMalformedResponseException`.
- Provider non-success and timeout paths map to typed provider errors.
- Error messages use fixed sanitized text and do not echo raw prompts, provider payloads, packet ids, mission kinds, keys, credentials, approval tokens, or sentinels.

Sanitized eval/live-smoke rows should be produced through `SanitizedMissionPlannerRuntimeEvalRunner`. Persisted rows contain only fixed codes, counts, provider/model/request metadata, response length, packet id, scenario label, and allowlisted mission kind.

Optional live smoke should remain skipped unless key presence and provider/credit health are safe. If OpenRouter returns empty content, malformed output, provider failure, or credit exhaustion, report the smoke as blocked and do not silently fall back to another provider.
