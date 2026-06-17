# ADR harness — structure, parse, grade, inform

A plan to give ADRs an enforced shape: a **single owned template**, a **zero-dependency
validator** that fails the build on structural drift, and **generated artifacts** (index
+ digest), so every ADR has predictable, machine-readable sections we can *summarize,
grade, and inform from* — while the heavy reading stays in linked design docs. Today
ADRs are prose with a house style nobody enforces; this turns the
[`design/adr/README.md`](../design/adr/README.md) **proposal** into running infra.

> This is a **harness plan**, not a rewrite of ADR practice. The *why* of ADRs, the
> house format, the Confirmation fitness-function idea, and the review pipeline are
> already settled in `design/adr/README.md` §1–§6. This plan adds the **enforcement
> surface** that README §4–§6 call for but leave unbuilt — and keeps it small, in
> governance, the way that README's "build incrementally (YAGNI)" closer asks.
>
> **It does not rewrite history.** The schema is enforced **going forward**; existing
> records are migrated only for the two things genuinely broken today (front-matter
> format, prose-only supersession), never restructured or padded with placeholder
> content. Where a rule would force an edit to a frozen record, the record is exempt —
> see §3 and §7 P2.

---

## 1. The problem — flexible prose, no infra

ADRs are "too flexible, easy to mis-build, no infra." Concretely, in the current set:

- **Two formats coexist.** `0001`–`0007` open with a bold-label preamble
  (`**Status:** …`, `**Scope:** …`, `**Supersedes:** …`); `0008`–`0012` use YAML
  front-matter (`status` / `date` / `amends`). A digest script can't read both.
- **`Confirmation` is the exception, not the rule.** Only `0008` and `0009` carry a
  `## Confirmation` block — the very section README §6 says makes ADRs *evaluable*.
- **Section drift.** Alongside the four canonical sections every ADR shares
  (`Context` / `Decision` / `Consequences` / `Alternatives considered`), one-off
  headings have accreted: `Vocabulary` (`0003`), `Open forks` (`0005`), `Watch-point`
  (`0004`), `Validation against references` (`0011`), two differently-titled `Amendment`
  blocks (`0001`). Each is reasonable in isolation; together they mean **no two ADRs are
  shaped alike**, so nothing downstream can parse them.
- **Supersession lives in prose, and is usually *partial*.** `0003` is "Refined by
  0004," `0006` is "Superseded **in part** by 0007," `0001` has dated amendments —
  stated in sentences, not in a field a tool can follow, and rarely a clean
  whole-record replacement. The seam graph (README §4) can't be built from this, and
  any field that models supersession must admit *partial* scope, not a boolean.

The cost is the test suite's pre-Rulebook cost: a guarantee that depends on authors
*remembering* a convention is a guarantee that silently rots.

---

## 2. The model — borrow the test harness's discipline, not its machinery

`tests/` solved "make a doc-shaped artifact structured, parseable, and enforced," and we
reuse its **discipline** — one owned template that authors *fill* rather than copy-edit,
a checker that fails the build on drift, and a ratchet stability contract. We do **not**
reuse its *machinery*: the test `ParityGuard` does bidirectional cross-checking of
`### ` headers against `[Rule]` attributes discovered by **reflection over a compiled
assembly** — it catches drift between a spec and its code enforcers. ADRs are not code,
have no assembly, and have no second artifact to be "in parity" with. So the ADR side is
honestly a **schema validator** (instance-against-template linting), not a parity guard.
Calling it parity would borrow authority the mechanism hasn't earned. The mapping, stated
straight:

| Test harness ([`tests/Harness/README.md`](../tests/Harness/README.md)) | ADR harness (this plan) |
|---|---|
| `template.md` — the **one owned shape**, single source of the format | `design/adr/template.md` — the ADR shape: front-matter keys + required sections + `Confirmation` grammar |
| `rules.md` — the instances (`### ` Rules) | `design/adr/NNNN-*.md` — the instances |
| `RulebookFormat.cs` — parses headings + bullet schema | an **ADR parser**: front-matter, section headings, `Confirmation` tier tags, supersession refs |
| `ParityGuard.cs` — *bidirectional* spec↔enforcer parity via reflection | **not copied** — there is no second artifact; we do one-directional **schema validation** instead |
| Meta self-suite drives parity from outside | governance CI job + git hook run the validator from outside `design/adr/` |
| `authoring.md` + `/judge-authoring` — the **craft** layer | a **new** `/judge` ADR adapter — doc-quality grading (semantic, the part a validator provably can't reach) |
| Generated nothing (rules *are* the index) | generated index + digest (front-matter + Decision + Confirmation + links) |
| **Stability ratchet**: add freely, edit deliberately, reshape almost never | same contract on the ADR **template** (§8) |

The load-bearing lesson we carry over verbatim: **the template is the single owner of
the shape; you fill it, you never copy a sibling and edit** — precisely how the test
suite grew "two `Why:` stylings and six near-identical preambles," and how the ADR set
grew two formats and a dozen one-off sections.

---

## 3. The owned shape — `design/adr/template.md`

Promote the README §2 house format from prose-in-a-guide to a **real file the parser
reads**. Two constraints shape the schema beyond the README:

- **Front-matter is flat and shell-parsable** — `key: value`, one per line, no YAML
  lists or nested maps. This keeps the validator in the zero-dependency POSIX family the
  other governance enforcers live in (ADR 0010/0012: the policy must parse without a YAML
  library), and it is the form `0008`–`0012` already use.
- **Going-forward, not retroactive** — the required set below is enforced on ADRs dated
  on/after the harness lands; historical records are migrated narrowly (§7 P2) and
  exempted where a rule would otherwise rewrite them.

**Front-matter** (the `0008`+ form wins; the bold-label preamble is retired):

```yaml
---
status: accepted            # proposed | accepted | superseded | deprecated
date: 2026-06-17            # ISO date
supersedes: 0003            # ADR number(s) this replaces; free text, may scope: "0003 §1–§2"
amends: 0009 §1, R-SPINE-2  # ADRs/rules this refines; free text, comma-separated, may carry § refs
---
```

`supersedes`/`amends` are **free-text** because real supersession is partial and
section-scoped (`0008` already carries `amends: 0003 §1–§2, 0005 §1`). The validator does
not impose a list grammar on them; it extracts the `NNNN` tokens it finds and checks each
resolves to a real ADR (§4a check 5) — nothing more. Rule ids (`R-SPINE-2`) are a
separate namespace and are not resolved against the ADR set.

**Required sections, in order** (presence + order enforced going-forward; prose *within*
a section is free):

1. `## Context` — forces, why-now.
2. `## Decision` — the rule as imperative "We will …".
3. `## Consequences` — good/bad + a **revisit trigger**.
4. `## Alternatives considered`.
5. `## More Information` — links to the living "how."

**`## Confirmation` is optional**, exactly as README §2 has it — placed before
`More Information` when present. It is **never backfilled as a placeholder**: a
`[review] not yet formalized` bullet on a frozen record is dead boilerplate (the
no-comments culture's exact anti-pattern) and pollutes the digest. The fitness-function
bullet is added to a *new* ADR when its decision is genuinely checkable, or to an old one
*opportunistically when touched* — never retroactively en masse. When present, its grammar
(README §6) is machine-checked:

```
- [det]    {deterministic assertion} — names the analyzer/test/grep that fires it
- [llm]    {semantic assertion, subagent judges yes/no}
- [review] {irreducible human/agent judgement}
```

The validator enforces: ≥1 bullet, every bullet carries a known tier tag, and a `[det]`
bullet names its enforcing check (so P4 has something to wire). *Quality* of the
assertion (atomic? self-contained?) is the judge's job, not the validator's.

**One-off sections** (`Vocabulary`, `Open forks`, `Amendment`…) are allowed; for new
ADRs they follow `More Information` and the digest ignores them. **Historical records keep
theirs wherever they sit** — `0001`'s `Amendment` blocks precede `Context`, `0005`'s
`Open forks` sits mid-record; the order check applies only to going-forward ADRs, so these
are never restructured.

**Status exemptions.** A `status: superseded | deprecated` record is frozen-dead: the
validator checks only that its front-matter parses and its number/title agree — it does
**not** demand the section set or a Confirmation block on a record history has retired.

---

## 4. The harness pieces

Parser → validator → generator, mirroring the test harness's parser → guard → (here)
generator.

### 4a. The parser + validator — `governance/adr-check.sh`

A single checker, the ADR analog of
[`protected-paths-check.sh`](../governance/protected-paths-check.sh): **zero-dependency
POSIX shell**, called by every enforcer. Because the front-matter is flat (§3), shell
parses it without a YAML library — so the §6 "shell vs. tool" question is **resolved in
favor of shell**, and no compiled tool enters the git-hook hot path. It asserts the
**structural** invariants only:

1. Front-matter parses; `status` is a known value; `date` is ISO.
2. The five required sections are present and in order (going-forward ADRs only;
   superseded/deprecated records exempt — §3).
3. If a `## Confirmation` block exists, it has ≥1 bullet, every bullet is
   `[det]`/`[llm]`/`[review]`, and each `[det]` bullet names its enforcing check.
4. Filename `NNNN-kebab-title.md` agrees with the `# ADR NNNN — …` heading; numbers are
   unique. **Contiguity is not enforced** (two branches drafting `0013` is a merge-time
   rename, not a build failure); the validator flags only *duplicates*.
5. **Supersession reference integrity (weak):** every `NNNN` token extracted from a
   `supersedes`/`amends` value resolves to a real ADR file. No bidirectional `status`
   mirroring is required — partial supersession ("in part by 0007") can't be modeled as a
   boolean, so we check that references *point at something real*, not that history was
   rewritten to mirror them.

Failure output follows the harness rule — **active voice, name the file, say the fix**:
*"ADR 0013 is missing `## Consequences`. Add the section before `## Alternatives
considered` (see design/adr/template.md)."*

### 4b. The generated artifacts — index + digest, **outside the protected tree**

**Generate, never hand-maintain** (README §5; a stale digest is worse than none). One
generator (`governance/adr-digest.sh`) emits two things — both written **outside
`design/adr/**`** so regenerating them on every ADR add does not trip the protected-path
review gate (`design/adr/**` is owner-reviewed; a machine-written file there would force
owner review of generated output on every change):

- **`design/adr-index.md`** (sibling of the `adr/` dir, *not* inside it) — the decision
  log: `NNNN | title | status | supersedes/amends | one-line Decision`. The **inform**
  output.
- **`design/adr-digest.md`** — the **summarize** output: per ADR, the *core* =
  front-matter + the one-line Decision + the `Confirmation` checklist (if any) +
  `More Information` links. The artifact the coherence agent and conformance eval consume,
  so heavy full-text reading stays in the source ADRs.

The generator pins LF and a stable sort so output is **byte-identical across the
ubuntu + windows CI legs** — the freshness check (§5) diffs it, so cross-OS determinism
is a generator requirement, not an afterthought.

### 4c. The grading layers — reuse the judge *agent*, write one new adapter

No new grading engine. The repo has a generic judge **agent** (`.claude/agents/judge.md`)
and workflow (`PLANS/generic-judge.md`); the existing `/judge` command is a *test-rulebook
adapter*, so this is **one new command file**, not just "a rubric" — modest, but real
work, not free:

- **Doc-quality adapter** (the **grade** output) — a new `/judge`-family command feeding
  the judge agent an ADR + an ADR rubric: decision is one imperative sentence;
  alternatives are honest; a revisit trigger exists; the "how" is *linked* not *inlined*;
  any `Confirmation` bullets are atomic and self-contained. This is the craft layer — the
  semantic rot a structural validator **provably cannot** catch — and is the single
  highest-value piece after the format cleanup.
- **Conformance eval** — fire each ADR's `[det]` Confirmation bullets (each names its
  check, §3) in CI; route `[llm]` bullets to a subagent against the diff + that ADR's
  digest core (README §6). Add only where a `[det]` check can't reach (README §6.5).
- **Coherence pass** — one agent over `design/adr-digest.md` checks the seams
  (supersession chains, rules restated inconsistently).

These answer three different questions and stay separate — README §4's table
(Conformance / Doc-quality / Coherence) is the spec; this plan wires it.

---

## 5. Enforcement wiring — it lives in governance

`design/adr/**` is **already protected** (tier `review`, in
[`governance/protected-paths`](../governance/protected-paths)). The harness slots into the
existing "one policy, many enforcers" frame
([`governance/README.md`](../governance/README.md)):

| Enforcer | Adds | Blocking? |
|---|---|---|
| CI `policy-guard` (or a new `adr-guard` step in `ci.yml`) | runs `adr-check.sh` over `design/adr/*.md`; **regenerates the index/digest and diffs** (the freshness check — this is where CODEOWNERS' regenerate-and-diff actually lives, `ci.yml`, *not* the hooks) | advisory first; **owner promotes to required** (see below) |
| `pre-commit` / `pre-push` ([`.githooks/`](../.githooks)) | runs `adr-check.sh` locally (structural checks only — fast, shell, no regenerate step, matching the existing hooks) | local catch, `ABOX_ALLOW_PROTECTED=1` override |
| CODEOWNERS review | unchanged — `design/adr/**` already requires owner review | merge gate of record |

**Freshness lives in CI, not the hook.** The hooks today do *only* protected-path
checking; the CODEOWNERS regenerate-and-diff runs in the `policy-guard` CI job. The ADR
index/digest freshness check follows that precedent — in CI — so the local hooks stay
fast and shell-only.

**Promotion is the owner's call, not the agent's.** Making `adr-guard` a *required* check
is a branch-ruleset change on `main`, and the ruleset is owner-only (governance Phase 3:
the agent has no `administration` scope). The plan *recommends* promotion once green;
**MgCohen** makes it.

The **template and the validator are themselves enforcement surface** — add
`design/adr/template.md` and `governance/adr-*.sh` to `protected-paths` so the shape can't
be quietly weakened (the reasoning that protects `tests/Harness/**`). The generated
`adr-index.md`/`adr-digest.md` are **deliberately not protected** — they're machine
output, regenerated and diffed in CI.

---

## 6. Resolved: the validator is shell, the front-matter is flat

Earlier drafts left "shell vs. a compiled tool" open. It is **resolved in favor of
shell**, by constraining the front-matter to a flat `key: value` form (§3) and keeping
supersession a *weak* reference check (§4a check 5) rather than a parsed seam graph. That
keeps the validator in the zero-dependency POSIX family ADR 0010/0012 mandate and out of
the git-hook hot path — no `dotnet build`/run on commit. The richer seam-graph analysis
that *would* need a real parser is pushed to the **agent coherence pass** (§4c), which
reads the generated digest and needs no in-hook tooling. The agent-driven layers
(doc-quality, conformance, coherence) reuse existing infra regardless.

---

## 7. Build order — walking skeleton, YAGNI

Each phase is independently shippable and verified by running it, not just building.

- **P0 — Shape + validator, one ADR end-to-end.** Write `design/adr/template.md` (§3).
  Build `adr-check.sh` (checks 1–4; shell). Bring **one** ADR (`0001`, per README §8.6) to
  the template. Wire the advisory CI step + git hook. *Done-when:* the validator passes on
  `0001`, fails on a deliberately broken copy.
- **P1 — Generated index + digest.** `adr-digest.sh` emits `design/adr-index.md` +
  `design/adr-digest.md` (outside the protected tree); CI regenerates-and-diffs.
  *Done-when:* the index regenerates byte-identical on both CI legs.
- **P2 — Narrow migration + weak reference check.** Migrate `0002`–`0007` front-matter to
  the flat form; convert prose supersession ("Refined by 0004") to `supersedes`/`amends`
  fields (free-text, partial scope allowed). Turn on check 5. **No Confirmation backfill,
  no section restructuring** — historical one-off sections and superseded records stay as
  they are (§3 exemptions). *Done-when:* the validator is green across all 12, and the
  reference check resolves every `NNNN` token. Then **recommend the owner promote
  `adr-guard` to a required check.**
- **P3 — Doc-quality adapter.** Write the new `/judge` ADR command (§4c). *Done-when:* it
  grades `0001` and surfaces at least one real finding.
- **P4 — Conformance eval.** Fire `[det]` bullets (each names its check) in CI; route
  `[llm]` to a subagent. Add only where `[det]` can't reach. *Done-when:* one ADR's
  checklist runs against the codebase.
- **P5 — Coherence pass.** Agent over `adr-digest.md` for cross-ADR seams. *Done-when:* it
  runs and reports clean (or a real conflict).

P0–P2 kill the "two formats / prose supersession / no enforcement" problem. **P3 is the
highest-value phase** — the semantic grading a validator can't do — and should not be
treated as optional; P4–P5 are the README pipeline and can trail.

---

## 8. Stability contract — the ADR template is a ratchet

Carry over the test harness's stability discipline; the failure modes are identical:

- **Adding an ADR — safe, encouraged.** A new record only adds decisions.
- **Adding an optional trailing section — safe.** It is outside the parseable core.
- **Editing the required-section set or front-matter keys — dangerous.** This is the
  schema every ADR and every downstream consumer (index, digest, judge, conformance)
  depends on; a change reshapes all records at once and can make the digest silently drop
  the operative decision. Treat it as an architecture change to the ADR system — route it
  through a PR and owner review like any protected path. **Don't reshape casually.**

The summary: **add ADRs liberally; change the schema deliberately; reshape the harness
almost never.**

---

## 9. Open questions

1. **`status` vocabulary** — README §2 wrote `superseded-by-NNNN`; no ADR uses it, and
   partial supersession (the common case) can't ride on a single status value. This plan
   uses a plain `superseded` status plus a free-text `supersedes` field. Confirm that's the
   shape we want before P2 (it diverges from the README's literal text, intentionally).
2. **Migration bar for `0001`–`0007`** — front-matter + supersession fields only, no
   section restructuring, no Confirmation. Confirm that narrow scope is acceptable (it
   leaves the historical one-off sections in place, which the going-forward order check
   tolerates by exemption).
3. **Where the index/digest live** — this plan puts them at `design/adr-index.md` /
   `design/adr-digest.md` (siblings of `adr/`, outside the protected glob) to avoid
   review-gating generated output. Confirm that location over inside `design/adr/`.

---

## 10. Done-when (the harness exists)

- `design/adr/template.md` is the single owned shape; `adr-check.sh` (shell,
  zero-dependency) fails the build on structural drift, and is itself protected.
- All 12 ADRs pass the validator (historical records via the §3 exemptions, **not** via
  forced edits); `adr-index.md` and the digest are **generated**, live outside the
  protected tree, and are freshness-checked in CI; the owner has promoted `adr-guard` to
  required.
- The doc-quality adapter exists and runs on PR; the conformance and coherence layers
  exist as named, runnable jobs (introduced incrementally), each answering its own question.
- A new ADR lands *with* its front-matter and sections (and `Confirmation` when its
  decision is checkable) or the build fails — the ratchet is closed for new records,
  without rewriting old ones.

## References

- [`design/adr/README.md`](../design/adr/README.md) — ADR practice, house format, the
  Confirmation/digest/pipeline **proposal** this plan implements (§2, §4–§6, §8).
- [`tests/Harness/README.md`](../tests/Harness/README.md) — the discipline this borrows
  (template-owns-the-shape, fail-on-drift, the ratchet) and the parity machinery it
  deliberately does **not** copy.
- [`governance/README.md`](../governance/README.md) — "one policy, many enforcers" and the
  CI regenerate-and-diff pattern for generated files.
- [`governance/protected-paths`](../governance/protected-paths) /
  [`protected-paths-check.sh`](../governance/protected-paths-check.sh) — the shared
  zero-dependency-shell checker `adr-check.sh` mirrors.
</content>
