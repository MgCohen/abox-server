# Intent-Driven vs Spec-Driven Development — A 2026 Research Report

> Deep-research synthesis (fan-out web search → adversarial verification → cited
> synthesis). Produced 2026-06-01. All evidence gathered via web search summaries;
> direct page fetching was blocked in the research environment, so quantitative
> figures are reported as **ranges** and corroborated across multiple independent
> queries. Confidence tags: **[H]** = 2+ independent sources, **[M]** = single
> decent source, **[L]** = vendor/marketing/unaudited.

---

## TL;DR

- **"Intent-driven development" is a real, multi-author emerging category** — at
  least half a dozen unrelated authors/orgs use intent-first framing with no
  shared origin. The underlying idea (author *intent + constraints + verification*,
  let the agent produce the *how*) is genuinely in the water. **[H]**
- **But "IDSD" specifically is one person's brand.** Kapil Viren Ahuja's acronym
  appears *only* in his own Medium posts — zero third-party repos, tools, or citing
  authors — and is rendered inconsistently ("Intent-Driven **Spec** Development" vs
  "Intent-Driven **System** Development"). Like → keep; cite as authority → don't. **[H]**
- **The establishment is NOT in the "replace spec with intent" camp.** Martin
  Fowler and Kent Beck refine spec-driven with *living specs + feedback loops*;
  they critique big-upfront-spec, not specs themselves. Intent advocates borrow
  their critique but diverge on the prescription. **[H]**
- **Intent vs spec is not a war — it's a ladder.** intent → spec → tests → code.
  The only live question is *which rungs a human hand-authors vs regenerates.* Most
  serious practice lands in the middle ("spec-anchored"), not at either extreme.
- **The hard limit is real and physical:** LLM non-determinism (rooted in
  floating-point accumulation order on GPUs, so not fixed by `temperature=0`) means
  a loose intent can pass acceptance criteria and still build the wrong thing. The
  "intent gap" is the central reliability bottleneck. **[H]**
- **The TDD analogy is the strong half of the idea** — and it's academically
  backed (LMUnit, IBM ASTER, AutoUAT/TestFlow). But it only holds *because* humans
  stay in the review loop and fixtures stay *executable/checkable*. "Just write
  English and let it rip" is the weak half.

---

## 1. Definitions & taxonomy

| Term | One-line definition | Source |
|---|---|---|
| **Spec-Driven Development (SDD)** | A version-controlled, often executable *specification* — not the code — is the source of truth; plan → tasks → code are derived from it. | github/spec-kit **[H]** |
| **Intent-Driven Development (IDD)** | Humans define *what* should exist and *why it matters*; autonomous agents determine *how* and *when* it's built ("implementation-last"). | intentdrivendevelopment.org, Mysore **[H]** |
| **IDSD** | Ahuja's coinage; "intent → AI derives spec → human validates spec → AI builds." Not an established term; unstable definition. | Ahuja (Medium) **[M]** |
| **Intent engineering** | The discipline that defines *what the model is allowed to do* and *how to verify it* — distinct from prompting (what you say) and context (what it knows). | Pathmode, MindStudio **[H]** |
| **Context engineering** | Designing the dynamic system that supplies the right info/tools, in the right format, at the right time, so the LLM can do the task ("software engineering for LLMs"). | Firecrawl, Elastic, deepset **[H]** |
| **Agentic engineering** | Structuring/orchestrating agents to run whole dev workflows end-to-end (plan, code, test, iterate to a PR). | IBM, LangChain **[H]** |

**The convergent shape.** Independently of branding, the "intent spec" structure
keeps converging on the same skeleton: **Objective → Outcomes → Constraints → Edge
Cases → Verification** (Pathmode's IntentSpec); or minimally *desired outcome +
constraints + delegation boundary*. Constraints are framed as *"don't stray outside
these lines"* rather than *"follow this exact line."* **[M]**

**The maturity ladder (this is the useful mental model).** SDD itself spans three
levels — and "intent-driven" mostly just renames the top rung:

1. **Spec-first** — write intent/spec before coding to guide the initial build; code then evolves freely.
2. **Spec-anchored** — specs evolve *with* code and are enforced by tests/contracts (Fowler's "living specs" live here). **← where most credible practice sits.**
3. **Spec-as-source** — humans never edit generated code; all change flows through the spec, then regeneration (Tessl's aspiration; code marked `// GENERATED FROM SPEC — DO NOT EDIT`). **[H]**

---

## 2. What people are actually saying

### The intent-driven advocates (genuine multi-author category)
- **Kapil Viren Ahuja** ("Activated Thinker", May 2026): SDD is "too rigid, too
  brittle, too dependent on getting the spec perfect before the system could move."
  Origin story is anecdotal — he changed a deployment target mid-build and "the
  structure couldn't survive a single upstream pivot." Proposes IDSD + a personal
  framework universe (Meridian, Phoenix-OS, HELIX). **[H]**
  - ⚠️ **Credential correction:** he is **Partner & Director, CTO Digital / Head of
    Digital Experiences at Nagarro** — a *divisional/practice* CTO, **not** the
    corporate company-wide CTO. Framing him as "Nagarro's CTO" overstates it. **[H]**
- **Independent voices, no shared origin:** Vishal Mysore ("what is IDD"), Binoy
  Ayyagari ("AIDD — Adaptive Intent-Driven Development, the AI-first successor to
  Agile"), Keyhole Software (a company, "build-first documentation"), ArcBlock
  (`ArcBlock/idd`), Exadra37 (`ai-intent-driven-development`), Giulio Sistilli
  ("Software Engineering 3.0: the Age of the Intent-Driven Developer"), Pathmode
  ("intent engineering"). **[H]**
- ⚠️ **Terminologically incoherent:** some treat "intent" as the *opposite* of
  "spec" (Ahuja, Keyhole), while intent-driven.dev's own tagline is "Context
  Engineering & Spec-Driven Development" and Deepak Babu Piskala frames *SDD* as
  "When Intent Becomes the Source Code." Intent and spec are used as antonyms by
  some and synonyms by others. **[H]**

### The establishment counterweight (refines SDD, doesn't replace it)
- **Martin Fowler / "exploring-gen-ai" series**: catalogs SDD as a real spectrum
  (spec-first / spec-anchored / spec-as-source). His actual position is skepticism
  toward writing the *whole* spec upfront; he favors **living specs that update when
  implementation reveals new information**, tied to XP feedback loops: *"the key to
  making full use of AI… is how to use it to accelerate the feedback loops."* **[H]**
- **Kent Beck** (cited alongside): writing the whole spec upfront "encodes the (to
  me bizarre) assumption that you aren't going to learn anything during
  implementation that would change the specification." **[M]**
- **Bruno Baketarić** ("The Spec Is Not the Method"): SDD is "a workaround shaped by
  the limitations of current technology"; treating it as *the* methodology is "a
  category error." An independent voice converging on the anti-spec-*primacy*
  critique without adopting Ahuja's brand. **[H]**

### Verdict on your original worry
You found a non-famous author and liked the idea. **The idea is validated by the
broader field; the specific author and his acronym are not.** You can adopt the
*substance* (intent-first, living/regenerable specs, constraints-as-guardrails)
with backing from Fowler/Beck/Thoughtworks — you do **not** need to stake anything
on Ahuja or "IDSD."

---

## 3. Head-to-head comparison

| Dimension | Spec-Driven (SDD) wins when… | Intent-Driven (IDD) wins when… |
|---|---|---|
| **Problem clarity** | Domain is well-understood; "done" is enumerable | Exploratory; you'll learn the real shape by building |
| **Review cost** | You want an auditable artifact gate before code | You want to minimize double-review (spec *and* code) |
| **Change tolerance** | Requirements are stable | Upstream pivots are likely (intent survives; detailed specs shatter) |
| **Team shape** | You have analyst+dev discipline to maintain specs | Small team; one person holds intent + verifies output |
| **Legacy code** | Greenfield or well-bounded modules | (Both struggle — you can't generate a project-wide spec for a 3-yr-old codebase) |
| **Risk profile** | Safety/critical invariants need a frozen contract | Speed of exploring edges matters more than one-shot perfection |

**The honest synthesis:** these are *the same ladder at different altitudes*, not
rival camps. Intent is the top rung (the durable "why/what"); the spec is the
middle rung (the regenerable "how"); tests/code are the bottom. The real decision
is **which rungs you hand-author and freeze vs. which you regenerate.** Both
degrade to waterfall if you freeze the top *and* middle before building.

**The verification risk (applies to both, worse for loose intent):**
- LLMs are **non-deterministic by construction** — non-associative floating-point
  accumulation across parallel GPU threads flips near-tied token choices, so the
  same prompt yields different code *even at temperature 0*. Not fixable via
  sampling settings. **[H]**
- Measured accuracy varies up to **~15%** across runs, with best-to-worst gaps up
  to **~70%** (figures from arXiv summaries — confirm exact numbers before quoting). **[M]**
- The **"intent gap" — distance between what a user means and what the program does
  — is the central reliability bottleneck.** Mutating clear prompts into
  ambiguous/contradictory ones drops Pass@1 by 20–40%; a large share of
  syntactically valid code is semantically wrong. **[H]/[M]**
- Therefore the famous failure mode: **"the wrong thing, built correctly"** —
  passes acceptance criteria, still not what you meant. Looser input → more load on
  constraints + verification. Intent-based approaches **don't remove ambiguity, they
  relocate it** and force you to resolve it explicitly. **[M]**

---

## 4. Tooling & adoption reality check (adversarially verified)

| Tool | What/where | Adoption signal | Verification |
|---|---|---|---|
| **GitHub Spec Kit** | Open-sourced by GitHub Sept 2025; CLI; `/specify`→`/plan`→`/tasks`→implement; "constitution" of immutable principles | **~90–92.4k stars** (May 2026); supports **30+** agents (was 3 at launch) | ✅ verified; *93k exact* unconfirmed — use the range |
| **AWS Kiro** | Agentic IDE; preview Jul 2025, **GA Nov 17 2025**; `requirements.md`/`design.md`/`tasks.md` | **250k+** developers (vendor PR); cost complaints (~$0.20/spec request) | ✅ verified; user count is soft/PR-sourced |
| **Tessl** | Guy Podjarny (ex-Snyk); pursues **spec-as-source** | Raised **~$125M** ($25M seed + $100M Series A, Index) at **$500M–$750M** valuation, Nov 2024 | ✅ verified; valuation figure **disputed** between sources |
| **BMAD** | MIT-licensed, 12+ agents | **~46.7k** stars; v6.6.0 Apr 2026 | ⚠️ secondary source |
| **GSD** ("Get Shit Done") | Claude-Code-first SDD framework | **~48–62k** stars (changing fast) | ⚠️ range only |

- **"Every major tool shipped an SDD flavor in 2026"** (Spec Kit, Kiro, Claude
  Code, Cursor, OpenSpec, BMAD, Tessl, Antigravity) — directionally **verified** as
  a media narrative; "becoming the industry norm" is **opinion, not measured
  adoption**, and is **contested** (HN pushback, Augment's "what SDD gets wrong"). **[H]**
- **🔴 IDSD adoption: none found.** Every "IDSD" result traces back to Ahuja's own
  articles. No third-party repo, tool, or citing author. Adjacent "IDD" / "intent
  as source code" work is by *other* authors and is **not** adoption of his
  coinage. **Treat any claim of IDSD traction as unsupported.** **[H]**

---

## 5. The TDD analogy — does "English fixtures, let the LLM translate" hold up?

This is the part of your instinct with the **most empirical support** — and the
clearest boundary conditions.

**It works (backed by peer-reviewed work, not just vendors):**
- **Tests-as-NL-spec:** instead of "generate a function that filters valid emails,"
  you write `it('should return only valid emails from a mixed list')` and the agent
  writes code to pass it. Shines on complex logic (pricing engines, rules
  validators). **[H]**
- **LMUnit** (Contextual AI): a "unit test" is a testable natural-language question
  about a desired response quality; decompose → write NL unit tests → score. SOTA on
  FLASK/BigGenBench/RewardBench 2; ships as a **CI/CD quality gate** (CircleCI
  tutorial repo exists). **[H]**
- **IBM ASTER:** LLM + lightweight static analysis → compilable, high-coverage,
  *natural* unit tests (Java/Python). Competitive-to-better than SOTA; 160+
  developer survey on naturalness; **ICSE 2025 Distinguished Paper**; in watsonx. **[H]**
- **BDD/ATDD revival:** user story → Gherkin → executable script. AutoUAT scenarios
  rated helpful **95%**, TestFlow outputs **92%**; AToMIC reports **93.3%**
  syntactically-correct Gherkin and **100%** of generated UI tests executing. **[H]**

**The boundary conditions (why "let it rip" is the weak half):**
- **LLMs are bad at *verifying* code against NL specs** — frequent false negatives
  (correct code flagged as wrong) and **"assumption injection"** (accepting invalid
  axioms instead of flagging gaps). **[H]**
- **CONTRADICTION:** more elaborate prompting *improves generation* (Tweag) but
  *increases misjudgment in verification* (arXiv). "A more precise English fixture
  is always better" is **not** unconditionally true — generation and verification
  are different tasks. **[H]**
- Every successful case study **keeps a human in the review loop**. The 92–100%
  numbers are *with* human gating, not full autonomy.
- A research line argues English fixtures are insufficient alone and should be
  **compiled to formal/executable specs** to remove non-determinism and add
  traceability — direct tension with loose-intent advocates. **[M]**

**Takeaway:** the analogy holds *if the fixture stays executable/checkable and a
human gates it.* The moment the "fixture" is just unchecked prose, you've moved the
ambiguity, not removed it.

---

## 6. Concrete ways a team can leverage intent-driven practice

1. **Author intent + constraints + verification; let the agent draft the spec.**
   Keep the human artifact small (one screen: objective / outcomes / constraints /
   edge cases / verification). Review the *generated* spec, not hand-maintain it.
2. **Constraints as guardrails, not tramlines** — "don't cross these lines" (perf
   budgets, invariants, forbidden deps) rather than step-by-step procedure.
3. **Living specs over frozen specs** (Fowler): regenerate/refine the spec whenever
   implementation teaches you something. Never freeze top + middle rung together.
4. **Natural-language fixtures as the contract** — Given/When/Then on the *critical
   path only* (not exhaustive enumeration), compiled to executable tests, run in CI.
5. **NL evals as a CI quality gate** (LMUnit/promptfoo pattern) for any
   model-mediated behavior, so regressions fail the build.
6. **Estimate at the intent level** (capabilities/outcomes) rather than task
   decomposition — shifts sizing to outcomes.
7. **Keep a "constitution"** of immutable project principles (Spec Kit's idea) that
   governs every generated spec/plan/task.

---

## 7. How *we* could use it (tailored to this repo)

Good news: **this repo is already closer to intent-driven than to brittle SDD.**
You have the rungs in place — the move is to lean on the durable ones and make the
volatile ones cheaper/regenerable, *without* throwing away the discipline that's
working.

| Our existing artifact | Maps to | Intent-first adjustment |
|---|---|---|
| `design/behavioral-oracle.md` (Tier-A invariants, the *why*) | **Intent + constraints** (the durable top rung) | Treat the oracle as the **single durable source of truth**. It already plays the "constitution" role. |
| EARS requirements in `02-prd.md` | **Constraints / acceptance** | Keep as guardrails ("don't cross these lines"), tied back to oracle items by citation (you already cite Tier-A). |
| ADRs (`design/adr/`) | **Context** handed off "as a brief, not a transcript" | Keep — these *are* the compaction points the intent-driven crowd talks about. |
| Per-layer specs in `03-implementation-plan.md` (L1→L12) | **Spec (middle rung)** | Make these **cheaper and more regenerable** *against the oracle*, rather than hand-maintained forever. This is "the document becomes the work," applied conservatively. |
| Walking-skeleton, layer-by-layer build | **Feedback loops** (Fowler's living-spec discipline) | Already correct — you avoid big-upfront-spec by construction. |
| `tests/` + "behavior verified (run it, not just compile)" per layer | **Executable NL fixtures** | This is your strongest lever: phrase per-layer "done-when" gates as Given/When/Then behavior fixtures derived from the oracle, run in CI. The oracle's parity ACs (AC1–AC6) are natural fixtures. |

**Suggested adoption (lowest-risk → highest-leverage):**
1. **Promote the oracle to explicit "constitution" status** — one line in CLAUDE.md
   already routes there; make "regenerate the layer spec from the oracle" an
   accepted move when an upstream decision changes (the pivot scenario Ahuja got
   burned by).
2. **Express per-layer done-when gates as executable behavior fixtures**
   (Given/When/Then over the critical path), so "behavior verified" becomes a CI
   gate, not a manual step. This is the TDD-analogy done *with* a human gate.
3. **Don't go spec-as-source.** Given LLM non-determinism + a PTY/ConPTY +
   subscription-billing system where "the wrong thing built correctly" is expensive,
   stay at **spec-anchored**: humans still own the oracle and read the diffs; specs
   are regenerable but not the only source.
4. **One-screen intent header per layer** before building: objective / outcomes /
   constraints (cite oracle) / edge cases / verification. Cheap, and it's the
   highest-signal artifact for an agent.

**What to explicitly *not* adopt:** the "SDD is dead / IDSD replaces it" framing,
spec-as-source regeneration of the whole system, and any reliance on Ahuja's
acronym as if it were an industry standard.

---

## Sources

**SDD / definitions / ladder:** github/spec-kit; martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html; thebcms.com/blog/spec-driven-development; developer.microsoft.com/blog/spec-driven-development-spec-kit; tessl.io/blog/from-code-centric-to-spec-centric/; thoughtworks.com (SDD 2025 practices); arxiv.org/abs/2602.00180.

**Intent-driven / intent engineering:** intentdrivendevelopment.org; intent-driven.dev; medium.com/@visrow (Mysore); medium.com/@binoy_93931 (AIDD); keyholesoftware.com; levelup.gitconnected.com (SE 3.0, Sistilli); pathmode.io/glossary/intent-engineering + /intentspec; medium.com/@__bbak (Baketarić); medium.com/data-science-collective (Piskala).

**Ahuja / IDSD:** medium.com/activated-thinker/the-method-that-replaces-spec-driven-development-idsd-66e921f6cdf7; .../spec-driven-development-isnt-broken-it-will-collapse; .../software-engineering-is-done-it-forked; linkedin.com/in/kvahuja (credential check).

**Establishment / Fowler / Beck:** martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html; linkedin.com/posts/martin-fowler-com (living specs / feedback loops).

**Skeptics:** marmelab.com/blog/2025/11/12/spec-driven-development-waterfall-strikes-back.html; zenn.dev/cbmrham (skepticism); blog.devgenius.io (Canciani, "the spec is the code"); news.ycombinator.com items 45935763 / 47197595 / 46835618; augmentcode.com/blog/what-spec-driven-development-gets-wrong.

**Non-determinism / intent gap:** dl.acm.org/doi/10.1145/3697010; arxiv.org/pdf/2308.02828; arxiv.org/pdf/2603.17150 (Intent Formalization); morphllm.com/defeating-nondeterminism-llm-inference.

**Tooling/adoption:** star-history.com/github/spec-kit; github.blog (Spec Kit launch); kiro.dev/docs/specs; aws.amazon.com (Kiro roundup); techcrunch.com + fortune.com (Tessl funding); marktechpost.com (BMAD/9-tools); github.com/brAIniac-sa/GSD.

**TDD analogy / NL fixtures:** tweag.github.io/agentic-coding-handbook/WORKFLOW_TDD; langwatch.ai/blog/tdd-with-llms; contextual.ai/lmunit + arxiv.org/abs/2412.13091; research.ibm.com/blog/aster-llm-unit-testing + arxiv.org/abs/2409.03093; functionize.com (BDD/NLP); arxiv.org/abs/2504.07244 (AutoUAT/TestFlow); arxiv.org/abs/2510.18861 (AToMIC); arxiv.org/pdf/2508.12358 (verification failures); arxiv.org/abs/2505.09027 (Tests-as-Prompt).
