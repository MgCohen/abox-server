# Wire Rulebook

## Purpose

Reach for Wire to pin one HTTP endpoint contract, proven with a real `HttpClient` against the Host.

Each Wire Rule is one endpoint contract, proven with a real `HttpClient` against the Host over
`WebApplicationFactory<Program>`, backed by a CLI-free flow. Add one Rule per endpoint behavior; enforce it
with a `[Rule]` fact in `Wire/Tests/`.

## Template

### <method> <route> <given> → <response contract>
- **Why:** <the routing/serialization/streaming guarantee this protects>

## Criteria

- **one_contract:** exactly one endpoint contract (method + route → result), not several bundled
- **observable:** asserts observable wire behavior (status, body shape, SSE stream), not an implementation detail
- **why_justifies:** the **Why:** gives the guarantee behind the endpoint, not a restatement of the header
