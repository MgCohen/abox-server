# Rulebook ⇄ doc-engine — status & remaining work

Home of record for the effort that unifies the test **Rulebook** model with the
standalone **doc-engine** (`tools/doc-engine/`). Decision rationale lives in
[ADR 0013](../design/adr/0013-rulebook-as-document.md); the doctype roadmap in
[`tools/doc-engine/planned-doctypes.md`](../tools/doc-engine/planned-doctypes.md).
Keep this short — it tracks state and the open forks, not the design.

## The idea (one line)

Unify the **model**, not the engine: the doc-engine validates *intra-document*
structure; the test engine owns *cross-artifact* parity + test-specific rule
semantics. The dependency arrow points **out of** the zero-dependency enforcement
spine — Harness never depends on the engine (ADR 0012 / 0013).

## Shipped (PR stack #89 → #90 → #92)

| PR | Branch | What landed |
|---|---|---|
| #89 | `claude/rulebook-docengine` | doc-engine Rulebook model: `rule`/`links`/`criterion` blocks, `rulebook`/`test-template` doctypes, `labelmap` field-kind, canonical field-order check, fence-aware label parser |
| #90 | `claude/rulebook-adr` | ADR 0013 (proposed) |
| #92 | `claude/docs-test-type` | `Docs` test type — runs `docengine check`/`validate` under `dotnet test` by shelling out (no Harness→engine reference); + the Fork 1/2 model changes below (block id optional, rulebook front-matter, `links` block retired) |

Review remediation (H1/L1/L2/M1/Nit) closed; H2 cut as over-mechanism (a config
switch + protected `ci.yml` edit buying nothing — the engine's output is
config-identical).

## Forks — settled

The three model decisions are made. Fork 1 + 2 landed as engine changes on
`claude/docs-test-type`; Fork 3 is a decision whose implementation *is* the
migration (below).

| Fork | Decision | Why |
|---|---|---|
| Block identity | `<!-- id -->` is an **optional** handle, not required, never derived. Nothing consumes it (no lookup, no link, no parity); the one render use was ordinal noise. | Parser keeps it for an agent that wants a stable cross-edit handle; most blocks omit it. |
| Front-matter | **Explicit** `docType`/`testType` per file; `Template`/`Harness` move into front-matter as required string attrs; the `links` block is deleted. | Engine reads front-matter as-is (zero new code) and stays uncoupled from the test-tree layout (ADR 0013). |
| `RulebookFormat` | **Delete** `RulebookFormat.cs` + `RulebookFormatTests.cs` — not "shrink." Drop the mechanical arrow-shape check. | All three Meta guards are intra-document structure the engine now owns. Parity was never in `RulebookFormat` — it lives in `ParityGuard`, untouched. Arrow-shape is a coarse proxy the judge's `right-type` rubric already covers semantically. |

## Remaining work

### 1. Merge the stack — owner's act
Protected paths + merge-to-`main` are walled off from the agent. Order, bottom-up:
`#89 → main`, then `#90` retargets → main, then `#92`. Flip ADR 0013
`proposed → accepted` when it lands.

### 2. Migrate the real rulebooks — DONE (on `claude/docs-test-type`, owner-gated merge)
Authored as one change touching protected paths (reviewed via PR; `ABOX_ALLOW_PROTECTED=1`
local override at commit). What landed, applying the settled forks:
- Each `tests/**/Rulebook/rules.md` reauthored to the engine shape — front-matter
  (`docType`/`testType`/`template`/`harness`) + `## Rules` of `### ` rules, **full
  headers kept** (so `[Rule("…")]` strings + parity keys didn't move).
- Each `template.md` reauthored as a `test-template` instance (`## Summary` + `## Criteria`
  of `### ` items); the schema-by-example example Rule dropped, Criteria kept for the judge.
- `docs` added to the `testType` enum in both doctypes; the Docs Rulebook migrated too.
- The `Docs` test (`Instances_validate`) now validates the real `tests/**/Rulebook/*.md`
  (via `RepoTree.RulebookFolders()`) on top of the `out/` samples — all 16 files PASS.
- `RulebookFormat.cs` + `RulebookFormatTests.cs` deleted; the three Meta Rules they proved
  (*Every Rule matches its type's template* / *…holds only rules* / *…carries judge criteria*)
  removed from the Meta Rulebook — their enforcement now lives in the doc-engine via Docs.
  `ParityGuard` unchanged; Meta parity holds (3 facts green).
- Stale shape docs updated: `tests/README.md`, `tests/Harness/README.md`, the `test-rulebook`
  skill (front-matter shape, removed-guard references, the Docs type added to the catalog).

### 3. Field-kind lookup — deferred, smallest
A documented catalog of the field-kinds (`string`/`bool`/`list`/`typespec`/
`attrs`/`strmap`/`fieldmap`/`labelmap`), so a schema author can discover what's
available. Independent of 1 and 2.

## Recommended sequence

Merge (1) first to clear protected-path churn → run the migration (2) as one
owner-reviewed PR → (3) any time; it's self-contained.
