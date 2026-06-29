# Mission Planner Bridge Eval Notes

Sprint 006 adds provider-local mission planner packet/result DTOs for prompt-to-structured-mission proposals. Required tests use deterministic mocks only and do not require network, API keys, or provider credits.

The planner bridge currently uses provider-local mirrors because the harness-owned mission-intake application/result contract is not on the published Sprint 006 base used by this lane. When that contract lands, provider results should map field-for-field into the harness-owned mission proposal shape rather than redefining authority rules in providers.

Sanitized eval rows persist only:

- packet id and scenario label
- expected and actual mission kind
- coarse outcome and error codes
- counts for user-stated fields, inferred fields, commitments, and pending confirmations
- provider/model/request metadata and response length

Sanitized eval rows must not persist raw prompt text, raw provider payloads, raw exception messages, field values, commitment descriptions, memory digests, credentials, or secret-like sentinels.

Optional live planner smoke can use OpenAI or OpenRouter only when the key is present and provider/credit checks pass. Empty content, malformed output, provider failure, or credit exhaustion must block the smoke and must not silently fall back to another paid provider.
