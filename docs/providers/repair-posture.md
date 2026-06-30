# Provider Repair Posture

Sprint 018 adds provider-local repair posture suggestions for planning edits. This lane does not mutate harness state and does not automatically apply repairs. It only reports model-assisted or deterministic suggestion posture for trusted planning nodes.

Core DTOs:

- `RepairPosturePacket` carries trusted packet id, locale, sanitized node metadata, and optional context digest.
- `RepairPostureNode` carries trusted node id, node kind, node status, downstream dependency count, user-confirmation flag, availability/hold risk flag, and evidence id count.
- `RepairSuggestion` carries trusted node id plus fixed `RepairMode`, fixed `RepairReasonCode`, and affected-node count.
- `RepairPostureResult` carries provider/model/request metadata and response length for accepted source output.

Supported repair modes:

- `Keep`
- `ReplanDay`
- `ReselectCandidate`
- `AskUser`
- `BlockedReview`

Required tests use `MockRepairPostureSource` only. The mock derives deterministic modes from sanitized node metadata: preserved nodes keep, day/slot nodes replan day, selected/deferred candidates reselect, user-confirmation nodes ask the user, and availability/hold-risk nodes require blocked review.

The optional `ModelCompletionRepairPostureSource` is a guarded live runner shape over `IModelCompletionClient`. It is disabled by default and requires explicit live enablement plus key availability. OpenRouter, OpenAI, and Grok/xAI-compatible provider descriptors are present as disabled-by-default shapes; no provider fallback is attempted.

Live safeguards:

- explicit live enablement
- API key presence signal
- credit guard where available, especially OpenRouter
- strict timeout
- empty and malformed response blocking
- packet/node/mode validation by the evaluator
- fixed sanitized outcome codes
- no paid-provider fallback

Rows and docs must not include raw prompt text, provider payloads, raw completions, candidate display text, approval tokens, hold references, credentials, API keys, or exception text.
