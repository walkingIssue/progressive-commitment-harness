# OpenRouter Qwen 14B Provider Notes

The sprint default hosted small-model target is OpenRouter model `qwen/qwen3-14b`.

`Pch.Providers.OpenRouter` loads the API key from `OPENROUTER_API_KEY` first, then from a configured key file path. Provider code must never log raw key material or include it in typed errors.

The OpenRouter client uses `/api/v1/chat/completions` for model completion and `/api/v1/credits` for credit checks. Completion requests default to `qwen/qwen3-14b` and support provider-local JSON schema response format options. The gateway intentionally keeps these DTOs local to the provider layer until core harness contracts are frozen.

Provider-dependent work must pause and report `BLOCKED` when OpenRouter credits are exhausted or unavailable. It must not silently fall back to another hosted provider, because fallback can change model behavior and sprint cost posture.

Required safeguards:

- strict request timeout
- typed credit-exhausted and provider-unavailable errors
- malformed JSON mapping to typed provider errors
- empty-content guard for completion responses
- deterministic mock providers for default tests

Manual live smoke is optional for this sprint. Default tests must run without network access or API keys.
