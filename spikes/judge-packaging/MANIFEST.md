# judge bundle — transfer manifest (Phase 2)

The judge is the first pillar to package because it depends on nothing and both
other pillars soft-depend on *it*. It is not a .NET package — it is a `.claude/`
bundle that drops into any repo (C#, Python, TS — the judge is topic-blind).

The bundle splits in two, and `verify.sh` proves the split holds:

## Core — travels verbatim, zero host coupling

| File | Role |
|---|---|
| `.claude/agents/judge.md` | The evaluator persona + the grading contract it must honor. Topic-blind: Subject / Context / Criteria in, one verdict per criterion out. |
| `.claude/workflows/judge.js` | The typed request/response schema and the `agent()` call. The contract every adapter conforms to. |

`verify.sh` asserts these two files contain **none** of the host repo's tokens
(`ABox`, `abox-server`, `tests/Central`, `ABox.slnx`, absolute paths) and that
`judge.js` still exports the full contract. A clean scan is the proof the core
lifts unchanged.

## Adapters — re-authored per host (the parameterization surface)

| File | Couples to |
|---|---|
| `.claude/commands/judge.md` | The host's test-file → Rulebook layout (`Tests/` folders, Rulebook path). |
| `.claude/commands/judge-rulebook.md` | The host's Rulebook template. |
| `.claude/commands/judge-authoring.md` | The host's authoring criteria. |
| `.claude/skills/test-rulebook/SKILL.md` | The full ABox test taxonomy (`ABox.<Owner>.Tests`, `tests/Central`, the marker). |

Adapters are *expected* to carry host tokens — they are the seam you re-point at
the new home's conventions. `verify.sh` reports each adapter's coupling count as
the re-authoring surface, not as a failure.

## The contract (what soft-depends on the judge)

```
request : { subject, context, files?, criteria: { id, description, howToCheck? }[] }
response: { generalFeedback, results: { criterionId, status: pass|fail|indeterminate, evidence }[] }
```

The doc-engine (via `docengine rubric`, which emits `criterion: rule` pairs) and
the test-harness (via the Rulebook adapters) both produce `criteria[]` for this
one contract. "Use the judge if it's there" = a host that has this bundle can
grade; one that doesn't still validates structurally. Soft, never a hard edge.

## What deliberately does NOT come along

No C#. A `Judging/` C# package existed here and was deleted (YAGNI, zero
consumers). The judge lives in the `.claude/` workflow layer precisely because a
schema cannot live in agent frontmatter — resurrecting a C# judge would re-add
mechanism nothing calls.
