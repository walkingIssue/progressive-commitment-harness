# Mission Planner Bridge Eval Notes

Sprint 006 adds provider-local mission planner packet/result DTOs for prompt-to-structured-mission proposals. Required tests use deterministic mocks only and do not require network, API keys, or provider credits.

The planner bridge uses provider-local mirrors and does not reference harness assemblies. The mirror is intentionally shape-compatible with Shellby's harness-owned mission-intake contract so a later adapter can map without inventing semantic data.

Mapping alignment:

- `MissionPlannerResult` maps to `MissionIntakeProposal`.
- `MissionFieldProposal(FieldPath, Value, AuthoritySource, EvidenceIds)` maps to Shellby's `MissionFieldProposal(FieldPath, Value, AuthoritySource, EvidenceIds)`. Field paths should use canonical mission paths such as `/mission/purpose`, `/mission/destination_country`, `/mission/start_date`, or `/mission/date_window`.
- `MissionConstraintProposal(ConstraintId, Label, Value, AuthoritySource, IsHard, EvidenceIds)` maps to Shellby's `ConstraintProposal(ConstraintId, Label, Value, AuthoritySource, IsHard, EvidenceIds)`.
- `MissionCommitmentProposal(CommitmentId, CommitmentKind, Title, StartsAt, EndsAt, Location, IsIrreversible, RequiresSpend, CommitmentPriority, AuthoritySource, EvidenceIds)` maps to Shellby's `CommitmentProposal(CommitmentId, CommitmentKind, Title, StartsAt, EndsAt, Location, IsIrreversible, RequiresSpend, CommitmentPriority, AuthoritySource, EvidenceIds)`.
- Provider-local `MissionProposalSource` and `MissionCommitmentPriority` are mirrors only; integration should translate them to the canonical harness enums or value objects.

Sanitized eval rows persist only:

- packet id and scenario label
- expected and actual mission kind
- coarse outcome and error codes
- counts for user-stated fields, inferred fields, commitments, constraints, and pending confirmations
- provider/model/request metadata and response length

Sanitized eval rows must not persist raw prompt text, raw provider payloads, raw exception messages, field values, commitment titles, constraint values, memory digests, credentials, or secret-like sentinels.

Optional live planner smoke can use OpenAI or OpenRouter only when the key is present and provider/credit checks pass. Empty content, malformed output, provider failure, or credit exhaustion must block the smoke and must not silently fall back to another paid provider.
