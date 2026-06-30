# Live Mission Proposal Eval Notes

Live mission proposal eval rows are provider-local release artifacts for explaining whether a live provider returned a structured mission proposal that can be safely handed toward the harness proposal envelope.

Accepted rows may persist:

- safe eval case name and trusted packet id
- trusted session id
- fixed outcome code `live_mission_proposal_accepted`
- role enum
- allowlisted output kind `mission_proposal`
- allowlisted mission kind enum
- validated `/mission/...` field paths
- commitment kind enums
- pending confirmation reason-code enums
- field, commitment, and pending confirmation counts
- response content length
- accepted provider/model/request metadata

Rejected rows use fixed identifiers:

- row name `live_mission_proposal_rejected`
- packet id `live_mission_proposal_packet_redacted`

Rejected rows omit session id, role, output kind, mission kind, field paths, commitment kinds, pending reason codes, counts from provider results, response length, provider metadata, raw request body, raw provider response, raw completion, prompt text, API keys, credentials, approval tokens, hold references, booking/payment references, candidate display values, field values, commitment titles, locations, evidence ids, and raw exception text.

Fixed outcomes:

- `live_mission_proposal_accepted`
- `live_mission_proposal_disabled`
- `live_mission_proposal_key_missing`
- `live_mission_proposal_credit_exhausted`
- `live_mission_proposal_timeout`
- `live_mission_proposal_empty_content`
- `live_mission_proposal_malformed_json`
- `live_mission_proposal_schema_invalid`
- `live_mission_proposal_packet_mismatch`
- `live_mission_proposal_unsupported_value`
- `live_mission_proposal_unsafe_value_redacted`
- `live_mission_proposal_fallback_disabled`
- `live_mission_proposal_provider_unavailable`

Unsafe value handling is conservative. If raw model values contain prompt/provider/credential/token/hold/booking/payment/candidate-display sentinels, the eval row is rejected with `live_mission_proposal_unsafe_value_redacted` and provider metadata is omitted.

Optional live smoke records only fixed/sanitized status and must not fall back to a different paid provider.
