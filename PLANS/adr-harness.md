# ADR harness — structure, parse, grade, inform

A plan to give ADRs the same treatment `tests/` got: a **single owned shape**, a
**parser**, a **guard that fails the build on drift**, and **generated artifacts**
(index + digest) — so every ADR has predictable, machine-readable sections we can
*summarize, grade, and inform from*, while the heavy reading lives in linked design
docs. Today ADRs are prose with a house style nobody enforces; this turns the
[`design/adr/README.md`](../design/adr/README.md) **proposal** into running infra.

> This is a **harness plan**, not a rewrite of ADR practice. The *why* of ADRs, the
> house format, the Confirmation fitness-function idea, and the review pipeline are
> already settled in `design/adr/README.md` §1–§6. This plan adds the **enforcement
> surface** that README §4–§6 call for but leave unbuilt — and keeps it small, in
> governance, the way that README's "build incrementally (YAGNI)" closer asks.

---

## 1. The problem — flexible prose, no infra

ADRs are "too flexible, easy to mis-build, no infra." Concretely, in the current set:

- **Two formats coexist.** `0001`–`0007` open with a bold-label preamble
  (`**Status:** …`, `**Scope:** …`, `**Supersedes:** …`); `0008`–`0012` use YAML
  front-matter (`status` / `date` / `amends`). A digest script can't read both.
- **`Confirmation` is the exception, not the rule.** Only `0008` and `0009` carry a
  `## Confirmation` block — the very section README §6 says makes ADRs *evaluable*.
  The other ten have nothing to fire.
- **Section drift.** Alongside the four canonical sections every ADR shares
  (`Context` / `Decision` / `Consequences` / `Alternatives considered`), one-off
  headings have accreted: `Vocabulary`, `Open forks`, `Watch-point`,
  `Validation against references`, two differently-titled `Amendment` blocks. Each is
  reasonable in isolation; together they mean **no two ADRs are shaped alike**, so
  nothing downstream can parse them.
- **Supersession lives in prose.** `0003` is "Refined by 0004," `0006` is "Superseded
  in part by 0007" — stated in sentences, not in a field a tool can follow. The seam
  graph (README §4) can't be built from this.

The cost is exactly the test suite's pre-Rulebook cost: a guarantee that depends on
authors *remembering* a convention is a guarantee that silently rots.

---

## 2. The model we're copying — the test Rulebook harness

`tests/` already solved "make a doc-shaped artifact structured, parseable, and
enforced." We copy its anatomy rather than invent one. The mapping:

| Test harness ([`tests/Harness/README.md`](../tests/Harness/README.md)) | ADR harness (this plan) |
|---|---|
| `template.md` — the **one owned shape** (schema), single source of the format | `design/adr/template.md` — the ADR shape: front-matter keys + required sections + `Confirmation` bullet grammar |
| `rules.md` — the instances (`### ` Rules) | `design/adr/NNNN-*.md` — the instances |
| `RulebookFormat.cs` — parses headings + bullet-label schema | an **ADR parser**: front-matter, section headings, `Confirmation` tier tags, supersession fields |
| `ParityGuard.cs` — fails the build when shape and instances drift | an **ADR guard**: fails CI when an ADR violates the template (missing section, malformed front-matter, dangling supersedes) |
| Meta self-suite drives parity from outside the product | governance CI job + git hook drive the ADR guard from outside `design/adr/` |
| `authoring.md` + `/judge-authoring` — the **craft** layer (semantic quality) | `/judge` rubric — ADR **doc-quality** grading (alternatives honest? decision one imperative line? links not inlined?) |
| Generated nothing (rules *are* the index) | generated `INDEX.md` + `digest` (front-matter + Decision + Confirmation + links) |
| **Stability ratchet**: add freely, edit deliberately, reshape almost never | same contract on the ADR template (§8) |

The load-bearing lesson we carry over verbatim: **the template is the single owner of
the shape; you fill it, you never copy a sibling and edit** (that is precisely how the
test suite grew "two `Why:` stylings and six near-identical preambles," and how the
ADR set grew two formats and a dozen one-off sections).

---

## 3. The owned shape — `design/adr/template.md`

Promote the README §2 house format from prose-in-a-guide to a **real file the parser
reads** (just as `RulebookFormat.ReadSchema` reads each test type's `template.md`).
The schema, fixed:

**Front-matter** (YAML, the `0008`+ form wins — it is machine-readable; the
bold-label preamble is retired):

```yaml
---
status: accepted            # proposed | accepted | superseded-by-NNNN | deprecated
date: 2026-06-17            # ISO date
supersedes: [0003]          # ADR numbers this fully replaces (optional)
amends: [0009]              # ADRs / rules this refines, e.g. 0009 or R-SPINE-2 (optional)
---
```

**Required sections, in order** (the parser asserts presence + order; prose *within*
a section is free):

1. `## Context` — forces, why-now.
2. `## Decision` — the rule as imperative "We will …".
3. `## Consequences` — good/bad + a **revisit trigger**.
4. `## Confirmation` — the fitness-function checklist (grammar below). **Required**,
   not optional — this is the section that makes an ADR evaluable.
5. `## Alternatives considered`.
6. `## More Information` — links to the living "how."

**`Confirmation` grammar** (README §6, now machine-checked): each line is a bullet
tagged with exactly one tier —

```
- [det]    {deterministic, CI-checkable assertion}        (analyzer / test / grep)
- [llm]    {semantic assertion, subagent judges yes/no}
- [review] {irreducible human/agent judgement}
```

The parser enforces: ≥1 bullet, every bullet carries a known tier tag. The *quality*
of the assertion (atomic? self-contained?) is the judge's job, not the parser's —
same framework/craft split as tests.

**One-off sections** (`Vocabulary`, `Open forks`, `Amendment`…) are not banned but are
**not part of the core**: they may follow `More Information`, and the digest ignores
them. This keeps author freedom while guaranteeing the parseable core is uniform.

---

## 4. The harness pieces

Three small pieces, mirroring the test harness's parser → guard → (here) generator.

### 4a. The parser + guard — `governance/adr-check.sh`

A single checker, the ADR analog of
[`protected-paths-check.sh`](../governance/protected-paths-check.sh): zero-dependency,
POSIX-shell-or-tiny-tool (see §6 for the language decision), called by every enforcer.
It asserts, per ADR file, the **structural** invariants only:

1. Front-matter parses; `status` is a known value; `date` is ISO.
2. The six required sections are present and in order.
3. `Confirmation` has ≥1 bullet and every bullet is `[det]`/`[llm]`/`[review]`.
4. Filename `NNNN-kebab-title.md` agrees with the `# ADR NNNN — …` heading; numbers
   are unique and contiguous.
5. **Supersession integrity**: every `supersedes`/`amends`/`superseded-by-NNNN`
   reference resolves to a real ADR, and a `supersedes` is mirrored by the target's
   `status: superseded-by-NNNN` (the seam graph is consistent, not just present).

Failure output follows the harness rule — **active voice, name the file, say the fix**
(`ParityGuard`'s message is the model): *"ADR 0011 is missing `## Confirmation`. Add
the section with ≥1 `[det]`/`[llm]`/`[review]` bullet (see design/adr/template.md)."*

### 4b. The generated artifacts — `INDEX.md` + `digest`

**Generate, never hand-maintain** (README §5; a stale digest is worse than none). One
generator (`governance/adr-digest.sh` or a `make adr-digest` target) emits two things:

- **`design/adr/INDEX.md`** — the decision log: a table of `NNNN | title | status |
  supersedes/amends | one-line Decision`. This is the **inform** output.
- **`design/adr/.digest.md`** (or stdout) — the **summarize** output: per ADR, the
  *core* = front-matter + the one-line Decision + the `Confirmation` checklist +
  `More Information` links. This is the artifact the coherence agent and the
  conformance eval consume (README §5), so the heavy full-text reading is left to the
  source ADRs and their linked design docs.

Because the sections are now a guaranteed schema, extraction is deterministic — the
whole reason §3 makes the shape load-bearing.

### 4c. The grading layers — reuse the existing judge

No new grading engine. The repo already has a generic judge (`/judge`,
`PLANS/generic-judge.md`) and the per-ADR / coherence subagent pattern sketched in
README §4. We add **rubrics**, not infrastructure:

- **Doc-quality rubric** (the **grade** output) — an ADR-adapter rubric for `/judge`:
  decision is one imperative sentence; alternatives are honest; a revisit trigger
  exists; the "how" is *linked* not *inlined*; `Confirmation` bullets are atomic and
  self-contained. This is the craft layer — semantic, not mechanical, exactly like
  `authoring.md` is to the test parity guard.
- **Conformance eval** — fire each ADR's `[det]` Confirmation bullets in CI and route
  `[llm]` bullets to a subagent against the diff + that ADR's digest core (README §6).
- **Coherence pass** — one agent over `.digest.md` checks the seams (supersession
  chains, rules restated inconsistently). Only worth standing up once there are enough
  ADRs for seams to matter — it already pays off at 12.

These three answer three different questions and must stay separate — README §4's
table (Conformance / Doc-quality / Coherence) is the spec; this plan just wires it.

---

## 5. Enforcement wiring — it lives in governance

`design/adr/**` is **already a protected path** (tier `review`, in
[`governance/protected-paths`](../governance/protected-paths)). The harness slots into
the existing "one policy, many enforcers" frame
([`governance/README.md`](../governance/README.md)) with no new enforcement concept:

| Enforcer | Adds | Blocking? |
|---|---|---|
| CI `policy-guard` / a new `adr-guard` job | runs `adr-check.sh` over `design/adr/*.md` on every PR touching them | start advisory, promote to required once green |
| `pre-commit` / `pre-push` ([`.githooks/`](../.githooks)) | runs `adr-check.sh` locally; **fails if `INDEX.md` is stale** (regenerate-and-diff, like CODEOWNERS) | local catch, `ABOX_ALLOW_PROTECTED=1` override |
| CODEOWNERS review | unchanged — `design/adr/**` already requires owner review | merge gate of record |

The "regenerate and diff" trick is how `generate-codeowners.sh` already keeps a
generated file honest — we reuse it so `INDEX.md` can never drift from the source ADRs.

The **template and the guard are themselves enforcement surface** — add
`design/adr/template.md` and `governance/adr-*.sh` to `protected-paths` so the shape
can't be quietly weakened (the same reasoning that protects `tests/Harness/**`).

---

## 6. One decision to make — shell vs. a tiny tool

The governance enforcers are deliberately **zero-dependency POSIX shell** (ADR 0010 /
0012: the policy must parse without a YAML library). The ADR guard needs to parse YAML
front-matter and ordered markdown sections, which is more than `protected-paths`'s flat
`glob|owner|tier` lines.

**Recommendation: a small dedicated tool, not shell.** Front-matter + section-order +
supersession-graph parsing is past the point where shell stays readable, and the repo
*is* a .NET solution — a tiny `governance/adr-tool` (or a console project under `tools/`)
reusing the same parsing discipline as `RulebookFormat` is clearer and testable. It is
invoked *by* the shell enforcers, so the "many enforcers, one checker" shape holds.
The alternative — keep it pure shell to match the existing enforcers — is viable for
checks 1–4 but gets ugly for check 5 (the seam graph). **Flagging this as the one open
choice before P0**; everything else in the plan is language-agnostic.

> The conformance/coherence/judge layers (§4c) are agent-driven and reuse existing
> infra regardless of this choice — only the structural guard (§4a) is affected.

---

## 7. Build order — walking skeleton, YAGNI

Each phase is independently shippable and verified by running it, not just building
(the repo's per-layer bar). Front-load the skeleton; defer the agent layers.

- **P0 — Shape + structural guard, one ADR end-to-end.** Write `design/adr/template.md`
  (§3). Build `adr-check.sh` for checks 1–4. Bring **one** ADR (`0001`, per README §8.6)
  fully to the template incl. a real `Confirmation` block. Wire the advisory CI job +
  git hook. *Done-when:* the guard passes on `0001`, fails on a deliberately broken copy.
- **P1 — Generated index + digest.** `adr-digest.sh` emits `INDEX.md` + `.digest.md`;
  hook enforces freshness. *Done-when:* `INDEX.md` regenerates byte-identical in CI.
- **P2 — Backfill + supersession graph.** Migrate `0002`–`0007` to front-matter; add
  `Confirmation` to `0010`–`0012`; convert prose supersession ("Refined by 0004") to
  fields; turn on check 5. *Done-when:* guard is green across all 12 and promoted to a
  **required** check; the seam graph is consistent.
- **P3 — Doc-quality rubric.** Add the `/judge` ADR adapter (§4c). *Done-when:* it
  grades `0001` and surfaces at least one real finding.
- **P4 — Conformance eval.** Fire `[det]` bullets in CI; route `[llm]` to a subagent.
  Add only where a `[det]` check can't reach (README §6.5, YAGNI). *Done-when:* one
  ADR's checklist runs against the codebase.
- **P5 — Coherence pass.** Agent over `.digest.md` for cross-ADR seams. *Done-when:* it
  runs over the set and reports clean (or a real conflict).

Stop after P2 if the agent layers don't earn their keep yet — P0–P2 alone kill the
"flexible, no infra" problem. P3–P5 are the README pipeline and can trail.

---

## 8. Stability contract — the ADR template is a ratchet

Carry over the test harness's stability discipline verbatim, because the failure modes
are identical:

- **Adding an ADR — safe, encouraged.** A new record only adds decisions. Everyday move.
- **Adding an optional trailing section — safe.** It is outside the parseable core.
- **Editing the required-section set or front-matter keys — dangerous.** This is the
  schema every ADR and every downstream consumer (digest, judge, conformance) depends
  on; a change here reshapes all 12 at once and can make the digest silently drop the
  operative decision. Treat it as an architecture change to the ADR system, with the
  burden of proof to match — route it through a PR and owner review like any protected
  path. **Don't reshape casually.**

The summary, borrowed: **add ADRs liberally; change the schema deliberately; reshape
the harness almost never.**

---

## 9. Open questions

1. **Guard language** (§6) — tiny .NET tool (recommended) vs. pure shell. Decide before P0.
2. **Backfill depth** — do `0001`–`0007` get full `Confirmation` blocks during P2, or
   only front-matter + the four sections, with `Confirmation` added "when touched"
   (the repo's "applied going forward" stance)? Leaning: front-matter now, real
   `Confirmation` opportunistically — but the *guard* requires the section to exist, so
   a backfilled ADR needs at least a placeholder `[review]` bullet stating "not yet
   formalized." Decide the migration bar in P2.
3. **`amends` vs. ADR-vs-rule references** — `amends` mixes ADR numbers (`0009`) and
   rule ids (`R-SPINE-2`). Check 5's graph resolution must treat rule ids as a separate
   namespace (resolve against the PRD, not the ADR set) or skip them. Decide in P2.

---

## 10. Done-when (the harness exists)

- `design/adr/template.md` is the single owned shape; `adr-check.sh` (or the tool)
  fails the build on any structural drift, and is itself protected.
- All 12 ADRs pass the guard; `INDEX.md` and the digest are **generated** and
  freshness-enforced.
- The doc-quality rubric, conformance eval, and coherence pass exist as named,
  runnable jobs (even if introduced incrementally), each answering its own question.
- A new ADR now lands *with* its front-matter, sections, and `Confirmation` or the
  build fails — the ratchet is closed, exactly as it is for a new test.

## References

- [`design/adr/README.md`](../design/adr/README.md) — ADR practice, house format, the
  Confirmation/digest/pipeline **proposal** this plan implements (§2, §4–§6, §8).
- [`tests/Harness/README.md`](../tests/Harness/README.md) — the Rulebook harness this
  copies: template-owns-the-shape, parser, parity guard, the ratchet.
- [`governance/README.md`](../governance/README.md) — "one policy, many enforcers" and
  the regenerate-and-diff pattern for generated files.
- [`governance/protected-paths`](../governance/protected-paths) /
  [`protected-paths-check.sh`](../governance/protected-paths-check.sh) — the shared-checker
  shape `adr-check.sh` mirrors.
</content>
</invoke>
