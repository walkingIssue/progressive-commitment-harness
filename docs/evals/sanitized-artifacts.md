# Sanitized Provider Eval Artifacts

Provider eval/status artifacts are release-facing diagnostics. They must be useful for debugging fixed-code outcomes without becoming a persistence path for raw prompts, provider payloads, credentials, approval tokens, booking references, payment data, or user-derived sensitive values.

Persisted-safe fields are limited to:

- eval case name, trusted packet id, scenario labels, and expected values supplied by deterministic test cases
- fixed outcome, decode, intake, and error codes
- trusted packet ids such as slot ids, candidate ids, evidence ids, and plan ids when the lane explicitly validates them against packet-owned data
- counts for fields, constraints, commitments, candidates, evidence, holds, pending confirmations, and response content length
- provider/model/request metadata only on accepted rows or lane-documented safe paths

Rejected, blocked, malformed, unsupported, mismatch, and exception rows must use fixed outcome/error codes and must not echo raw provider result fields. They must omit raw provider result metadata when the result itself is untrusted or failed validation.

Persisted artifacts must not contain:

- raw prompt text or packet context digests
- raw model/provider response payloads
- raw exception messages
- approval tokens, hold references, payment data, credentials, or secret-like values
- candidate display values, candidate tags, commitment titles, constraint values, field values, memory digests, or provider-returned ids outside trusted packet maps
- test sentinels used to prove redaction behavior

The shared provider test fixture `SanitizedEvalArtifactAssert` serializes eval/status artifacts with the same JSON defaults used by lane tests and asserts that sensitive sentinels are absent. New provider eval lanes should add at least one rejected/error-path test through this helper before becoming release artifacts.

Fidelity eval artifacts follow this same policy for Stage 6 bake-off rows: accepted rows may persist trusted candidate ids plus allowlisted decision enums/counts, while rejected rows omit source and candidate details entirely.

Availability preview artifacts also follow this policy: quote-ready rows may persist trusted packet candidate ids/categories, allowlisted status enums, safe counts, and accepted provider metadata, while blocked/error rows omit provider metadata, quote references, fare refs, and result candidate details.

Model role guardrail artifacts follow the same policy: ready rows may persist trusted safe eval ids plus role/mode/availability enums and allowlisted status codes, while blocked/error rows use fixed row identifiers and omit raw config text, provider payloads, credentials, prompts, raw errors, and result metadata.

Live model role runner artifacts follow the same policy: accepted rows may persist trusted packet ids, role enums, configured model ids, allowlisted output kinds, allowlisted mood enums, response length, and accepted provider metadata. Runtime argument JSON is JSON-ignored and never persisted in eval rows. Rejected rows use fixed row identifiers and omit raw prompts, provider payloads, raw completion JSON, raw arguments, credentials, context digests, exception text, and untrusted mood prose.

Media source registry artifacts follow the same policy: accepted rows may persist trusted slot/candidate/category ids, sanitized media ids, sanitized source class/id/provider name, sanitized license class/name, sanitized attribution fields, dimensions, response length, and accepted provider metadata. Unsafe provider-origin metadata becomes fixed redacted placeholders or null author URLs. Rejected rows use fixed row identifiers and omit raw search queries, image URLs, thumbnail URLs, source URLs, alt text, provider payloads, API keys, credentials, raw exception text, candidate display values, and failed-response metadata.

Repair posture artifacts follow the same policy: accepted rows may persist trusted node ids/kinds, fixed repair mode and reason-code enums, node/suggestion/affected counts, response length, and accepted provider metadata. Rejected rows use fixed row identifiers and omit raw prompts, provider payloads, raw completions, candidate display text, approval tokens, hold references, credentials, API keys, raw exception text, and provider result metadata.

Live preflight artifacts follow the same policy: accepted rows may persist role enums, probe ids, configured model ids, provider kind enums, allowlisted structured-output status, counts, response length, and accepted provider metadata. Rejected rows use fixed row identifiers and omit raw request bodies, raw provider responses, raw completions, prompt text, API keys, credentials, approval tokens, hold references, candidate display values, raw exception text, and provider result metadata.

Live mission proposal artifacts follow the same policy: accepted rows may persist trusted packet/session ids, role enums, allowlisted output and mission kind enums, validated `/mission/...` field paths, commitment kind enums, pending reason-code enums, counts, response length, and accepted provider metadata. Runtime field values, commitment titles, locations, and evidence id collections are JSON-ignored. Rejected rows use fixed row identifiers and omit raw request bodies, raw provider responses, raw completions, prompt text, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, raw exception text, provider result metadata, and unsafe proposal values.

Live turn runner artifacts follow the same policy: accepted rows may persist trusted run/turn/packet ids, role/output enums, trusted choice candidate ids/categories, counts, duration, response length, and accepted provider metadata. Runtime question text, choice framing, option labels/rationales, summary text, mission field values, commitment titles/locations, and evidence id collections are JSON-ignored. Rejected rows use fixed row identifiers and omit raw request bodies, raw provider responses, raw completions, prompt text, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, raw exception text, provider result metadata, and unsafe turn output values.

Planner primitive runner artifacts follow the same policy: accepted rows may persist trusted run/turn ids, manifest id/version, output kind enum, manifest-owned primitive ids/kinds/counts, safe task ids/count, option count, repair flag, duration, response length, and accepted provider metadata. Runtime raw prompt text, submitted answer values, context summaries, model-authored primitive labels/help/prompts/defaults/options/task titles/summaries/state prose, request bodies, completions, provider payloads, and debug details are JSON-ignored or omitted. Sprint 024 accepted rows additionally prove HTML/form structure through primitive kinds, non-text primitive presence, task ids/count, option count, and output kind; generic text-only destination/date/pace outputs, missing task decomposition, untrusted field paths, and numeric controls with select-style option payloads are rejected with fixed codes before provider accepted status can be reported. Rejected rows use fixed row identifiers and omit primitive result fields, provider metadata, raw prompts, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, raw exception text, and unsafe primitive output values.

Optional live smoke paths remain skipped or blocked by default. A live smoke must be key and health/credit guarded where applicable, use strict timeout/error handling, and must not fall back to another paid provider when blocked.
