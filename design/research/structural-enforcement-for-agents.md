# Hard Structural Enforcement for AI Agents — A 2026 Research Report

> Deep-research synthesis (fan-out web search → adversarial verification → cited
> synthesis). Produced 2026-06-10. Five parallel research angles: compile-time vs
> test-based architecture enforcement, AI-agent-specific code structuring &
> guardrails, modular-monolith physical-vs-logical boundary cost/benefit,
> community/forum opinion (HN/Reddit/blogs), and academic + empirical evidence.
> Confidence tags: **[H]** = official vendor/authority docs or 2+ independent
> sources, **[M]** = single decent source or comparative judgment, **[L]** =
> anecdote / unsourced figure / single weak source.
>
> **Motivating question:** This repo deliberately goes "heavy" on hard walls —
> many assemblies, compile-time blocking — on the belief that compile-time
> enforcement is *better for AI agents* than relying on structural/architecture
> tests that only fail after a build. For a human-only team the boilerplate would
> be wrong; the bet is that it's worth it for agents because the walls are (a)
> harder for the AI to break and (b) easier to validate. Is that bet supported by
> the literature, expert opinion, and community experience?

---

## TL;DR

- **The defensible version of the thesis is "prefer mechanical, immediate,
  compiler-level enforcement over soft prose rules" — and that is exactly where
  the evidence points.** The *non*-defensible version is "therefore maximize the
  number of assemblies," which the same literature says costs you (and costs
  agents specifically) without buying more safety. **[H]**
- **The single best-evidenced claim in the whole space:** machine-checkable
  constraints (types, compiler) measurably improve AI code correctness.
  Type-constrained decoding cuts compile errors **>50%** and lifts functional
  correctness; **~94%** of compile errors in their LLM evals were type-check
  failures — the exact class a strong typed boundary makes impossible. **[H]**
- **The literature splits "boundaries" into two levers that behave oppositely for
  agents.** *Logical / compile-time* boundaries (typed walls, declared dependency
  graphs, the compiler refusing a bad reference) **help**. *Physical
  fragmentation* into many small assemblies/files **hurts** — it inflates the
  files an agent must traverse and hold in context ("file-fragmentation
  paradox"). The win is the *enforcement mechanism*, not the *module count*. **[H]**
- **The authorities you'd respect already prefer the compiler over tests** — for
  precisely the "fails immediately, can't be ignored" reason — *but* they treat
  aggressive source-splitting as a high-cost workaround for a missing C# module
  system, not a virtue. Simon Brown, Mark Seemann, Microsoft/Ardalis. **[H]**
- **Automated feedback is not a silver bullet.** Self-repair gains are "often
  modest… sometimes not present at all"; the *granularity and timing* of the
  signal is the bottleneck. This is itself the argument for compile-time: a
  precise, immediate compiler error beats a vague late test failure. **[H]**
- **Honest gap:** no controlled study shows "more-modularized codebase → higher
  agent success rate." The evidence is (a) types/compiler help — *strong, direct*;
  (b) structure/locality help — *real but small and confounded with "we added a
  tool."* The leap to "many assemblies → fewer agent errors" is a well-motivated
  hypothesis, not a measured result. **[H]**

---

## 1. Strongest evidence: machine-checkable constraints improve AI code

This is the one place with hard, controlled data, and it is the core of the
"better for AI" intuition.

- **Type-constrained decoding cuts compilation errors by >50% and raises
  functional correctness** (pass@1 +3.5% synthesis, +5.0% translation, **+37% on
  repair**). The paper reports **~94% of compile errors in their LLM evaluations
  were type-check failures** — exactly the class a strong typed boundary makes
  unrepresentable. Mündler, He, Wang, Sen, Song, Vechev, "Type-Constrained Code
  Generation with Language Models," **PLDI 2025**. **[H]** (controlled, multiple
  model families incl. >30B open-weight; independently surfaced by two of our
  five angles). https://arxiv.org/abs/2504.09246
- **Practitioner translation of the same mechanism:** wrap primitives in domain
  types so *"if the agent swaps `DocumentName` and `BlobUri`, the code won't even
  compile"* — guardrails must be non-optional, not a prompt the agent can ignore.
  Jeroen Van Eyck, "Guardrails for Agentic Coding." **[M]**
  https://jvaneyck.wordpress.com/2026/02/22/guardrails-for-agentic-coding-how-to-move-up-the-ladder-without-lowering-your-bar/
- **But automated feedback is bounded by the model's ability to interpret it.**
  Olausson, Inala, Wang, Gao, Solar-Lezama, "Is Self-Repair a Silver Bullet for
  Code Generation?", **ICLR 2024**: gains "often modest, vary a lot between
  subsets, sometimes not present at all"; human-level feedback far outperforms
  model self-critique. **[H]** Implication: a wall that yields a *precise,
  immediate compiler error* is worth far more than one yielding a vague late test
  failure — the timing/granularity of the signal is what pays off, which *is* the
  compile-time argument. https://arxiv.org/abs/2306.09896
- **Feedback-loop quality matters more than its mere presence.** "Helping LLMs
  Improve Code Generation Using Feedback from Testing and Static Analysis"
  (arXiv 2412.14841): fine-grained error locations help more than binary
  "did it compile." **[M]** https://arxiv.org/abs/2412.14841

## 2. Architecture authorities: prefer the compiler — but physical splitting has a real cost

The people you'd most respect on .NET architecture *already* prefer compiler
enforcement over test-based enforcement, for your reason.

- **Simon Brown** (C4 model, *Software Architecture for Developers*, originator of
  "modular monolith"): *"I'd personally like to use the compiler to enforce my
  architecture if at all possible."* He ranks enforcement: discipline/review
  (weakest — *"we all know what happens when budgets and deadlines start
  looming"*) < build-time static analysis like ArchUnit/NDepend (*"fallible, and
  the feedback loop is longer than it should be"*) < **compiler**. **[H]** This is
  essentially the repo's argument, from an authority.
  https://simonbrown.je/modular-monolith/
- **…but Brown also calls aggressive source-splitting *"very much an idealistic
  solution, because there are real-world performance, complexity, and maintenance
  issues."*** His ideal is a *language module system* (Java JPMS, Spring Modulith)
  giving compiler enforcement **without** multiplying deployment units. C# lacks
  that — which is the *only* reason .NET teams reach for assemblies + `internal`.
  It's a workaround for a missing language feature, not a virtue. **[H]**
- **The "public vs published" gap** is the conceptual driver: a type can be
  `public` (visible) yet not *published* (intended for external use). Module
  systems expose a public type to *some* modules only; C#'s nearest equivalent is
  a separate assembly + `internal`. Simon Brown. **[H]**
- **Mark Seemann** generalizes: prefer designs where the compiler gives feedback
  over designs that defer discovery to run-time/tests — "the better the type
  system, the fewer tests you need." *Code That Fits in Your Head* / ploeh blog.
  **[H]** (philosophy-level; the general "types over tests" case, not
  specifically about assembly walls.) https://blog.ploeh.dk/
- **Microsoft / Ardalis** endorse separate projects for compiler-enforced
  encapsulation: a single project has *"no clear indication of which classes…
  should depend on which others… frequently leads to spaghetti code,"* while
  separate projects *"enforce restrictions on which layers can communicate."*
  **[H]** *Note the admitted leak:* the DI composition root must reference
  everything, so even the assembly wall isn't airtight at wiring — mitigated only
  by convention. https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- **The genuine substantive disagreement is about *scope*, not speed:** the
  compiler enforces only coarse, reference-level dependency rules (project A can't
  see B). Application-specific conventions (naming, "validators implement
  `IValidator`", "no dependency buried inside a method body") are *not* expressible
  as references — that is the documented gap NetArchTest/ArchUnitNET fill, and
  their own advocates position them for exactly that, accepting the weaker
  run-after-build feedback as the cost. Ben Morris (author of NetArchTest);
  *Building Evolutionary Architectures* (Ford/Parsons/Kua, Thoughtworks). **[H]**
  https://www.ben-morris.com/writing-archunit-style-tests-for-net-and-c-for-self-testing-architectures/

## 3. Standard human-team guidance: few assemblies

- **The most-repeated heuristic: create a new assembly/project only when you need
  to deploy/version that code independently.** Codurance ("Multiple projects…
  considered harmful"); Patrick Smacchia (NDepend) lists *"creating assemblies
  merely to hide implementation details from your own team"* as an **invalid**
  reason. Logical boundaries = folders/namespaces; physical boundaries =
  deploy/versioning units, and the two *"do not necessarily map one-to-one"*
  (Microsoft). **[H]**
  https://www.codurance.com/publications/2015/03/23/multiple-projects-in-visual-studio
  · https://www.red-gate.com/simple-talk/development/dotnet-development/partitioning-your-code-base-through-net-assemblies-and-visual-studio-projects/
- **Real-world counts are single digits.** The de-facto reference (Ardalis Clean
  Architecture) settles at ~4 projects driven by the inward dependency rule; ">20
  projects" is widely called a smell (Chad Myers / Los Techies). **[H]** (the
  *principle* is stable; specific build-time figures are dated 2008–2015 and
  softer on modern SDK-style parallel/incremental builds). **[M]**
  https://ardalis.com/clean-architecture-asp-net-core/
- **Costs of many projects are real:** build/debug time, DI/wiring ceremony,
  `Copy Local` duplication, and `InternalsVisibleTo` sprawl (its legit use is
  letting *test* assemblies reach `internal` types; overusing it beyond tests
  quietly defeats the encapsulation the assembly boundary was meant to give).
  **[H]**
- **The lone clear dissent for physical walls:** Mark Heath argues that in
  large/less-disciplined teams a separate assembly *"does make a very real and
  noticeable difference in how often coupling is inadvertently introduced"* —
  while conceding *"Visual Studio simply does not cope well with large numbers of
  projects."* **[M]** *Interesting twist for this repo:* an AI agent is arguably
  the ultimate "large, less-disciplined team," so Heath's pro-wall argument
  transfers to agents **better** than to the human teams he wrote it for.
  https://markheath.net/post/some-thoughts-on-assemblies-versus

## 4. The critical nuance: the "file-fragmentation paradox"

This cuts against the literal "many assemblies" instinct and is the finding to
weigh most.

- **Logical/compile-time boundaries help agents; physical fragmentation hurts
  them.** Reading 15 small focused files costs 15+ tool calls and context budget,
  where one larger file is a single read; aggressive splitting/compression can
  *increase* total session cost. Recommended agent structure is **vertical slices
  (by feature), maximizing locality**, not horizontal layers scattered across many
  projects. Tian Pan, "The AI-Legible Codebase." **[L]** on the figures (blog,
  unsourced), but **[M]** on direction — corroborated by a strong vertical-slice
  consensus (Bogard; Milan Jovanović). https://tianpan.co/blog/2026-04-13-the-ai-legible-codebase
- **Armin Ronacher** (creator of Flask) prefers Go for agents because it
  *"forbids circular dependencies between packages"* and stays explicit/local, and
  warns *"hiding permission checks in another file… will almost guarantee you that
  the AI will forget."* **[M]** The lever is *enforced dependency rules +
  locality*, not assembly count. https://lucumr.pocoo.org/2025/6/12/agentic-coding/
- **The one direct on-point anecdote for the thesis:** a team that moved to a
  Bazel-style declared dependency graph (*"a build can only see what it explicitly
  declares as a dependency"*) reported **"no instances of agents circumventing
  architectural rules"** afterward and judged the boilerplate worth it. Phoebe
  engineering, "Enforcing Architecture in an Agent-Driven Codebase." **[L]**
  (single-team anecdote, but exactly the hypothesis). The lever was *declared
  dependencies* — achievable via project references *or* a build graph; again,
  mechanism over raw count. https://www.phoebe.work/blog/enforcing-architecture-in-an-agent-driven-codebase

## 5. The "harness" framing — where the instinct fits best

- **Birgitta Böckeler** (Thoughtworks Distinguished Engineer, on Fowler's site),
  "Harness Engineering for Coding Agents," distinguishes **computational controls**
  (deterministic, millisecond-fast: type checkers, linters, *ArchUnit
  module-boundary tests*) from **inferential controls** (LLM-as-judge: slow,
  non-deterministic), and explicitly cites a *"coding-agent hook running ArchUnit
  tests that check for violations of module boundaries"* as a reliable sensor.
  **[H]** Takeaway: invest in *deterministic, fast-failing* checks — the compiler
  is the fastest, cheapest computational control of all, which is the steelman of
  the repo's position. https://martinfowler.com/articles/harness-engineering.html
- **Adjacent caution from security research:** soft instructional guardrails leak
  — *"for any finite set of guardrails, some prompt exists that gets the AI to
  disregard them"* — pushing enforcement to the *boundary* (tool calls,
  compilation) rather than instructions. **[M]** Reinforces "prefer compiler/type
  enforcement over prose rules." https://www.helpnetsecurity.com/2026/06/10/broken-ai-guardrails-research/

## 6. Community/forum spread

- **Topic A (over-modularizing for boundaries) is genuinely contested.**
  Pragmatist center of gravity: *"Logical separation… preserves developer sanity.
  Physical separation… lets you ship flexibly"* and "split only when a real
  problem appears." Skeptics: hard boundaries *"lock in Hyrum's-law mistakes"* and
  "move the spaghetti into the connections." Counter-skeptics: *"in monoliths
  things slowly degrade… it's just far easier to slip when it's one codebase."*
  HN threads [45810482](https://news.ycombinator.com/item?id=45810482),
  [38793015](https://news.ycombinator.com/item?id=38793015). **[M]** (anecdotal but
  recurring). The emerging .NET "have it both ways" answer: **architecture/fitness
  tests inside a unified solution** rather than project sprawl — with explicit
  warnings not to over-enforce (Coding Militia, Milan Jovanović). **[M]**
- **Topic B (structuring code for agents) is notably *less* contested.** No
  meaningful camp argues it *doesn't* help; the practitioner mainstream agrees
  explicit domain naming, strong types, clear boundaries, and short scoped rule
  files reduce agent error and improve verifiability — and this is the area with
  empirical (type-constrained generation) support. The main skeptical thread is
  "soft instructional guardrails leak — lean on mechanical enforcement." Also note
  the CLAUDE.md guidance corollary: *keep it short or the agent ignores half of
  it* (RanTheBuilder; Claude Code docs) — which validates this repo's "keep
  CLAUDE.md short, route to canonical docs" approach. **[M]**

## 7. Honest gaps & contradictions

- **No controlled study shows "more-modularized codebase → higher agent success
  rate."** Supporting evidence is (a) types/compiler help — *strong, direct*;
  (b) explicit structure/locality help — *real but small*: RepoGraph adds only
  ~+2–3pp absolute on SWE-bench-Lite, and measures *adding tooling*, not intrinsic
  modularity (arXiv 2410.14684). **[H]**
- **Classic coupling→defect evidence is correlational** and confounded with
  size/complexity; the "best" coupling metric is inconsistent across studies
  (Parnas 1972 is normative argument, not measurement; coupling-metric studies are
  observational). **[H]**
- **Self-repair optimism vs pessimism is partly a model-generation artifact:**
  newer RL-trained models exploit execution feedback better than the 2023–24
  models in the ICLR study, so the "silver bullet" pessimism may be softening over
  time. **[M]**
- The strongest practitioner figures (60–70% fewer iterations, 67% cost increase
  from compression, 30% higher defect risk) are **unsourced blog claims** —
  directionally useful, not citable as evidence. **[L]**

---

## Implications for this repo

Reframed against the evidence, the high-value reading of "structure wins":

1. **Spend the enforcement budget on the *compiler and type system*, not on
   assembly count.** Typed boundaries (*make illegal states unrepresentable*) are
   the best-evidenced agent guardrail — the part to go hardest on. Aligns with the
   CLAUDE.md craft rule of the same name.
2. **Use assembly walls where the rule is expressible as a reference dependency**
   (`Contracts` can't see `Host`; `Core` can't see the orchestrator). There the
   compiler fails immediately and the agent can't route around it — the genuine
   edge over test-based enforcement. Matches the existing stance: *"an assembly
   boundary only where it earns enforcement or reuse."*
3. **Resist multiplying assemblies past that.** Beyond a handful you pay in build
   friction, DI/wiring ceremony, `InternalsVisibleTo` leakage — and, specific to
   agents, context/tool-call overhead from fragmentation the literature flags as a
   net negative. Each wall should map to a real seam (reuse, a wire contract, a
   deploy/compose boundary), not to hiding internals from the agent.
4. **For finer-grained conventions the compiler can't express** ("validators are
   Steps," "no `new Agent()` in composition"), **architecture tests (NetArchTest)
   are the right tool** — run as a fast pre-commit/agent-hook computational control
   (Böckeler), accepting that they fail after build. That's not a weakness to
   eliminate; it's the correct tier for rules a reference graph can't capture.

Net: "go hard on hard walls" is right *as a preference for mechanical, immediate,
compiler-level enforcement over soft prose rules* — that is where the data points.
It is wrong only if it slides into "therefore maximize the number of assemblies,"
which the same literature says costs you (and costs agents specifically) without
buying more safety.

---

## Key sources

- Type-Constrained Code Generation, PLDI 2025 — https://arxiv.org/abs/2504.09246
- Is Self-Repair a Silver Bullet?, ICLR 2024 — https://arxiv.org/abs/2306.09896
- LLM code-gen feedback (testing + static analysis) — https://arxiv.org/abs/2412.14841
- RepoGraph (repo code graph → SWE-bench) — https://arxiv.org/html/2410.14684
- Simon Brown, "Modular monolith / package by component" — https://simonbrown.je/modular-monolith/
- Mark Seemann, ploeh blog — https://blog.ploeh.dk/
- Microsoft/Ardalis, common web app architectures — https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures
- Microsoft, logical vs physical architecture — https://learn.microsoft.com/en-us/dotnet/architecture/microservices/architect-microservice-container-applications/logical-versus-physical-architecture
- Ben Morris, ArchUnit-style tests for .NET — https://www.ben-morris.com/writing-archunit-style-tests-for-net-and-c-for-self-testing-architectures/
- Building Evolutionary Architectures (Thoughtworks) — https://www.thoughtworks.com/en-us/insights/books/building-evolutionaryarchitectures-second-edition
- Smacchia/NDepend, partitioning your code base — https://www.red-gate.com/simple-talk/development/dotnet-development/partitioning-your-code-base-through-net-assemblies-and-visual-studio-projects/
- Codurance, multiple projects considered harmful — https://www.codurance.com/publications/2015/03/23/multiple-projects-in-visual-studio
- Mark Heath, assemblies vs namespaces (dissent) — https://markheath.net/post/some-thoughts-on-assemblies-versus
- Ardalis Clean Architecture — https://ardalis.com/clean-architecture-asp-net-core/
- Jimmy Bogard, Vertical Slice Architecture — https://www.jimmybogard.com/vertical-slice-architecture/
- Birgitta Böckeler, Harness Engineering for Coding Agents — https://martinfowler.com/articles/harness-engineering.html
- Armin Ronacher, Agentic Coding Recommendations — https://lucumr.pocoo.org/2025/6/12/agentic-coding/
- Tian Pan, The AI-Legible Codebase — https://tianpan.co/blog/2026-04-13-the-ai-legible-codebase
- Jeroen Van Eyck, Guardrails for Agentic Coding — https://jvaneyck.wordpress.com/2026/02/22/guardrails-for-agentic-coding-how-to-move-up-the-ladder-without-lowering-your-bar/
- Phoebe, Enforcing Architecture in an Agent-Driven Codebase — https://www.phoebe.work/blog/enforcing-architecture-in-an-agent-driven-codebase
- HN: "Modularity is what matters" — https://news.ycombinator.com/item?id=45810482
- HN: cognitive load / microservices — https://news.ycombinator.com/item?id=38793015
