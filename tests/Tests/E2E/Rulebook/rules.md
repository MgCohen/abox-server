# E2E Rulebook

Each Rule is one **flow guarantee** — a whole flow driven end to end through the real composition (real
Steps, real Flow engine, real snapshot stream), with only the agent's mouth scripted or a real local tool
(git) behind it. Each `###` header IS the Rule; a `[Rule("<header>")]` test in `E2E/Tests/` enforces it,
and `ParityTests` keeps them in lockstep. Cardinality is **1:N**.

Template:
```markdown
### <flow> <given some input> → <observable end state>
- Why: <the user-visible behavior this proves>
```

The in-process `ScriptedProvider` backbone is the default driver here — deterministic and CI-safe, never a
real agent CLI (that is the Live type). Every test in `E2E/Tests/` carries a `[Rule]`; the guard rejects a
bare flow test.

---

### claude-ping drives the implementer to completion with the scripted reply
- Why: proves the API-down backbone — real composition, real Flow engine, real snapshot stream, real
  resolver wiring — carries an agent flow to a Completed terminal with only the provider's mouth scripted.
  This is the deterministic counterpart to the live `claude-ping` smoke.

### chore commits the working tree and pushes it to the remote
- Why: the non-agent flow path end to end — `GitChoreFlow` stages the dirty tree, commits with the given
  subject, and pushes to the remote, leaving the working copy clean and the remote head at that commit.
