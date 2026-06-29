# Mission Planner Runtime Provider Notes

Sprint 007 adds a provider-local mission planner runtime surface for handing `MissionPlannerPacket` inputs to an `IMissionPlannerClient` and returning a provider-local `MissionPlannerResult` through a sanitized runtime handoff.

Runtime shape:

- `IMissionPlannerClient.PlanAsync` remains the provider client contract and returns `MissionPlannerResult`.
- `MissionPlannerRuntimeClient.RunAsync` invokes an `IMissionPlannerClient` and passes the result through `MissionPlannerRuntimeBridge`.
- `MissionPlannerRuntimeBridge` creates a `ProviderRuntimeMissionIntakeProposal` for in-memory adapter handoff only.
- The serialized `MissionPlannerRuntimeHandoffResult` contains only provider-local proposal metadata: proposal id, packet id, mission kind, field paths, commitment ids/kinds, constraint ids, counts, provider/model/request metadata, and response length.

The runtime bridge does not reference `Pch.Harness`. Until Shellby's adapter contract is consumed directly by the harness layer, this provider-local mirror assumes a future adapter will map the in-memory `MissionPlannerResult` into `MissionIntakeProposal` and its `MissionFieldProposal`, `ConstraintProposal`, and `CommitmentProposal` children.

Sanitized persisted outputs must not include raw prompt text, raw provider payloads, field values, commitment titles, constraint values, memory digests, raw exception text, credentials, approval tokens, or secret-like sentinels. Runtime eval rows persist only fixed decode/intake/error codes, counts, provider/model/request metadata, response length, packet id, scenario label, and mission kind.

Optional live smoke may be added for OpenAI or OpenRouter-compatible planners only when it is clearly useful and guarded by:

- key presence checks that never print or persist key material
- OpenRouter credit/provider-health checks when OpenRouter is used
- strict request timeout
- empty-content and malformed-output blocking
- typed provider/credit failure handling
- no silent fallback to another paid provider

Required tests must continue to use deterministic mocks and must not require network, API keys, or provider credits.
