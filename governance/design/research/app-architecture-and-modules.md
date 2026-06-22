# App architecture & modules — future-state brainstorm

High-level, forward-looking thinking about the **broader environment around the orchestrator** —
not the L1→L12 spine build, but the "what is this whole thing growing into" question. Captured
from a brainstorm; **not a plan, not a committed decision.** Treat as exploration to revisit when
a second real module actually forces the contracts below to exist.

Related: [`flow-orchestration-references.md`](flow-orchestration-references.md) (the flow engine
itself), [`agno-agentos.md`](agno-agentos.md) (prior "agent OS" research).

## The question

As we build the flow/orchestration system, then evaluation/governance, then likely telemetry /
cost / analytics, the project starts to look like a bigger **environment** ("OS" / platform) made
of individual modules composed into one structure. Some modules are tightly tied to orchestration;
many are genuinely separate sub-apps that just share a home. The questions:

1. Is there an industry structure to copy?
2. How independent vs. integrable is our flow design inside a bigger system?
3. What's needed *first* to unify the whole thing?

## What "modules" actually means here

Not microkernel subsystems — mostly **UI-fronted sub-apps that are related but independent**.
Concrete examples from the product vision:

- **Flow orchestrator** + its UI (list flows, run, see events/analytics).
- **Project-setup wizard** — walks you through North Star / architecture / required files; an LLM
  validates each input against benchmarks; on approval it scaffolds files. Nothing to do with the
  orchestrator's *runtime*, but conceptually it *is* a flow (see "flow engine is substrate" below).
- **Code-standards / ADR authoring** utility — nice UI, buttons → parses back into specific files.
- **VM provisioning** console — create VMs to run flows on (a different *layer* than the flow).
- **Evals store** — a place to analyze/query evals that arrive from flows *or* other logic *or*
  recurring runs against code. Independent module with its own ingest + query.
- **Task manager** — open tests / open PRs; largely a projection over external systems (GitHub).

## The frame: a platform shell hosting vertical feature modules

The dominant industry pattern for "suite of related-but-independent sub-apps under one roof" is
the **modular monolith / vertical-slice** architecture, with a **plugin-host / micro-frontend**
shape on the UI side. (Deliberately *not* a literal microkernel — "OS" is the wrong target; the
real target is a **platform / control plane with a plugin model**.)

- A thin **platform/shell** provides what every module needs and none should reimplement: app
  frame + navigation, identity/auth, design system, config/secrets, and a short set of shared
  **substrate services**.
- Each **feature module is a self-contained vertical**: its own UI, domain logic, storage schema.
  It plugs into the shell by *declaring* what it contributes (routes, nav entries, commands, perms).
- **Modules depend on the platform, never on each other.** When two modules must interact (flows
  feed the evals store), it goes through a *published contract or event* — producer emits, consumer
  ingests, neither imports the other. This discipline is what keeps "related" from becoming "coupled."

### The plane model (each plane integrates differently)

| Plane | Modules | How it touches the core |
|---|---|---|
| **Control / execution** (kernel) | Orchestration: flows, steps, agents, providers | *Produces* the event stream |
| **Observability** | Telemetry, analytics, cost-as-measurement | *Reads* the stream, read-only, ~zero coupling |
| **Policy / governance** | Evaluation, governance, budget enforcement, permissions | *Reads* the stream **and can gate/veto** — needs an interception seam |
| **Interface / surface** | Web + local app, file viewer, task UI, wizards | *Views* projections + *issues commands* back |
| **State / substrate** | Event log, artifact/file store, run history, config catalog | The shared ground everything reads/writes |

Observability is trivial (just subscribe). Governance is the hard one (intercept, not just watch).
The file viewer / task UI / wizards are "userland applications," not peers of the kernel.

### One fork worth flagging early: in-band vs out-of-band governance

The PRD already chose *validators are Steps* (R-SPINE) — **in-band** governance composed into a
flow. Great for flow-local concerns. But system-wide governance/eval is usually **out-of-band** —
a supervisor that can halt any run regardless of flow ("org over budget → kill all agents"). These
coexist: in-band Steps for flow-specific logic, an out-of-band plane for cross-cutting org policy.
"Validators are Steps" does **not** by itself give system-wide governance — noticing this now
avoids a painful retrofit.

## Prior art to steal from

- **Backstage** (Spotify) — the closest match: a single-pane-of-glass portal assembled from
  independent plugins sharing a catalog + auth + design system. Two pieces map *directly*:
  its **Scaffolder / Software Templates** = our project-setup wizard; its **Software Catalog** =
  a shared entity registry every plugin references. Start here.
- **VS Code extension host / LSP** — stable host API, features are contributions. Our
  "better-than-cloud file viewer" is literally an extension contributing a view.
- **Super-apps** (WeChat-style) — independent mini-apps sharing identity + shell.
- **Module Federation** (micro-frontends) — for *if* modules ever need independent build/deploy.
- **Kubernetes control plane** — declarative resources + controllers + CRDs as the "add a module
  without touching the core" extensibility model.
- **OpenTelemetry GenAI semantic conventions** — free standard vocabulary for the telemetry/cost/
  analytics trace, *if/when* we build the observability plane.

## Concrete shape in our stack (.NET 10 + Blazor)

Our CLAUDE.md already says *"an assembly boundary exists only where it earns enforcement or reuse."*
A **feature module is precisely an assembly that earns its boundary** by being an independently-
comprehensible vertical. A lightweight convention (do **not** over-build into a plugin SDK):

- A module = a class library exposing **routable Razor components** + a small `IModule`:
  - `Register(IServiceCollection)` — wires its own services into DI.
  - contributes **nav entries / commands** to the shell.
  - declares **routes** (Blazor `Router` supports `AdditionalAssemblies`, so routable components
    from a referenced module assembly just work).
- The shell discovers modules at startup, calls `Register`, merges nav/routes.
- Shared substrate lives in platform assemblies the modules reference.

"Add a module" = "add an assembly that self-registers." No microservices, no broker, no plugin SDK —
a **modular monolith in one Blazor app**. Harden into a formal plugin API only when a 3rd/4th module
(or a non-team author) proves the shape. (YAGNI: it's a convention until the second use.)

## The product-vision walkthrough, sorted into substrate vs. modules

From the envisioned daily flow (home dashboard → menu of tools → Flows → run → active flows →
projects → file system + GitHub → "new feature" wizard → evals panel → task manager):

| Shared substrate (platform) | Surfaced by these modules |
|---|---|
| **Project/workspace context** (the tenant everything scopes to) | — (ambient context every tool reads) |
| **Flow engine** (sequence + validate + human-input + agent-spawn) | Flows, New-Feature wizard, recurring evals |
| **Workspace + Git** (read/write files, GitHub) | File browser, scaffolding steps, ADR/standards tool |
| **Evals/analytics store** (ingest contract + query) | Evaluations panel |
| **Scheduler** (recurring jobs) | Recurring evals, notifications |
| **Attention/inbox** (things that need me) | Active-flows-needing-input, bad-eval alerts, PR review queue |
| **Shell**: home, nav/menu, design system, identity | The home dashboard + every panel |

The modules turn out to be **mostly thin: a view + a specific configuration of a few shared
engines.** That's the healthy version — the "OS" is really ~6 substrate services + light tools.

### Key insight: the flow engine is *substrate*, not a module

The "start a new feature" wizard (write North Star → LLM validates → scaffold → next prompt →
spawn planning agent) *is the flow engine wearing a wizard skin*. So the flow engine is not "the
thing behind the Flows menu" — it's a **horizontal capability several tools render differently**:
the Flows catalog UI, the New-Feature wizard, and headless recurring evals all consume it.

Design constraint that falls out: **the flow engine must be embeddable and headless** — drivable
by any module with its own UI, runnable with no UI at all — not welded to the Flows page. (Detail
on the two-host model is in [`flow-orchestration-references.md`](flow-orchestration-references.md).)

### Two cross-cutting things that earned their abstraction (appeared 3×)

1. **"Project" as a first-class context.** "App is per project," navigate projects, files-per-
   project, features-within-project, evals-against-our-code. Project is the namespace scoping
   flows/files/evals/tasks. The New-*Project* wizard mints one. (Backstage's "entity"/catalog.)
2. **An "attention inbox."** Three moments funnel into "things that need me now": a flow paused for
   input, an eval went bad, a PR needs review. One cross-cutting surface fed by many producers —
   the home "notifications" panel and "active flows needing input" menu are the same service.

### The coupling spectrum — pick a default

1. **Shell-only** — share frame + auth + design, nothing else. *(default; e.g. project-setup ↔ VM console)*
2. **Data-plane** — read/write shared stores. *(the exception you justify per pair; e.g. flows → evals store)*
3. **Event/contract** — react to each other's published events. *(later, if needed)*
4. **Direct API calls between modules** — tightest; **avoid**, route through the platform.

## Two substrate services worth factoring early

1. **Workspace / structured file-write service.** Project-setup, ADR/standards, *and* flow outputs
   all "parse back into specific files in a repo." Same capability three times — write structured
   content to a workspace with repo/path/templating handled once.
2. **Analytics/evals store with an ingest contract + query API.** Evals arrive from flows *or* other
   logic *or* elsewhere → define an **ingestion contract** any producer writes to; the analytics
   module owns querying and doesn't know flows exist.

## What to unify *first*

1. **The shell + module contract** — how a module declares itself (routes, nav, services, perms).
   One small interface + a discovery mechanism.
2. **The platform services catalog** — an explicit, deliberately *short* list of what's platform
   (identity, workspace/file-write, analytics ingest+query, config, maybe compute/VM fabric) vs.
   module-local. Resist over-stuffing the platform.
3. **The inter-module rule, written down** (one ADR) — depend on platform not peers; cross-module
   only via published contract/event. Cheap to write, prevents the expensive mistake.

## Open forks (to resolve when this becomes real)

- **Global app with project-as-context, or one app instance per project?** The walkthrough pulls
  both ways ("app is per project" vs. "see *all* open PRs"). Likely **global shell + project as
  ambient context + a few intentionally cross-project views** — but it changes how project context
  threads through every module.
- **Is the orchestrator the privileged primary app, or just another module** that plugs into the
  shell like the rest? Decides whether flows get refactored into the module shape now or later.
- **One deployable, or do some modules ship/scale independently** (esp. VM-provisioning, half infra)?
  Assumed one Blazor app / modular monolith until a module proves it needs to be separate.
