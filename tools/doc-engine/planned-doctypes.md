# Planned doc types — ADR & Tests (Rulebook)

> **Plan only.** This designs two future doc types; it does not add them. Both are
> new **doc types**, not new **kinds** — so they need *zero* engine change: a doc
> type is `doctypes/<name>.yaml` plus whatever blocks it composes. Implementing
> either is the `add-a-block` + `add-an-instance` flow, gated by `check` + `validate`.

These are the first real consumers that justify the catalog beyond the spike's
`feature-plan` / `research`. Designing them is choosing **which blocks to reuse, which
to add, the `required` set, the front-matter attrs, and the rubric**.

## Existing blocks (reuse inventory)

`summary` · `context` · `scope` · `decision` (coll. "Decisions") · `phase` (coll.) ·
`verification` · `question` (coll.) · `expected-result` · `quotation` (coll.) ·
`analysis` · `outcome` · `open-question` (coll.).

Note `decision` is *settled choices carried into a plan, do-not-relitigate* — that is
**not** an ADR's decision (the one being made), so ADR needs its own block.

---

## 1. `adr` — Architecture Decision Record

Mirror the records already in `design/adr/` (Context → Decision → Consequences),
so an ADR becomes an enforceable, structured instance.

**Front matter (`attrs`)**

| attr | type | notes |
|---|---|---|
| `status` | enum `[proposed, accepted, superseded, deprecated]`, default `proposed` | the ADR lifecycle |
| `supersedes` / `superseded-by` | (deferred) | needs the cross-ref feature (NOTES punt #3) — omit at first |

The ADR number lives in the filename (`out/0001-foo.adr.md`), like the repo's ADRs.

**Blocks**

| block | reuse / NEW | required | role |
|---|---|---|---|
| `summary` | reuse | ✓ | one-line what + why |
| `context` | reuse | ✓ | the forces, constraints, the problem being decided |
| `decision-record` | **NEW** (singleton) | ✓ | the decision taken + its rationale (the heart) |
| `consequences` | **NEW** (singleton) | ✓ | what gets easier/harder; trade-offs accepted |
| `alternatives` | **NEW** (collection "Alternatives") | — | each option considered + why not chosen |
| `open-question` | reuse | — | genuine follow-ups |

`required: [summary, context, decision-record, consequences]`

**New blocks to author** (sketch of their `rubric`):
- `decision-record`: `decision-stated` (state the decision in one imperative sentence),
  `rationale` (why this, grounded in the context's forces), `closed` (it is decided,
  not a musing).
- `consequences`: `both-sides` (name what improves AND what it costs), `honest` (no
  pure-upside theatre), `follow-ons` (downstream work it forces, if any).
- `alternatives`: `option-and-why-not` (each: the option + the concrete reason
  rejected), `fair` (steelman, not strawman), `real` (actually-considered options).

**Doc-type `rubric` sketch:** `coverage`, `decision-singular` (exactly one decision
per ADR), `context-forces` (the context names real forces, not generic preamble),
`standalone`, `concrete`.

**Open question / lean:** ADR supersession is a cross-doc link → the deferred
cross-reference feature. *Lean:* ship `adr` without links first; add `supersedes` once
cross-refs land.

---

## 2. `tests` — a test-type Rulebook

Model the repo's **Rulebook** concept (`tests/**/Rulebook/`, `tests/README.md`): a
doc whose guarantees (Rules) are each proven by tests, policed by the ParityGuard.
An enforced `tests` doc type makes a Rulebook a validated artifact.

**Front matter (`attrs`)**

| attr | type | notes |
|---|---|---|
| `testType` | enum `[arch, structure, unit, e2e, wire, live, meta]` | which taxonomy bucket |

**Blocks**

| block | reuse / NEW | required | role |
|---|---|---|---|
| `summary` | reuse | ✓ | what this test type guarantees, overall |
| `scope` | reuse | — | what is in/out for this test type |
| `rule` | **NEW** (collection "Rules") | ✓ | one guarantee per member, with the test(s) that prove it |

`required: [summary, rule]`

**New block `rule`** (collection, group "Rules"):
- attrs: `proven-by` (string — the test name/fact id that enforces it).
- `rubric`: `guarantee-not-implementation` (states a behaviour guaranteed, not how
  it's coded), `one-per-member` (one rule each), `proven` (names the test that proves
  it), `stable-id` (the `<!-- id -->` is the durable handle the ParityGuard echoes).

**Doc-type `rubric` sketch:** `coverage` (every guarantee has a rule), `each-rule-proven`
(no rule without a proving test), `right-type` (rules match the declared `testType`),
`no-orphans`.

**⚠ Governance:** `tests/**/Rulebook/**` is a **protected, critical** path (owner
review; ParityGuard + policy-guard enforce it). A `tests` doc type that *generates or
validates* real Rulebooks would intersect that machinery — so it is **owner-gated**:
design it here, but landing/wiring it into the test tree is the owner's call, ideally
behind an ADR. Until then it can target `out/` as a standalone authoring aid.

---

## Net new work to implement both (when chosen)

- **New blocks:** `decision-record`, `consequences`, `alternatives` (ADR); `rule` (Tests).
- **New doctypes:** `doctypes/adr.yaml`, `doctypes/tests.yaml`.
- **Engine/kind change:** none — both are doc types over the existing `doctype` kind.
- **Then:** author one real instance of each as the proof (dogfood: this very file
  could become the first `adr`-or-`feature-plan` instance), `validate`, and — for
  Tests — get owner sign-off on touching the Rulebook surface.
