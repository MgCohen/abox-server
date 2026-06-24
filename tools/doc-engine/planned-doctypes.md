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

## 2. Rulebook + test-template — **BUILT (Phase 1)**

> Superseded the original single-`tests`-doctype sketch. A test type's Rulebook is
> modelled by **two** doctypes, not one, and `proven-by` is dropped — parity (Rule ↔
> proving test) is the **test engine's** job, never the doc-engine's.

**As built** (`doctypes/rulebook.yaml`, `doctypes/test-template.yaml`):

| doctype | blocks | required | front matter |
|---|---|---|---|
| `rulebook` | `links`, `rule` | both | `testType` enum (required) |
| `test-template` | `summary`, `criterion` | both | `testType` enum (required) |

**New blocks:** `rule` (collection "Rules") with explicit labels **Why** (required) +
**Outcome** (optional — the `→` tail of behaviour types); `links` (Template/Harness
pointers, both required); `criterion` (collection "Criteria").

**New engine capability:** a `labelmap` field-kind — a block declares required/optional
`**Label:**` bullets in its body, enforced at `validate` (used by `rule` and `links`).
Plus a canonical field-order check at `check` (`body` last), driven by each kind's
declared field order.

**Proof:** `out/arch.rulebook.md` (invariant), `out/wire.rulebook.md` (arrowed, uses
`Outcome`), `out/arch.test-template.md` — all `validate` PASS.

**Phase 2 (owner-gated, protected):** a `Docs` test type whose `[Rule]` facts shell out
to `docengine check` / `validate` (mirroring `Live → claude`), so doc enforcement runs
under `dotnet test` + ParityGuard with no Harness dependency on the engine; then
front-matter/ids on the real `tests/**/Rulebook/` files. Lands via the owner's PR + an ADR.

---

## Net new work — ADR (still planned)

- **New blocks:** `decision-record`, `consequences`, `alternatives`.
- **New doctype:** `doctypes/adr.yaml`.
- **Engine/kind change:** none — a doc type over the existing `doctype` kind.
- **Then:** author one real instance as the proof, `validate`. (Tests: §2 above is built.)
