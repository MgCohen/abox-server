# ADR harness — structure, parse, grade, inform

A plan to give ADRs an enforced shape: a **single owned template**, a **validator that
fails the build on structural drift** (expressed as Rules in the xUnit **Structure**
rulebook, not a parallel shell enforcer), and **generated artifacts** (index + digest), so
every ADR has predictable, machine-readable sections we can *summarize, grade, and inform
from* — while the heavy reading stays in linked design docs. Today ADRs are prose with a
house style nobody enforces; this turns the [`design/adr/README.md`](../design/adr/README.md)
**proposal** into running infra.

> This is a **harness plan**, not a rewrite of ADR *practice*. The *why* of ADRs, the
> house format, the Confirmation fitness-function idea, and the review pipeline are already
> settled in `design/adr/README.md` §1–§6. This plan adds the **enforcement surface** that
> README §4–§6 call for but leave unbuilt.
>
> **It does, deliberately, rewrite the *shape* of history.** Unlike earlier drafts, this
> plan migrates **all twelve** records to one template — full section set *and* a
> `Confirmation` block on every record (Decision D4). That is a conscious override of the
> README §1 immutability counsel ("you do not rewrite history") and of the
> `protected-paths` reason on `design/adr/**` ("frozen history, not living docs"): the owner
> is trading strict historical immutability for a uniformly evaluable set. We hold the line
> where it costs nothing — the *decision text* and dated amendments of each record are
> preserved **verbatim**; only the surrounding shape is normalized, and every backfilled
> `Confirmation` bullet must be a **genuine** fitness assertion, never placeholder
> boilerplate (§3, §7 P2).

---

## 0. Decisions that shape this plan

Five choices were settled before this revision; they reverse three positions earlier
drafts had resolved the other way, so they are recorded here with rationale rather than
buried as "open questions."

| # | Decision | Reverses | Why |
|---|---|---|---|
| **D1** | **`status: superseded-by-NNNN`** is the status for a *whole-record* replacement (README's literal enum). *Partial* refinement — the only kind in the current set — keeps the old record at `status: accepted` and records the edge on the **superseding** ADR's `amends`. | Earlier draft's plain `superseded` + free-text `supersedes` field (old §9 Q1). | Restores the documented house enum and gives an at-a-glance "retired" signal, without forcing partially-refined records to lie about being wholly dead. |
| **D2** | The validator's scope is **format drift only** — "does the ADR match the template?" Code-vs-ADR (*semantic*) drift is out of scope; it is caught decision-by-decision as **Arch `[Rule]`s** and by the **coherence pass**, not by this validator. | (clarification) | A document-local checker can decide format from the ADR files alone; it provably cannot decide whether `/src` obeys a decision. Conflating the two is the speculative over-reach the YAGNI rule warns against. |
| **D3** | The validator is the **xUnit Structure rulebook** — ADR-format checks are `[Rule]`s under `tests/Tests/Structure`, paired with `### ` Rulebook headers, scanning `design/adr/*.md` on disk (exactly how the placement guards scan `src/`). It runs at `dotnet test` / CI, **not** in the git-hook hot path. | Earlier draft's zero-dependency shell `governance/adr-check.sh` (old §2/§4a/§6). | Folds ADR well-formedness into the existing test taxonomy and its parity machinery instead of standing up a second, parallel enforcer. It does **not** violate ADR 0010/0012 (those govern *hook-time governance enforcers*; this is a test). Trade: a malformed ADR is caught at `dotnet test`/CI, not at local commit — acceptable, since `design/adr/**` is CODEOWNERS-gated and ADRs are rarely authored. |
| **D4** | **Full migration**: all of `0001`–`0007` are restructured to the template (front-matter + the five ordered sections) **and** given a `Confirmation` block. | Earlier draft's narrow "front-matter only, no restructuring, no Confirmation" migration (old §3/§7 P2/§8). | Buys a uniformly shaped, uniformly *evaluable* set — every record feeds the digest and the conformance eval identically. Cost (rewriting frozen records, the risk of fabricated checks) is bounded by preserving decision text verbatim and writing only genuine `Confirmation` assertions. |
| **D5** | Generated index + digest live **inside `design/adr/`** (`design/adr/INDEX.md`, `design/adr/DIGEST.md`). | Earlier draft's `design/adr-*.md` siblings *outside* the protected tree (old §4b/§9 Q3). | Co-locates the decision log with the records. `protected-paths` is a flat allow-list with **no exclusion glob**, so the generated files cannot be carved out — but they don't need to be: an ADR change *already* rides an owner-reviewed PR (`design/adr/**` is `review`-tier), so the regenerated files travel in that same PR at no extra review cost. CI's regenerate-and-diff enforces freshness. |

---

## 1. The problem — flexible prose, no infra

ADRs are "too flexible, easy to mis-build, no infra." Concretely, in the current set:

- **Two formats coexist.** `0001`–`0007` open with a bold-label preamble
  (`**Status:** …`, `**Scope:** …`, `**Supersedes:** …`); `0008`–`0012` use YAML
  front-matter (`status` / `date` / `amends`). A digest script can't read both.
- **`Confirmation` is the exception, not the rule.** Only `0008` and `0009` carry a
  `## Confirmation` block — the very section README §6 says makes ADRs *evaluable*.
- **Section drift.** Alongside the four canonical sections every ADR shares
  (`Context` / `Decision` / `Consequences` / `Alternatives considered`), one-off headings
  have accreted: `Vocabulary` (`0003`), `Open forks` (`0005`), `Watch-point` (`0004`),
  `Validation against references` (`0011`), two differently-titled `Amendment` blocks
  (`0001`). Each is reasonable in isolation; together they mean **no two ADRs are shaped
  alike**, so nothing downstream can parse them.
- **Supersession lives in prose, and is usually *partial*.** `0003` is "Refined by 0004,"
  `0006` is "Superseded **in part** by 0007," `0001` has dated amendments — stated in
  sentences, not in a field a tool can follow, and rarely a clean whole-record replacement.
  The seam graph (README §4) can't be built from this, and the supersession model must
  admit *partial* scope, not a boolean (this is exactly what D1 settles).

The cost is the test suite's pre-Rulebook cost: a guarantee that depends on authors
*remembering* a convention is a guarantee that silently rots.

---

## 2. The model — this *is* a Structure rulebook, used one-directionally

Earlier drafts argued the ADR checker should stand apart from the test harness ("ADRs are
not code, have no assembly, no second artifact to be in parity with") and live as a shell
script. D3 reverses that: ADR-format checks join the **Structure** test type. The fit is
exact — Structure tests already **read repo files on disk** and assert facts about them
(`HomeFolders` scans `src/` for stray folders; `SourceTree` scans for stray build output).
An ADR validator is the same shape: scan `design/adr/*.md`, assert each matches the
template.

One honest caveat, carried over from the earlier draft because it is still true: this is
**one-directional schema linting**, not the *bidirectional* spec↔code parity the test
`ParityGuard` performs between `### ` Rules and reflected `[Rule]` attributes. We borrow
the Structure type's discipline — a `### ` Rulebook header per guarantee, a backing
`[Rule]` fact, the Meta parity check that neither outruns the other — but the thing each
ADR Rule *asserts* is "every `design/adr/*.md` conforms to `template.md`," not "this spec
and that enforcer agree." The parity that the harness guarantees is between the **ADR
Rulebook headers and the ADR `[Rule]` facts**, exactly as for every other Structure rule.

| Test harness ([`tests/Harness/README.md`](../tests/Harness/README.md)) | ADR harness (this plan) |
|---|---|
| `template.md` — the one owned shape | `design/adr/template.md` — the ADR shape: front-matter keys + required sections + `Confirmation` grammar |
| `Rulebook/rules.md` — `### ` Rules (the guarantees) | new `### ` Rules in `tests/Tests/Structure/Rulebook/rules.md`, one per ADR-format invariant (§4a) |
| `[Rule]` facts in `StructureTests.cs` | new `[Rule]` facts that parse `design/adr/*.md` and assert conformance |
| `RulebookFormat.cs` — parses headings + bullet schema | an **ADR parser** in `tests/Tests/Structure/Support/` (front-matter, sections, `Confirmation` tiers, supersession refs) — the analog of `RulebookFormat`/`SourceTree` |
| `ParityGuard.cs` — bidirectional spec↔enforcer parity | reused as-is for **header↔`[Rule]` parity** on the ADR rules; the rules' *content* is one-directional doc linting |
| Meta self-suite drives parity from outside | unchanged — Meta already polices the whole Structure type, ADR rules included |
| Generated nothing (rules *are* the index) | generated `INDEX.md` + `DIGEST.md`, kept fresh by a Structure freshness Rule (§4b) |
| **Stability ratchet**: add freely, edit deliberately, reshape almost never | same contract on the ADR **template** (§8) |

The load-bearing lesson we carry over verbatim: **the template is the single owner of the
shape; you fill it, you never copy a sibling and edit** — precisely how the test suite grew
"two `Why:` stylings and six near-identical preambles," and how the ADR set grew two
formats and a dozen one-off sections.

> **`tests/Tests/Structure/**` is itself `critical`-protected.** Adding these Rules is a
> deliberate, owner-reviewed PR, authored with the **`test-rulebook`** skill so each Rule
> lands paired with its Rulebook header and the parity guard stays green.

---

## 3. The owned shape — `design/adr/template.md`

Promote the README §2 house format from prose-in-a-guide to a **real file the parser
reads**. Two constraints shape the schema beyond the README:

- **Front-matter is flat** — `key: value`, one per line, no YAML lists or nested maps. With
  the parser now in C# (D3), this is no longer *forced* by a shell constraint; it is kept
  for **simplicity and continuity** — it is the form `0008`–`0012` already use, and the
  parser stays trivial.
- **Applies to the whole set.** Because D4 migrates all twelve, the required section set and
  `Confirmation` block are enforced **uniformly across `0001`–`0012`** — there is no
  "going-forward only" exemption for the legacy records. The only status-based relaxation is
  for records that are *themselves* wholly retired (below).

**Front-matter** (the `0008`+ form wins; the bold-label preamble is retired):

```yaml
---
status: accepted                 # proposed | accepted | superseded-by-NNNN | deprecated
date: 2026-06-17                 # ISO date
supersedes: 0003                 # ADR number(s) this WHOLLY replaces; free text, may scope
amends: 0009 §1, R-SPINE-2       # ADRs/rules this refines; free text, comma-separated, may carry § refs
---
```

**Supersession model (D1).** `status: superseded-by-NNNN` marks a record as **wholly
replaced** by ADR `NNNN`; the validator checks `NNNN` resolves to a real ADR. *Partial*
refinement — `0003` "Refined by 0004 §1", `0006` "superseded in part by 0007" — does **not**
flip status: the refined record stays `accepted`, and the partial edge is recorded on the
**superseding** record's `amends` (`0004` carries `amends: 0003 §1`; `0007` carries
`amends: 0006`). None of the current twelve are *wholly* superseded, so the
`superseded-by-NNNN` value is defined in the schema and applies the first time there is a
clean full replacement.

`supersedes`/`amends` are **free-text** because real supersession is partial and
section-scoped. The validator does not impose a list grammar; it extracts the `NNNN` tokens
it finds and checks each resolves to a real ADR (§4a check 5). Rule ids (`R-SPINE-2`) are a
separate namespace and are not resolved against the ADR set.

**Required sections, in order** (presence + order enforced across all twelve; prose
*within* a section is free):

1. `## Context` — forces, why-now.
2. `## Decision` — the rule as imperative "We will …".
3. `## Consequences` — good/bad + a **revisit trigger**.
4. `## Alternatives considered`.
5. `## Confirmation` — the fitness-function checklist (now **required**, see below).
6. `## More Information` — links to the living "how."

**`## Confirmation` is required on every record (D4).** This strengthens README §2 (which
made it optional): because the migration adds it to all twelve, requiring it going forward
keeps the set uniformly evaluable rather than letting it decay back to "the exception." Its
grammar (README §6) is machine-checked:

```
- [det]    {deterministic assertion} — names the analyzer/test/grep that fires it
- [llm]    {semantic assertion, subagent judges yes/no}
- [review] {irreducible human/agent judgement}
```

The validator enforces: ≥1 bullet, every bullet carries a known tier tag, and a `[det]`
bullet names its enforcing check (so P4 has something to wire). *Quality* of the assertion
(atomic? self-contained?) is the judge's job (§4c), not the validator's.

> **The anti-boilerplate guard rail.** Requiring `Confirmation` everywhere is the part of
> D4 that risks "documentation that lies." The mitigation is a content rule the **judge**
> enforces, not the validator: a `Confirmation` bullet must be a *real* fitness assertion.
> Most legacy decisions are structurally checkable and earn an honest `[det]`/`[llm]`
> bullet (README §6 gives `0001`'s); the few genuinely irreducible ones take a *specific*
> `[review]` assertion — never a `[review] not yet formalized` placeholder, which the judge
> flags as boilerplate.

**One-off sections** (`Vocabulary`, `Open forks`, `Amendment`…) remain allowed; they follow
`More Information` and the digest ignores them. During migration (D4) a legacy record's
one-off content is **kept** — relocated after `More Information` if needed, never deleted —
and its original *decision text and dated amendments are preserved verbatim*.

**Status exemption.** A `status: superseded-by-NNNN | deprecated` record is frozen-dead:
the validator checks only that its front-matter parses and its number/title agree — it does
**not** demand the full section set or a `Confirmation` block on a record history has
retired. (No current record is in this state; the exemption is for the future.)

---

## 4. The harness pieces

Parser → validator (Structure Rules) → generator, mirroring the test harness's parser →
guard → (here) generator — all inside the **Structure** test project.

### 4a. The parser + validator — Structure `[Rule]`s over `design/adr/*.md`

An **ADR parser** lands in `tests/Tests/Structure/Support/` (the home of `SourceTree`,
`HomeFolders`): it reads a `design/adr/*.md` file into a small record — front-matter map,
ordered section headings, `Confirmation` bullets with their tier tags, and the `NNNN`
tokens in `supersedes`/`amends`. The validator is then a set of Structure `[Rule]` facts,
each paired with a `### ` Rulebook header, asserting the **structural** invariants:

1. Front-matter parses; `status` is a known value (`proposed | accepted |
   superseded-by-NNNN | deprecated`); `date` is ISO.
2. The required sections are present and in order (all twelve; `superseded-by-NNNN`/
   `deprecated` records exempt per §3).
3. Every record has a `## Confirmation` block with ≥1 bullet; every bullet is
   `[det]`/`[llm]`/`[review]`; each `[det]` bullet names its enforcing check.
4. Filename `NNNN-kebab-title.md` agrees with the `# ADR NNNN — …` heading; numbers are
   unique. **Contiguity is not enforced** (two branches drafting `0013` is a merge-time
   rename, not a build failure); the validator flags only *duplicates*.
5. **Supersession reference integrity (weak):** every `NNNN` token extracted from a
   `supersedes`/`amends` value (and from a `superseded-by-NNNN` status) resolves to a real
   ADR file. No bidirectional `status` mirroring is required — partial supersession can't be
   a boolean (D1), so we check references *point at something real*, not that history was
   rewritten to mirror them.

Each failing assertion follows the harness rule — **active voice, name the file, say the
fix**: *"ADR 0013 is missing `## Consequences`. Add the section before `## Alternatives
considered` (see design/adr/template.md)."*

### 4b. The generated artifacts — `INDEX.md` + `DIGEST.md`, inside `design/adr/`

**Generate, never hand-maintain** (README §5; a stale digest is worse than none). The same
ADR parser (§4a) feeds a generator that emits two files **inside `design/adr/`** (D5):

- **`design/adr/INDEX.md`** — the decision log / **inform** output:
  `NNNN | title | status | supersedes/amends | one-line Decision`.
- **`design/adr/DIGEST.md`** — the **summarize** output: per ADR, the *core* =
  front-matter + the one-line Decision + the `Confirmation` checklist +
  `More Information` links. The artifact the coherence agent and conformance eval consume,
  so heavy full-text reading stays in the source ADRs.

Generation reuses the C# parser, so there is **one** ADR parser, two consumers (the
validator Rules and the generator) — no second implementation to drift. A Structure
**freshness Rule** asserts the committed `INDEX.md`/`DIGEST.md` equal what the generator
produces from the current records; re-running the generator (an explicit regen entry point,
e.g. gated by `ABOX_ADR_REGEN=1`) writes them. Output pins LF and a stable sort so it is
**byte-identical across the ubuntu + windows CI legs** — the freshness Rule diffs it, so
cross-OS determinism is a generator requirement, not an afterthought.

> **Why inside the protected tree is fine (D5).** `protected-paths` is a flat allow-list
> with no exclusion glob, so `INDEX.md`/`DIGEST.md` under `design/adr/` are `review`-tier
> protected like the records. That is harmless: an ADR add/change already rides an
> owner-reviewed PR, and the regenerated files travel **in that same PR**. The freshness
> Rule makes a stale digest fail the build, so the author must regenerate before the owner
> approves — the review cost is the ADR's, not an extra gate.

### 4c. The grading layers — reuse the judge *agent*, write one new adapter

No new grading engine. The repo has a generic judge **agent** (`.claude/agents/judge.md`)
and workflow (`PLANS/generic-judge.md`); the existing `/judge` command is a *test-rulebook
adapter*, so this is **one new command file**, not just "a rubric":

- **Doc-quality adapter** (the **grade** output) — a new `/judge`-family command feeding the
  judge agent an ADR + an ADR rubric: decision is one imperative sentence; alternatives are
  honest; a revisit trigger exists; the "how" is *linked* not *inlined*; **every
  `Confirmation` bullet is a genuine, atomic, self-contained fitness assertion — not
  placeholder boilerplate** (the D4 guard rail, §3). This is the craft layer — the semantic
  rot a structural validator **provably cannot** catch (D2) — and is the single
  highest-value piece after the format cleanup.
- **Conformance eval** — fire each ADR's `[det]` Confirmation bullets (each names its check,
  §3) in CI; route `[llm]` bullets to a subagent against the diff + that ADR's digest core
  (README §6). Add only where a `[det]` check can't reach (README §6.5).
- **Coherence pass** — one agent over `design/adr/DIGEST.md` checks the seams (supersession
  chains, rules restated inconsistently) — i.e. the *semantic* slice of code-vs-ADR drift
  that D2 keeps out of the validator.

These answer three different questions and stay separate — README §4's table (Conformance /
Doc-quality / Coherence) is the spec; this plan wires it.

---

## 5. Enforcement wiring — it rides `dotnet test`, plus governance for the surface

D3 moves the ADR validator into the test suite, so its primary gate is **`dotnet test`**
(local and CI), not the git hooks. The pieces still touch governance where the *enforcement
surface* — the template — must be protected.

| Enforcer | Adds | Blocking? |
|---|---|---|
| `dotnet test` (the Structure suite) | runs the ADR-format `[Rule]`s (§4a) and the index/digest freshness Rule (§4b) | yes, wherever `dotnet test` already gates — the same gate every Structure rule rides |
| CI `policy-guard` | unchanged for paths; the freshness Rule's regenerate-and-diff runs as part of the test job, mirroring the existing CODEOWNERS regenerate-and-diff precedent | yes (test job) |
| `pre-commit` / `pre-push` ([`.githooks/`](../.githooks)) | **no ADR check added** — the hooks stay protected-path-only and shell-only; ADR format is a test concern now | n/a |
| CODEOWNERS review | unchanged — `design/adr/**` already requires owner review; the generated `INDEX.md`/`DIGEST.md` ride the same gate (§4b) | merge gate of record |

**The template is enforcement surface — protect it.** Add `design/adr/template.md` to
`governance/protected-paths` (the reasoning that protects `tests/Harness/**` and the
Structure rules themselves) so the shape can't be quietly weakened. The Structure `[Rule]`s
and the ADR parser already live under the `critical`-protected `tests/Tests/Structure/**`.
The generated `INDEX.md`/`DIGEST.md` are **not** separately exempted (the policy format has
no exclusion glob) and don't need to be (§4b).

**Promotion is the owner's call, not the agent's.** Whether the Structure suite is a
*required* check on `main` is a branch-ruleset setting, and the ruleset is owner-only
(governance Phase 3: the agent has no `administration` scope). The plan *recommends* the ADR
Rules ride whatever gate the rest of the Structure suite already does; **MgCohen** confirms.

---

## 6. Resolved: the validator is a Structure rulebook Rule, not shell

Earlier drafts resolved "shell vs. a compiled tool" **in favor of shell**, constraining the
front-matter to flat `key: value` so a POSIX checker could parse it without a YAML library.
**D3 reverses that resolution.** The validator is now an xUnit **Structure** `[Rule]` set
(§4a), because the owner chose to fold ADR well-formedness into the existing test taxonomy —
one parity machinery, one Meta self-suite, one `test-rulebook` authoring path — rather than
maintain a second, parallel enforcer in shell.

This does **not** breach ADR 0010/0012. Those fix the repo's *hook-time governance
enforcers* (the protected-paths checker, the notifier) as zero-dependency shell; the ADR
validator is a **test**, gated by `dotnet test`, not a hook enforcer. The flat-front-matter
constraint survives only as a simplicity choice (§3), no longer a shell requirement. The
trade D3 accepts: a malformed ADR is caught at `dotnet test`/CI rather than at local commit
— tolerable because `design/adr/**` is CODEOWNERS-gated and ADRs are authored rarely and
deliberately. The richer seam-graph analysis stays in the **agent coherence pass** (§4c),
reading the generated digest.

---

## 7. Build order — walking skeleton, YAGNI

Each phase is independently shippable and verified by running it, not just building.

- **P0 — Shape + validator, one ADR end-to-end.** Write `design/adr/template.md` (§3). Add
  the ADR parser to `tests/Tests/Structure/Support/` and the format `[Rule]`s with their
  `### ` Rulebook headers (checks 1–4), via the `test-rulebook` skill. Bring **one** ADR
  (`0001`, per README §8.6) fully to the template, including a genuine `Confirmation` block.
  *Done-when:* the Structure suite passes on `0001`, and fails on a deliberately broken copy.
- **P1 — Generated index + digest.** The generator emits `design/adr/INDEX.md` +
  `design/adr/DIGEST.md`; add the freshness Rule (§4b). *Done-when:* the index regenerates
  byte-identical on both CI legs and the freshness Rule is green.
- **P2 — Full migration + weak reference check (D4).** Restructure `0002`–`0012` to the
  template: flat front-matter, the five ordered sections, and a `Confirmation` block on
  every record — **preserving each decision's text and dated amendments verbatim**, writing
  only genuine fitness assertions (no placeholder `[review]`), and relocating (never
  deleting) one-off sections. Convert prose supersession to `supersedes`/`amends`/
  `superseded-by-NNNN` per D1. Turn on check 5. *Done-when:* the Structure suite is green
  across all twelve and the reference check resolves every `NNNN` token. Then **recommend
  the owner ensure the Structure suite gates `main`.**
- **P3 — Doc-quality adapter.** Write the new `/judge` ADR command (§4c), including the
  anti-boilerplate `Confirmation` rubric. *Done-when:* it grades `0001` and surfaces at
  least one real finding.
- **P4 — Conformance eval.** Fire `[det]` bullets (each names its check) in CI; route
  `[llm]` to a subagent. Add only where `[det]` can't reach. *Done-when:* one ADR's
  checklist runs against the codebase.
- **P5 — Coherence pass.** Agent over `design/adr/DIGEST.md` for cross-ADR seams.
  *Done-when:* it runs and reports clean (or a real conflict).

P0–P2 kill the "two formats / prose supersession / no enforcement" problem. **P3 is the
highest-value phase** — the semantic grading a validator can't do, and the guard against the
boilerplate risk D4 introduces — and should not be treated as optional; P4–P5 are the README
pipeline and can trail.

---

## 8. Stability contract — the ADR template is a ratchet

Carry over the test harness's stability discipline; the failure modes are identical:

- **Adding an ADR — safe, encouraged.** A new record only adds decisions (with its
  front-matter, sections, and `Confirmation`, or the Structure suite fails).
- **Adding an optional trailing section — safe.** It is outside the parseable core.
- **Editing the required-section set, front-matter keys, or `status` enum — dangerous.**
  This is the schema every ADR and every downstream consumer (index, digest, judge,
  conformance) depends on; a change reshapes all records at once and can make the digest
  silently drop the operative decision. Treat it as an architecture change to the ADR system
  — route it through a PR and owner review like any protected path (the template is now
  protected, §5). **Don't reshape casually.**

The summary: **add ADRs liberally; change the schema deliberately; reshape the harness
almost never.**

---

## 9. Done-when (the harness exists)

- `design/adr/template.md` is the single owned shape and is protected; the Structure suite's
  ADR `[Rule]`s (zero new enforcer outside the test taxonomy) fail the build on structural
  drift, paired with their Rulebook headers under the parity guard.
- All twelve ADRs conform to the template **via genuine migration** (full section set +
  honest `Confirmation` on each, decision text preserved verbatim — D4), and `superseded-by`/
  `amends`/`supersedes` references all resolve (check 5). `INDEX.md` and `DIGEST.md` are
  **generated** inside `design/adr/`, kept fresh by the freshness Rule, and ride the ADR's
  own owner-reviewed PR.
- The doc-quality adapter exists and runs on PR (including the anti-boilerplate
  `Confirmation` check); the conformance and coherence layers exist as named, runnable jobs
  (introduced incrementally), each answering its own question.
- A new ADR lands *with* its front-matter, sections, and `Confirmation` or the build fails —
  the ratchet is closed for new records, and the legacy records have been brought up to the
  same bar rather than exempted.

## References

- [`design/adr/README.md`](../design/adr/README.md) — ADR practice, house format, the
  Confirmation/digest/pipeline **proposal** this plan implements (§2, §4–§6, §8). D4
  knowingly overrides its §1 immutability counsel for a uniformly evaluable set.
- [`tests/Harness/README.md`](../tests/Harness/README.md) — the Structure test type, the
  `### `-Rule + `[Rule]` parity discipline this validator now joins (D3), and the
  `RulebookFormat`/`SourceTree` parsers the ADR parser mirrors.
- [`tests/Tests/Structure/`](../tests/Tests/Structure) — where the ADR `[Rule]`s, the parser
  (`Support/`), and the Rulebook headers land; authored with the `test-rulebook` skill.
- [`governance/README.md`](../governance/README.md) — "one policy, many enforcers" and the
  CI regenerate-and-diff pattern the freshness Rule follows.
- [`governance/protected-paths`](../governance/protected-paths) — the flat, exclusion-free
  allow-list (`design/adr/**` is `review`-tier, "frozen history") under which the template is
  newly protected and the generated files ride the ADR PR (D5).
