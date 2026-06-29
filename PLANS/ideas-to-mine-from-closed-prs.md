# Ideas to mine from the closed governance / artifact PRs

Updated 2026-06-26. These PRs were **closed as obsolete** — they predate the doc-engine landing (#98) and
test co-location (#96), which re-derived the structured-doc / validation / parity spine differently, so the
*implementations* are dead and don't merge onto current `main`. But the **design ideas** are still good. This
note is the salvage list so we can revisit deliberately, on current `main`, rather than resurrecting stale
diffs.

> The throughline we **do** want: *deliberately organize where each kind of document lives* — give every
> document type a declared **home**, and enforce its shape/placement — now that the doc-engine gives us typed,
> validated documents.

## The big one — document homes & organization (#88, #84)

- **A declared `home` per document type.** #84's `artifact.yml` carried `home: <path>` (+ `purpose`, `family`,
  `parity`, `gate`); #88 made `governance/` an agent-first root with an explicit **engine/instance seam**
  (portable `harness/` vs per-repo `policy/ · decisions/ · plans/ · design/ · registry/`) and consolidated the
  two scattered `research/` dirs into one home. **Idea to replicate:** every doc type (ADR, plan, research,
  rulebook, …) declares where its instances live, and a guard keeps strays out — the doc-engine already knows
  the *type*; pair it with a *home*.
- **Tiering by role** (#88/#65): `critical` = machinery (page), `attention` = contract (label), `review` =
  routine (sign-off). A cleaner mental model than today's flat critical/review split.

## Selectability & conformance floors (#83, #86)

- **#83 — "every type declares a Purpose / when-to-use."** An agent must know *when* to reach for a doc type,
  not just that it exists. A required `## Purpose` (when-to-use line) makes a type *selectable*, not just
  discoverable. Cheap, high-value for agent-first.
- **#86 — the `## Shape` instance-conformance floor.** A template declares the `## ` headings every instance
  must carry; a Meta guard **hard-fails** if an instance drops one. Mechanical and always-blocking, **distinct
  from** the semantic `/judge` (which rates whether a section is *good*). Opt-in per type. This is the
  structural complement to the doc-engine's field checks.

## The artifact-registry abstraction (#84, #85, #86)

- A generic **"registered artifact type"** model: `register` (it exists, with a floor) + `template/criteria`
  (what good looks like) + `structural-validation` (it conforms). Tests were the first member; **Research** was
  the second (#86) — proving it generalizes beyond code-first to **nl-first** artifacts, with `gate = block`
  vs `advise`. The doc-engine's doctypes now cover much of "what a type is"; the open idea is the **registry +
  floor meta-guard** layer that says "this *kind of artifact* is registered and meets its floor."
- **#85 — definitions ↔ code parity across a move.** Keeping a doc's *definition* and its *proving code* in
  different trees, bridged by parity. We chose the opposite (co-locate) for tests, but the pattern is useful
  where the definition genuinely is ownerless.

## ADRs get the test treatment (#65, #66)

- Give **ADRs** the same enforcement tests got: a Structure-rulebook validator for **format drift**, a
  generated `INDEX`/`DIGEST` kept fresh by a regenerate-and-diff guard, and explicit
  `status: superseded-by-NNNN` / `amends` semantics for whole-record vs partial supersession. With ADRs as
  doc-engine instances (post-#98 they can be), this is largely "point the `Docs` type at `design/adr/` + add a
  freshness guard."

## How to revisit

Pick the **document-homes** idea first (highest leverage, matches "organize where each document goes"): on
current `main`, give each doc-engine doctype a declared home + a placement guard, reusing the `Docs` type and
`#99`'s front-matter discovery rather than a new registry. The conformance floors (#83/#86) fold in as small
Meta guards. The full governance/-as-root relocation (#88) is the heaviest and least certain — revisit only if
the multi-repo "stand this up elsewhere" need becomes real.

Closed PRs for reference: #83, #84, #85, #86, #88, #65, #66.
