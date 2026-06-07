# ADR practice: format, contextualization, and conformance evals

A cold-readable guide to how we author, review, and evaluate Architecture
Decision Records (ADRs) in this repo. It captures both the *settled house style*
and a *proposed pipeline* for agent-driven review and conformance checking. Where
something is a proposal still to be validated rather than current practice, it is
marked **(proposal)**.

If you are an agent about to review or write an ADR, read §1–§3 first; if you are
wiring up the review/eval tooling, read §4–§6.

---

## 1. What an ADR is — and is not

An ADR answers **"why did we choose this, and what did we trade away?"** — frozen
at a moment in time. It is a *historical decision record*, ideally immutable once
accepted. When a decision changes you write a **new** ADR that supersedes the old
one; you do not rewrite history.

An ADR is **not**:

- a description of how the system works *today* (the "how"),
- a reference/API doc, or
- a tutorial.

Those are **living documents** — code, design docs, READMEs — and they *drift* as
the system evolves. An ADR must not drift. This split is the whole point:

> **ADRs capture *why*; design docs and code capture *how*.**

The practical consequence — and the most common failure mode to avoid — is pouring
the full solution into an ADR. The code then changes, the ADR does not, and you
have **documentation that lies, confidently**. If you find yourself documenting
*how it works now*, that content belongs in a design doc or the code; the ADR
**links** to it.

References: [Michael Nygard, "Documenting Architecture Decisions"][nygard];
[Martin Fowler, "Architecture Decision Record"][fowler].

---

## 2. House format

We use a [MADR][madr]-flavoured shape (Nygard's original sections plus MADR's
machine-readable front-matter and `Confirmation` section). Existing records
`0001`–`0007` are Nygard-style; new and amended ADRs should adopt the front-matter
and `Confirmation` block so the tooling in §4 has a deterministic "core" to extract.

```markdown
---
status: accepted            # proposed | accepted | superseded-by-NNNN
date: 2026-06-07
supersedes:                 # ADR numbers this replaces, if any
amends:                     # rules/ADRs this refines (e.g. R-SPINE-2)
---

# ADR NNNN — {short title, the decision phrased as the rule}

## Context
The forces and the problem. Why this needs deciding now.

## Decision
The rule, stated as an imperative "We will …". One or two lines per rule;
one-line definitions of key terms are fine here.

## Consequences
What follows — good and bad — plus a **revisit trigger** ("we will revisit if …").

## Confirmation                          # see §6 — the binary fitness functions
- [det] {deterministic, CI-checkable assertion}
- [llm] {semantic assertion, LLM-judged yes/no}

## Alternatives considered
The options weighed and why they lost.

## More Information
Links to the living "how": design doc, code path, spec rule, related ADRs,
external references.
```

Two sections do the load-bearing work for the rest of this document:

- **Front-matter** (`status`, `supersedes`, `amends`) — machine-readable, so an
  index/digest can be generated rather than hand-maintained.
- **`Confirmation`** — MADR 4.0's section for fitness functions (§6).

---

## 3. The two jobs we use ADRs for

### 3a. Code standards

A coding convention *is* a legitimate ADR subject — but the ADR records the
**decision and its rationale**, not the rulebook. The catalog of rules lives in the
style guide (`CLAUDE.md` / `.editorconfig`); the linter/analyzer **enforces** it;
the ADR justifies the non-obvious choice.

> Rule of thumb: write an ADR for a coding standard only when the choice was
> **contested or non-obvious**. Trivial conventions belong only in the linter.

Real-world example — **Backstage ADR003: Avoid Default Exports** ([source][bs003]):

```markdown
## Decision
We will stop using default exports except when absolutely necessary
(such as React.lazy modules). [short workaround snippet]

## Consequences
We will actively work to remove them from our codebases...
We will add tools, such as lint rules, to help migrate away from default exports.
```

Note the anatomy: decision is **one imperative sentence**; the long rationale is a
**link** (a `humanwhocodes.com` blog post); enforcement is **named, not embedded**
("we will add lint rules"). Our `CLAUDE.md` no-comments rule is exactly this kind
of decision — the ADR would be its *justification record*, not a restatement.

### 3b. Architectural rules

This is ADRs' home turf: "whenever you build a *collection*, use the *repository
pattern*" is a textbook ADR. State the rule, define the key terms in a line each,
list consequences + a revisit trigger, and **link out** for the mechanics — do not
inline the full "how to build a repository."

Real-world example — **Backstage ADR011: Plugin Package Structure**
([source][bs011]) states a package taxonomy (each package type gets a one-line
definition), references a **GitHub issue thread** for the fuller reasoning rather
than pasting it, and lists enforcement (lint rules, `CODEOWNERS`).

### What goes where

| Goes **in** the ADR | Gets **linked from** the ADR |
|---|---|
| The decision, as a "We will…" rule | Deep how-to / tutorial ("how to build a repository") |
| *Why* — drivers, forces, options rejected | Long external rationale (blog, paper, RFC, issue) |
| One/two-line definitions of key terms | The pattern's canonical reference (e.g. Fowler PoEAA) |
| Consequences + a revisit trigger | The living style guide / API docs / the code itself |
| How it will be **enforced** | |

---

## 4. The review & contextualization pipeline (proposal)

ADRs are not independent — they amend and reaffirm each other (`0003 §5` reaffirms
`0002 §5`; `0005 §4` amends `R-SPINE-1`; `0001` has dated amendments). Bugs in an
ADR *set* live in the **seams**. The pipeline reflects that:

```
                 ┌─ per-ADR review ─┐
  10 ADRs ──────▶│ 1 subagent / ADR │──▶ depth findings
  (full files)   │ full ADR context │
                 └──────────────────┘
        │
        │  digest script (§5)
        ▼
  cores.md (concat of each ADR's "core")
        │
        ├──▶ coherence agent ──▶ cross-ADR conflicts / drift
        │    (one agent, whole set in one context)
        │
        └──▶ conformance eval ──▶ fires the §6 fitness functions
```

1. **Per-ADR review** — one subagent per ADR, **full file** as context. Good for
   *depth*: is this single decision internally sound, are its alternatives honest,
   does it follow the house format? By construction it cannot see cross-ADR issues.
2. **Digest extraction** — a script concatenates each ADR's *core* (§5) into one
   file, so the next stages fit in a single context window without reading all
   full files (the set is ~60 KB; `0001` alone is 13 KB).
3. **Coherence pass** — one agent reads the concatenated digest and checks the
   **seams**: supersession chains, rules restated inconsistently, conflicting
   assertions.
4. **Conformance eval** — runs each ADR's `Confirmation` fitness functions (§6)
   against the codebase.

### Three distinct evaluation jobs — keep them separate

It is easy to blur these; they need different inputs and live in different places.

| Job | Question | Where it lives |
|---|---|---|
| **Conformance** | Is the *codebase* faithful to this ADR? | per-ADR `Confirmation` checklist (§6) |
| **Doc quality** | Is the *ADR document* any good? (states alternatives? has a revisit trigger? decision is one imperative sentence? links instead of inlining?) | **one** generic rubric, applied by the per-ADR reviewers — *not* duplicated into each ADR |
| **Coherence** | Do the ADRs contradict each other? | the coherence agent; concretely, checks no two ADRs' `Confirmation` assertions conflict |

---

## 5. The digest / "core" extraction (proposal)

The open question for any concat-the-ADRs script is *what counts as "core."* Do
**not** mechanically slice lines — our ADRs bury the operative decision in prose
(and amendments), so a grep-of-headings digest would silently drop the actual
decision and an agent would reason from a half-truth.

Instead, **"core" is a contract the template guarantees**. The digest per ADR is:

> **front-matter** (`status` / `supersedes` / `amends`)
> **+ the one-line Decision**
> **+ the `Confirmation` checklist**
> **+ the `More Information` links**

This one artifact has three consumers — the coherence agent (to spot conflicts),
the conformance eval (to fire the checks), and humans (as a decision log). Because
the parts are structured sections, the script extracts them deterministically.

Operational rule: **generate, never hand-maintain** the digest (a `make
adr-digest` target or pre-commit hook), so it can never drift from the source
ADRs. A stale digest is worse than none.

Prior art: this is what [MADR][madr]/[Log4brains][log4brains] call an ADR
**index/log**, and what [`adr-tools`][adrtools] generates.

---

## 6. The `Confirmation` section — binary fitness functions (proposal)

This is the mechanism that makes ADRs *evaluable*. It is MADR 4.0's `Confirmation`
section, whose own template text reads (verbatim):

> "Describe how the implementation / compliance of the ADR can/will be confirmed.
> **Is there any automated or manual fitness function?** If so, list it and explain
> how it is applied. … a design/code review or a test with a library such as
> **ArchUnit** can help validate this."

The broader concept is **architectural fitness functions** from *Building
Evolutionary Architectures* (Ford, Parsons, Kua): architectural rules expressed as
automated checks.

### Design rules

1. **A checklist of atomic binary assertions, not one question.** An ADR encodes
   several invariants; one yes/no is too coarse, and a *compound* "is X and Y and
   Z true?" is the most reliable way to make an LLM judge flaky. One claim per
   line, AND-ed for the ADR's overall pass.
2. **Tier each assertion; LLM is the last resort.** The value of binary phrasing is
   cheap, repeatable validation:

   | Tier | Mechanism | Use when | Cost |
   |---|---|---|---|
   | **`[det]`** | analyzer / ArchUnit-style test / lint / grep / unit test | the invariant is *structural* | free, exact, runs in CI |
   | **`[llm]`** | subagent answers yes/no vs diff + ADR core | the invariant is *semantic*, no tool can see it | cheap, slightly noisy |
   | **`[review]`** | human/agent judgement | genuinely irreducible to binary | expensive |

3. **Phrase `[llm]` questions for reliability:** atomic; self-contained (answerable
   from the diff + this ADR's core alone, no hidden context); carries its own
   pass/fail criteria, ideally a passing and a failing example; framed toward an
   observable state, not a vibe.
4. **Keep it to the load-bearing invariants** — ~3–6 per ADR, not every sentence.
5. **Asymmetry to respect:** a `[det]` check self-validates in CI (it goes red when
   it drifts); an `[llm]` check can silently start lying as code evolves. Bias
   toward `[det]`; promote an `[llm]` item to a real analyzer once the cost of a
   false pass exceeds the cost of writing the test (YAGNI applied to conformance).

### Examples

For **ADR 0001** (flow catalog / R-SPINE-2):

```markdown
## Confirmation
- [det] No `new \w+Flow(` occurs inside a registration lambda or composition root.   (R-SPINE-2)
- [det] Every `FlowCatalog.Register<T>(...)` entry has a non-empty, unique Name.       (boot-guard test)
- [llm] `Flow` holds no run-state fields (no lock/version/snapshot); state lives on FlowContext.
- [llm] `FlowConfig` is supplied as an execution argument to ExecuteAsync, not a ctor field.
```

For a hypothetical **"collections use the repository pattern"** ADR:

```markdown
## Confirmation
- [det] No `DbContext`/`IQueryable` symbol is referenced outside `*/Data/`.            (ArchUnit-style test)
- [llm] Every type exposing a collection of domain entities does so via a repository
        interface, not a raw query.
```

---

## 7. Trade-offs

- **Per-ADR depth vs coherence.** One agent per ADR is blind to cross-ADR seams;
  the coherence pass exists to cover that. Running both costs more than one big
  review, but a single all-files review blows the context budget and dilutes
  attention. The digest is the compromise.
- **Structured "core" vs author freedom.** Requiring front-matter + a
  `Confirmation` block constrains how ADRs are written. The payoff is a
  deterministic digest and runnable evals; the cost is a slightly heavier
  template and the discipline to keep the checklist honest.
- **`[det]` vs `[llm]` checks.** Deterministic checks are exact and self-healing
  but only reach structural invariants; LLM checks reach semantic ones but can rot
  silently. The tiering makes the trade-off explicit per assertion rather than
  global.
- **Conformance section vs rot.** A `Confirmation` block that drifts from the code
  is worse than none. `[det]` checks mitigate this (CI catches drift); `[llm]`
  checks need periodic re-grounding. Keep the list small.
- **ADRs for code standards — scope creep.** Tempting to write one per rule;
  resist. The style guide is the catalog; ADRs are only for the *argued* choices.

---

## 8. Final suggestions

1. **Adopt the §2 house format** going forward: front-matter + `Confirmation` +
   `More Information`. Backfill `0001`–`0007` opportunistically as they are touched
   (consistent with the repo's "applied going forward" stance).
2. **Treat ADRs as *why + links*.** State the rule and the rationale; link the
   "how." Never inline a tutorial.
3. **Use ADRs for code standards only when the choice was contested.** Otherwise
   the linter + style guide suffice. (A `Comment policy` ADR formalizing the
   `CLAUDE.md` no-comments rule with its rationale is a good first candidate,
   because that choice *is* non-obvious and argued.)
4. **Make the digest a generated artifact** (`make adr-digest` / pre-commit), with
   "core" = front-matter + one-line Decision + `Confirmation` + links.
5. **Build the pipeline incrementally (YAGNI).** Start with the per-ADR reviewers
   and the digest. Add the coherence agent when there are enough ADRs for seams to
   matter. Add `[llm]` conformance checks last, and only where a `[det]` check
   cannot reach.
6. **Validate on one ADR end-to-end before rolling out.** Take `0001`, write its
   `Confirmation` block, run it through per-ADR → digest → coherence/eval, and
   confirm the binary phrasing actually holds up.

---

## 9. References

- [Michael Nygard — Documenting Architecture Decisions][nygard] — origin of the ADR
  and the `Context / Decision / Status / Consequences` shape.
- [Martin Fowler — Architecture Decision Record][fowler] — the *why* vs *how*
  framing.
- [MADR — Markdown Any Decision Records][madr]; [template with the `Confirmation`
  section][madrtmpl] — our format base.
- *Building Evolutionary Architectures* — Neal Ford, Rebecca Parsons, Patrick Kua
  — **architectural fitness functions** (the basis for §6).
- [ArchUnit][archunit] — conformance-test tooling for `[det]` checks (JVM; the
  Roslyn/analyzer equivalent applies for .NET).
- [arc42 §9 — Architecture Decisions][arc42] — ADRs within a broader arch-doc.
- Tooling: [`adr-tools`][adrtools], [Log4brains][log4brains].
- In-repo examples used above: [Backstage ADR003 (code standard)][bs003],
  [Backstage ADR011 (architectural rule)][bs011].

[nygard]: https://www.cognitect.com/blog/2011/11/15/documenting-architecture-decisions
[fowler]: https://martinfowler.com/bliki/ArchitectureDecisionRecord.html
[madr]: https://adr.github.io/madr/
[madrtmpl]: https://github.com/adr/madr/blob/main/template/adr-template.md
[archunit]: https://www.archunit.org/
[arc42]: https://docs.arc42.org/section-9/
[adrtools]: https://github.com/npryce/adr-tools
[log4brains]: https://github.com/thomvaill/log4brains
[bs003]: https://github.com/backstage/backstage/blob/master/docs/architecture-decisions/adr003-avoid-default-exports.md
[bs011]: https://github.com/backstage/backstage/blob/master/docs/architecture-decisions/adr011-plugin-package-structure.md
