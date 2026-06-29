# Contract Notes

## Internal State Versus Model DTOs

Internal state can be rich and nested. Model-facing DTOs must be flat and stage-specific.

Do not feed `ItineraryGraph` directly to a small model. Feed a compiled `StagePacket`.

## State Authority

Every state write has an authority source:

- user,
- trusted tool,
- strong model inference,
- small model draft,
- harness default,
- country-pack assumption.

`StateAuthorityPolicy` decides whether a proposed patch can auto-apply or requires confirmation.

## Claim Ledger

Every user-visible fact should trace to evidence:

- candidate ID,
- user statement,
- tool result,
- search summary,
- country-pack assumption,
- confirmed state patch.

The fidelity bake-off must fail outputs with unsupported claims.

