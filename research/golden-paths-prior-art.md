# Golden Paths — Prior Art & Vendor Landscape

*Companion to [`golden-paths.md`](golden-paths.md) (foundations + AI-agent research) and [`golden-paths-compilation-pipeline.md`](golden-paths-compilation-pipeline.md) (the A.Box idea, summary & insights — no code). This doc is the **competitive/prior-art map**: who builds the pieces of the proposed compilation pipeline, with snippets, use cases, and exactly where each falls short. Method: 4-way fan-out web research (MTGA engine internals · spec-driven codegen vendors · compositional program synthesis · action-catalog + dual-validation prior art), June 2026 snapshot. Vendor/tooling claims are fetched-and-claim-extracted (not 3-vote verified); academic and MTGA-engine claims are primary-sourced with URLs inline.*

---

## The pipeline being compared against

The A.Box flow has six load-bearing moves (full treatment in the [pipeline doc](golden-paths-compilation-pipeline.md)):

1. decompose intent into **phases/operations**
2. **match** each unit to a catalog of templates / golden paths
3. **re-review** the unit list
4. **compose** the output from the matched template fragments
5. validate output **against each template used** (structural)
6. validate output **against the original human intent**

The distinctive, unoccupied parts are **#2 + #4 + #5** — fragment-granular matching, composition, and per-template validation.

## Headline finding: no vendor occupies the seam

The market splits the pipeline across **non-overlapping** categories. No single tool runs the full decompose → match-code-fragment → compose → dual-validate loop.

| Pipeline capability | Who owns it best | Gap vs. the flow |
|---|---|---|
| Decompose intent → phases/tasks | Spec Kit, Kiro, Copilot Workspace, OpenSpec | Commoditized — nobody's differentiator |
| Match unit → reusable **code-fragment** catalog | **Nobody.** Scaffolders match per-*project*; Tessl Registry matches *library docs*; Goose/Cursor/Skills catalog *workflows/instructions* | No fragment-granular **code** matching |
| **Compose final code from atomic template fragments** | **Nobody** — all do whole-feature LLM authoring | The core bet, unoccupied |
| Validate output → intent | **Tessl** (test-backed), OpenSpec (`validate --strict`) | Not validated *per-template-used* |

---

## A. Spec-driven codegen family (decompose, but no fragment catalog)

These nail decomposition and spec-consistency, but "templates" are *document* templates and generation is whole-feature LLM authoring.

- **GitHub Spec Kit (`specify`)** — `/specify → /plan → /tasks → /implement`; tasks are marked parallelizable `[P]`; continuous spec-analysis for ambiguity/contradiction. No code-fragment catalog, no compositional assembly. ([spec-driven.md](https://github.com/github/spec-kit/blob/main/spec-driven.md))
- **AWS Kiro** — three-phase spec: `requirements.md → design.md → tasks.md` with dependencies; "steering" files inject standards. Validation is *pre*-generation only. ([kiro.dev/docs/specs](https://kiro.dev/docs/specs/))
- **GitHub Copilot Workspace** — task → spec → per-file plan → implement. **Discontinued May 30, 2025**, folded into Copilot Agent Mode. ([githubnext.com](https://githubnext.com/projects/copilot-workspace/))
- **OpenSpec** — strict proposal→apply→archive state machine + `openspec validate --strict` (catches missing acceptance scenarios). Strong on intent-validation, no catalog/composition. ([openspec.dev](https://openspec.dev/))

---

## B. The eight tools, explained

Each: what it is · a representative snippet (illustrative, simplified to the shape that matters) · a use case · where it sits in the pipeline.

### 1. OpenRewrite — *catalog of atomic code-mutation operations* (closest operation-level prior art)

Automated refactoring engine (Java-origin, now polyglot). Parses code to a **Lossless Semantic Tree** (full types + formatting), then runs **recipes** — small deterministic AST transforms that compose ("a single operation **or** linked with other recipes"). 500+ in the catalog.

```java
public class AddCreatedAt extends Recipe {
    @Override public String getDisplayName() { return "Add createdAt to Task"; }
    @Override public TreeVisitor<?, ExecutionContext> getVisitor() {
        return new JavaIsoVisitor<ExecutionContext>() {
            @Override public J.ClassDeclaration visitClassDeclaration(J.ClassDeclaration cd, ExecutionContext ctx) {
                if (!cd.getSimpleName().equals("Task")) return cd;
                return cd.withBody(addField(cd.getBody(), "java.time.Instant", "createdAt"));
            }
        };
    }
}
```
```yaml
type: specs.openrewrite.org/v1beta/recipe
name: com.acme.AuditFields
recipeList:
  - com.acme.AddCreatedAt
  - org.openrewrite.java.AddImport: { type: java.time.Instant }
  - org.openrewrite.staticanalysis.RemoveUnusedImports
```
**Use case:** migrate 4,000 repos JUnit 4 → 5 by composing catalog recipes that run deterministically everywhere.

**Pipeline fit:** this *is* the operation-level catalog (`add field to class`, `set field value`). **Missing:** a planner that turns *"add a timestamp to the task"* into that recipe list — that's step 4. (Sources: [recipes](https://docs.openrewrite.org/concepts-and-explanations/recipes) · [catalog](https://docs.openrewrite.org/recipes).) Note **Moderne** embeds LLMs as deterministic *tools inside* recipes — the inverse of AI-composes-recipes — evidence the obvious adjacent player has *not* taken this seam ([moderne.ai](https://www.moderne.ai/blog/ai-assisted-refactoring-in-the-moderne-platform)).

### 2. Backstage — *project-level golden-path scaffolder*

Spotify's developer portal. **Software Templates** scaffold a whole new repo from a parameter form — one template = one *project* starting point.

```yaml
apiVersion: scaffolder.backstage.io/v1beta3
kind: Template
metadata: { name: dotnet-microservice }
spec:
  parameters:
    - title: New service
      properties: { name: { type: string } }
  steps:
    - id: fetch
      action: fetch:template
      input: { url: ./skeleton, values: { name: '${{ parameters.name }}' } }
    - id: publish
      action: publish:github
      input: { repoUrl: '${{ parameters.repoUrl }}' }
    - id: register
      action: catalog:register
```
**Use case:** pick "new .NET microservice," type a name, get a fully wired repo already on the org's golden path.

**Pipeline fit:** the *coarse* end of "match unit → template" — **one template per project**, no decomposition, no output validation. The "stops at one-template-per-project" failure mode. ([backstage.io templates](https://backstage.io/docs/features/software-templates/))

### 3. Parsel — *decompose → implement-per-unit → compose-and-test* (academic, closest front-half)

Write a task as a tree of **natural-language function descriptions**; the LLM generates N candidate implementations per node; **search combinations** that pass tests/constraints.

```
collatz_recursion(n): return the Collatz sequence starting from n as a list
    is_one(n): return whether n equals 1
    next_collatz(n): if n even return n/2, else 3n+1
```
**Pipeline fit:** steps 3–6 in miniature. **Gap:** units are *freshly generated* per task, not matched against a fixed, pre-verified catalog — A.Box adds that catalog. ([arXiv:2212.10561](https://arxiv.org/abs/2212.10561))

### 4. DreamCoder / LILO — *learn the catalog instead of authoring it* (academic)

Program synthesis that **grows its own primitive library**. DreamCoder alternates **wake** (solve tasks over current primitives) → **sleep/abstraction** (compress recurring sub-programs into new named primitives).

```scheme
; round 0 primitives: + * map fold cons
; after abstraction, the library invents and names:
(define (sum xs)   (fold + 0 xs))
(define (double x) (* x 2))
; future tasks treat `sum`, `double` as single primitives
```
**LILO** modernizes it: LLM proposes solutions, **Stitch** compresses, **auto-documentation** gives each new primitive an English name + docstring — a catalog of reusable code primitives *with NL handles*.

**Pipeline fit:** the alternative to hand-authoring templates. If catalog coverage (the ~20% tail) is the bottleneck, this is how you **mine new templates from your own codebase**; the NL docstring is exactly what makes a primitive LLM-matchable. ([LILO, arXiv:2310.19791](https://arxiv.org/abs/2310.19791))

### 5. Clover — *closed-loop dual validation* (academic)

"Closed-Loop Verifiable Code Generation." Generates **code**, a **formal spec** (Dafny), and an **NL docstring**, then checks **all three are mutually consistent** — reject unless code⟷spec (formal proof), code⟷doc, and spec⟷doc agree.

```dafny
// docstring (intent): "return the absolute value of x"
method Abs(x: int) returns (y: int)
  ensures y >= 0                         // ── formal spec
  ensures y == x || y == -x
{
  if x < 0 { y := -x; } else { y := x; }  // ── code
}
```
**Pipeline fit:** steps 5–6 done right. The split matters: code⟷spec is *deterministic* (a prover) = "validate vs template"; code/spec⟷docstring is the *LLM-judge* part = "validate vs intent," the research-flagged weak link. ([arXiv:2310.17807](https://arxiv.org/abs/2310.17807); cf. **PropertyGPT** per-template invariants, [arXiv:2405.02580](https://arxiv.org/html/2405.02580v1))

### 6. Component-based synthesis (Hoogle+ / type-directed) — *typed composition is the guarantee* (academic)

Given a **type signature** + a **component library**, search for a composition that **typechecks** and satisfies examples. Illegal compositions are never enumerated — the types don't connect.

```haskell
-- goal: [a] -> (a -> Bool) -> Int
-- synthesized by composing length ∘ filter:
\xs p -> length (filter p xs)
```
**Pipeline fit:** the rigor reference for "what makes two operations connect sensibly?" → **typed input/output ports** on every operation. This is what turns "merge" into a guarantee rather than LLM glue. ([Hoogle+](https://ranjitjhala.github.io/static/hoogle_plus.pdf) · [spec-guided component synthesis, arXiv:2209.02752](https://arxiv.org/pdf/2209.02752))

### 7. Tessl — *spec is the source artifact; verify code matches intent* (vendor, closest back-half)

Spec-centric AI dev (Guy Podjarny / Snyk founder). The **spec lives in the repo as source of truth**; the agent generates code from it, generates **tests that verify code matches the spec**, and grounds library usage against a **Spec Registry** (10k+ specs).

```markdown
# task-timestamp.spec.md   ← the source artifact, not the code
## Behaviour
When a Task is created, set `createdAt` to the current UTC instant.
## Requirements
- [REQ-1] createdAt is ISO-8601, UTC
- [REQ-2] set exactly once, at creation
```
**Pipeline fit:** closest *vendor* to steps 5–6 — validates output **against intent** (test-backed) and has a registry. **But** the registry is *library-usage docs, not composable code fragments*, and generation is whole-feature, not fragment-composition. ([tessl.io](https://tessl.io/blog/how-tessls-products-pioneer-spec-driven-development/) · [docs.tessl.io](https://docs.tessl.io/use/spec-driven-development-with-tessl))

### 8. Cortex / Port — *IDPs; Backstage-class scaffolding + catalog* (vendor)

Internal Developer Platforms: project-level scaffolder + service catalog + scorecards. Port models entities as **blueprints** and exposes **self-service actions** that trigger a backend workflow.

```yaml
identifier: scaffold_service
title: Scaffold microservice
trigger:
  userInputs:
    properties: { name: { type: string }, tier: { enum: [bronze, gold] } }
invocationMethod:
  type: GITHUB
  org: acme
  repo: platform-scaffolder
  workflow: create-service.yml
```
**Use case:** developer clicks "New Service" → backend pipeline scaffolds the repo + registers it in the catalog.

**Pipeline fit:** same role as Backstage — **project-granularity, no decomposition, no validation.** The commercial instances of "scaffold one golden path per project." ([cortex.io](https://www.cortex.io/post/what-is-port))

---

## C. Low-code / no-code dataflow — the closest *working* system

Low-code node graphs (n8n, Make, Node-RED, Zapier, Power Automate, Unreal Blueprints, Apache NiFi) are **flow-based programming**: pre-built **nodes** (operations) with **typed ports**, wired output-port → input-port. **A low-code node ≈ one A.Box operation template.** The full conceptual treatment is in the [pipeline doc, §"Adjacent paradigm"](golden-paths-compilation-pipeline.md); here is the concrete code contrast.

Same decomposition of *"add a timestamp to the task"* produces two different artifacts:

**Low-code (n8n-style) — the graph is stored and an engine interprets it at runtime:**
```json
{
  "nodes": [
    { "name": "Now",      "type": "dateTime", "op": "now" },
    { "name": "SetField", "type": "set",  "params": { "field": "createdAt", "value": "={{$node.Now.iso}}" } },
    { "name": "Save",     "type": "db.update", "params": { "entity": "Task" } }
  ],
  "connections": { "Now": ["SetField"], "SetField": ["Save"] }
}
```

**A.Box — same node selection, but compiled to idiomatic source that lives in the repo:**
```csharp
public Task Create(TaskDraft draft) {
    var task = _factory.New(draft);
    task.CreatedAt = _clock.UtcNow();   // node: now → node: set-field
    _repo.Save(task);                    // node: save
    return task;
}
```

The spectrum of *how far the graph gets compiled*:

| System | What the graph becomes | Runtime needs the platform? |
|---|---|---|
| n8n / Zapier / Node-RED | stays a graph, **interpreted** live | **yes** — engine walks it forever |
| Unreal **Blueprints** | compiled to **VM bytecode** | yes — the VM |
| **OutSystems** (low-code) | generates **standard C#/.NET source** | no — real code |
| **A.Box** | AI builds graph from NL → **idiomatic source in repo** | no |

**Key takeaways** (expanded conceptually in the pipeline doc):
- **Typed ports validate the composition contract.** n8n/Blueprints make illegal connections *unrepresentable* at the port level — exactly the typed-port model A.Box should borrow for step 4.
- **Inversion of where the LLM sits.** Low-code + AI puts the LLM *inside* the graph (a runtime node). A.Box puts it *above* the graph (a build-time author that then disappears).
- **OutSystems is the closest existing thing** — low-code that emits real .NET — but the graph is hand-dragged in a closed platform; A.Box adds NL-driven graph authoring + idiomatic output into an existing hand-written repo.

---

## D. MTG Arena reality-check (the guiding analogy)

The flow's analogy: card text → parse into chunks → each maps to an atomic effect → compose → check they connect.

| Analogy claim | Reality |
|---|---|
| Atomic effect primitives, composed | **True everywhere** — the universal core idea |
| "Check they connect sensibly" | **True** — typed targets/costs/zones constrain composition (illegal states don't compose) |
| Text parsed into chunks → primitives | **Only MTG Arena does this**; OSS engines (Forge, XMage) hand-script from a primitive catalog |
| Via **regex** | **False** — MTGA uses a **PEG/formal grammar**; regex is too weak for nesting/recursion |
| Parsing English | Parsing **WotC's controlled templating grammar** — which is why it works at ~80%; the other **~20% need hand-authoring** |

**Honest framing for A.Box:** lead with primitive-composition + typecheck-the-connections (rock-solid, universal); state the parse step as "LLM → typed operation-AST," not regex over English; budget for a ~20% novel-operation tail with no template. (Sources: [WotC, *Living Breakthrough*](https://magic.wizards.com/en/news/mtg-arena/on-whiteboards-naps-and-living-breakthrough) · [MTG PEG grammar](https://soothsilver.github.io/mtg-grammar/) · [formal grammar for MTG](https://hudecekpetr.cz/a-formal-grammar-for-magic-the-gathering/) · [Forge scripting](https://github.com/Card-Forge/forge/wiki/Card-scripting-API) · [XMage HOWTO](https://github.com/magefree/mage/wiki/Development-HOWTO-Guides).)

---

## E. Where each tool sits on the pipeline

| Pipeline stage | Owner(s) | Granularity | Missing for A.Box |
|---|---|---|---|
| Decompose intent → units | **Parsel**, Spec Kit, Kiro | operation/feature | fixed catalog (units generated) |
| Catalog of code fragments | **OpenRewrite** | operation | NL→recipe planner |
| Catalog (learned, not authored) | **DreamCoder / LILO** | operation | intent-validation |
| Catalog of whole templates | **Backstage / Cortex / Port** | **project** | decomposition + validation |
| Runtime dataflow node graph | **n8n / Blueprints / OutSystems** | operation | compiles to interpreted graph, not source (OutSystems excepted); no NL author |
| Typed composition contract | **Component synthesis**, low-code ports | operation | NL front-end |
| Validate vs template (deterministic) | **Clover** (code⟷spec), **PropertyGPT** | operation | decomposition |
| Validate vs intent | **Clover** (⟷docstring), **Tessl** | feature | per-template gate |

**Bottom line:** OpenRewrite (catalog) + Parsel (decompose) + component synthesis / low-code ports (typed compose) + Clover/Tessl (dual-validate) each hold *one corner*. A.Box's bet is wiring all four into a single loop — and the two columns nobody fills are **NL-driven matching of decomposed ops to a fixed code-fragment catalog** and **composing from those fragments under a typed contract**.

---

## Sources

**MTGA engine:** WotC *Living Breakthrough* (GRE/GRP/CLIPS) · [MTG PEG grammar](https://soothsilver.github.io/mtg-grammar/) · [Hudeček formal grammar](https://hudecekpetr.cz/a-formal-grammar-for-magic-the-gathering/) · [MTG Wiki: Rules text](https://mtg.fandom.com/wiki/Rules_text) · [Forge](https://github.com/Card-Forge/forge/wiki/Card-scripting-API) · [XMage](https://github.com/magefree/mage/wiki/Development-HOWTO-Guides).

**Spec-driven vendors:** [GitHub Spec Kit](https://github.com/github/spec-kit/blob/main/spec-driven.md) · [AWS Kiro](https://kiro.dev/docs/specs/) · [Copilot Workspace](https://githubnext.com/projects/copilot-workspace/) (discontinued) · [Tessl](https://tessl.io/blog/how-tessls-products-pioneer-spec-driven-development/) · [OpenSpec](https://openspec.dev/) · [Backstage templates](https://backstage.io/docs/features/software-templates/) · [Cortex/Port](https://www.cortex.io/post/what-is-port).

**Academic:** Parsel ([2212.10561](https://arxiv.org/abs/2212.10561)) · DreamCoder/LILO ([2310.19791](https://arxiv.org/abs/2310.19791)) · Voyager ([2305.16291](https://arxiv.org/abs/2305.16291)) · Hoogle+/component synthesis ([2209.02752](https://arxiv.org/pdf/2209.02752)) · Clover ([2310.17807](https://arxiv.org/abs/2310.17807)) · PropertyGPT ([2405.02580](https://arxiv.org/html/2405.02580v1)) · CodeJudgeBench ([2507.10535](https://arxiv.org/pdf/2507.10535)) · formal-verification-from-NL ([2507.13290](https://arxiv.org/pdf/2507.13290)).

**Adjacent tooling:** [OpenRewrite recipes](https://docs.openrewrite.org/concepts-and-explanations/recipes) · [OpenRewrite catalog](https://docs.openrewrite.org/recipes) · [Moderne](https://www.moderne.ai/blog/ai-assisted-refactoring-in-the-moderne-platform) · low-code: n8n, Node-RED, Unreal Blueprints, OutSystems.
