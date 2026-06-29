# Provider Fidelity Eval Artifacts

Sprint 013 adds provider-local fidelity eval DTOs and deterministic sources for Stage 6 bake-off artifacts. The provider surface compares three source kinds:

- `SmallModel`
- `StrongModel`
- `HarnessOnly`

The evaluator requires one source for each kind. Results are compared against trusted packet candidate ids, and accepted rows derive persisted candidate ids/categories from the packet. Provider output contributes only allowlisted decision enums and source-level counts after validation.

Mock behaviors cover:

- schema-valid comparison output
- schema-invalid output
- unsupported claim output
- missing or unexpected candidate id coverage
- timeout and provider-error paths
- fallback-required blocking

`fidelity_eval_fallback_required` is terminal. The evaluator does not continue to later sources after fallback is requested, which keeps optional paid-provider fallback from becoming implicit behavior.

Future live checks may target local Ollama, OpenRouter, or OpenAI-compatible sources only when explicitly enabled. They must use key/health/credit guards, strict timeouts, empty/malformed output blocking, sanitized fixed-code errors, and no fallback to another paid provider.
