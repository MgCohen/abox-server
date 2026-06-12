# Unit Rulebook

Each Rule is one behavioral guarantee about a single type or slice tested with local fakes. Each `###`
header IS the Rule, stated as the guarantee itself; the bullets carry its rationale. A `[Rule("<header>")]`
test in `Unit/Tests/` enforces it, and `ParityTests` fails the build if a Rule has no test or a test cites
no Rule. Cardinality is **1:N** — one guarantee may be realized by several case tests (several
`[Rule("<same header>")]` methods, or a future theory-backed Rule).

Template:
```markdown
### <Subject> <condition> → <expected result>
- Why: <the contract this protects>
```

> **Adoption is going-forward.** This Rulebook starts empty and grows as new behavioral tests land with
> their Rule. Existing `[Fact]` tests are backfilled opportunistically, never in one swept pass — so the
> set of Rules below is intentionally smaller than the set of tests in `Unit/Tests/`.

---
