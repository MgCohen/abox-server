# Live Rulebook

Each Rule is one **real-CLI guarantee** — a flow or agent driven against the *real* `claude`/`codex` CLI and
a real subscription. These prove the things only a live agent can: it edits the project on disk, it
self-resolves a question under autonomy, it resumes from a supplied answer. Each `###` header IS the Rule;
a `[Rule("<header>")]` test in `Live/Tests/` enforces it, and `ParityTests` keeps them in lockstep.
Cardinality is **1:N**.

**Gating:** Live tests are opt-in — they skip unless `RUN_LIVE=1` (Phase 4's `[LiveFact]`), so the default
`dotnet test` and CI never hit a real CLI. They are the manual confidence pass before a real run.

Template:
```markdown
### <agent/flow> <given a real prompt> → <real-world effect>
- Why: <the live behavior no scripted provider can prove>
```

> **Adoption is going-forward.** Starts with whatever Rules have been authored; existing skip-gated `[Fact]`
> smoke tests are backfilled opportunistically as they convert to `[LiveFact]`.

---
