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

> **Adoption is going-forward.** Starts with whatever flow Rules have been authored; existing `[Fact]`
> flow tests are backfilled opportunistically. The in-process `ScriptedProvider` backbone (Phase 3) is the
> default driver here — deterministic and CI-safe, never a real agent CLI (that is the Live type).

---
