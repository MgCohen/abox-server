# E2E Rulebook

Each Rule is one flow guarantee — a whole flow driven end to end through the real composition (real Steps, real
Flow engine, real snapshot stream) with only the agent's mouth scripted or a real local tool behind it. The
in-process `ScriptedProvider` is the default driver; the real CLI is the Live type. Convention, parity
discipline, and the Rule shape live in [`../../../Harness/README.md`](../../../Harness/README.md) and `template.md`.

---

### claude-ping with a scripted reply → implementer reaches Completed
- **Why:** proves the API-down backbone — real composition, Flow engine, snapshot stream, resolver wiring —
  carries an agent flow to a Completed terminal with only the provider's mouth scripted. The deterministic
  counterpart to the live `claude-ping` smoke.

### chore on a dirty tree → committed and pushed, working copy clean
- **Why:** the non-agent flow path end to end — `GitChoreFlow` stages the dirty tree, commits with the given
  subject, and pushes to the remote, leaving the working copy clean and the remote head at that commit.
