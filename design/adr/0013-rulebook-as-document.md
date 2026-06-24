---
status: proposed
date: 2026-06-24
supersedes:
amends:
---

# ADR 0013 — A test Rulebook is a doc-engine document; the test engine owns what spans artifacts

## Context

Two structured-artifact systems grew independently in this repo:

- The **test Rulebook stack** (`tests/`): the Harness (`Rule`, `ParityGuard`,
  `RulebookFormat`, `TestTypes`), a per-type `template.md` schema, and a `rules.md`
  instance whose `### ` headers are guarantees, each proven by a `[Rule]` fact.
- The **doc-engine** (`tools/doc-engine/`): a self-describing meta-model
  (`kind.schema` → kinds → blocks/doctypes → instances) with `check` / `validate`.

They are the *same shape* — a self-describing floor, a taxonomy of types, content
units, instances, and a validator. The doc-engine even independently rediscovered
the test stack's two best ideas: a self-describing floor (`kind.schema` ≙ `Meta`)
and a stable-id content unit carrying a "why" (a block's rubric ≙ a Rule + its Why).
That convergence raised the question: should they merge into one engine?

A naïve full merge collapses a real distinction. A **Test** is an *executable
validator*; a **Document** is a *validated artifact*. They are different kinds,
related by **direction**: tests validate documents, and a Rulebook is itself
*authored as* a document. Erasing that direction would push the doc-engine to grow
cross-artifact/relational constraints it does not otherwise need (to absorb
`ParityGuard`), and — worse — make the protected, zero-dependency enforcement spine
(ADR 0010, ADR 0012) depend on the doc-engine and its YamlDotNet, inverting the
dependency arrow the control surface relies on.

## Decision

**We will unify the *model*, not the *engine*, and split responsibility by whether
a guarantee is intra-document or spans artifacts.**

- **A test type's Rulebook is a doc-engine document.** A `rule` is a *block*; a
  Rulebook is the `rulebook` *doctype*; a per-type template is the `test-template`
  *doctype*. One model serves both plans and Rulebooks.
- **The doc-engine validates only generic, intra-document structure** — a Rulebook
  is a well-formed list of well-formed Rules (each with a required `Why`, an optional
  `Outcome`); a template carries criteria. *Intra-document* = checkable from one
  file against its doctype.
- **The test engine owns everything cross-artifact or test-specific** — `parity`
  (a Rule ↔ its proving `[Rule]` test) and per-type rule semantics (e.g. a behaviour
  Rule must carry an `Outcome`). These MUST NOT enter the doc-engine. *Cross-artifact*
  = correlates a document with code or other files.
- **The dependency arrow points out of the enforcement spine.** A test type MAY
  consume the doc-engine (test → tool; e.g. a `Docs` type that runs `docengine`), but
  the Harness mechanism (`Rule`, `ParityGuard`, `RulebookFormat`) MUST NOT depend on
  the doc-engine — consistent with ADR 0012's failure-mode budget (the load-bearing
  spine stays zero-dependency).

## Consequences

- The doc-engine gains the `rule` / `links` / `criterion` blocks and `rulebook` /
  `test-template` doctypes, plus a `labelmap` field-kind and a canonical field-order
  check — built authoring-side in Phase 1 (PR #89), no protected path touched. The
  "how" lives in `tools/doc-engine/planned-doctypes.md` §2 and the catalog, not here.
- A future **`Docs` test type** can bring document enforcement under `dotnet test` +
  `ParityGuard` by shelling out to `docengine check` / `validate` (mirroring
  `Live → claude`), with no Harness dependency on the engine — "no enforcement outside
  `tests/`" without inverting the arrow.
- Converting the real `tests/**/Rulebook/` files into instances (front-matter + ids)
  and splitting `RulebookFormat` (generic shape → engine-expressible; per-type +
  parity → Harness) is *enabled but deferred*; each is a protected change landing via
  an owner-reviewed PR.
- Cost accepted: two expressions of one model (`template.md` and the `test-template`
  doctype) coexist until/unless unified; a Meta test can later assert they agree.
- **Revisit if** the doc-engine ever needs cross-artifact or relational constraints
  to do its own job (that would mean this boundary is drawn wrong), or if maintaining
  `template.md` alongside `test-template` causes drift.

## Confirmation

- [det] `tests/Harness/**` takes no reference on `ABox.DocEngine` or YamlDotNet.
- [det] The doc-engine `rule` block declares no "which test proves this" field —
  parity stays test-side.
- [llm] The doc-engine validates only the intra-document structure of a Rulebook;
  the Rule ↔ test correspondence is enforced by the test engine, never the doc-engine.

## Alternatives considered

- **Full merge — one engine subsumes `ParityGuard`.** Rejected: makes the protected
  spine depend on the doc-engine, and forces the engine to grow relational /
  external-fact features purely to absorb a check the test engine already does well.
  Elegance, not need.
- **A doctype (or kind) per test type.** Rejected in favour of one normalized
  `rulebook` doctype + `test-template`: the seven types share one Rule shape (header +
  `Why` [+ `Outcome`]) and differ by *values*, not *structure*. The per-type
  requiredness that cannot be a single-doctype check (it is cross-instance) stays a
  test.
- **Keep the two stacks fully separate.** Rejected: forgoes one model and the
  "no enforcement outside `tests/`" win, when the two already use each other at two
  directed seams.

## More Information

- Phase 1 implementation: PR #89; `tools/doc-engine/planned-doctypes.md` §2;
  `tools/doc-engine/blocks/`, `tools/doc-engine/doctypes/`.
- Test taxonomy and Rulebook convention: `tests/README.md`, `tests/Harness/README.md`.
- Relates to: [`0010-agent-repo-controls.md`](0010-agent-repo-controls.md) (the
  protected enforcement surface), [`0012-dependency-budget-by-failure-mode.md`](0012-dependency-budget-by-failure-mode.md)
  (why the spine stays zero-dependency).
