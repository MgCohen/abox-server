# Flow orchestration — annotated references

Prior art for **predetermined, multi-step process/flow orchestration in app UIs**: a defined
flow drives *which screen/step runs next based on carried state + user decisions*, with
validation gates, producing a final output. This is **not** general screen routing / menu
navigation — that concern is deliberately excluded.

The reason this matters here: our flow engine is a typed step sequence with carried state,
validation gates, and a small step palette (human-input / validate / side-effect / agent),
and it must drive **two surfaces** — a foreground **wizard host** (each step binds a UI page,
advances on confirm) and a background **autonomous runner** (long-running, human input is the
exception, resumable). The question this research answers: is "one shared flow core + two
presentation hosts + navigation-derived-from-state" an established pattern, or are we
conflating things the industry keeps apart?

## The headline finding

It is an established pattern, and it's the **convergence point of five independent lineages**.
Every one of them, after a decade of iteration, arrived at the same core principle:

> **The flow is an explicit, owned, inspectable state object. Screens are dumb renderers
> driven by that state and emit events; they know nothing about siblings or "what's next."**

The mature systems also **share one flow core** across human-driven and autonomous flows —
they treat human-input vs. automated as *step kinds within one engine*, not separate engines
(Salesforce Flow's Screen vs. Autolaunched flows under one Orchestrator; BPMN user-task vs.
service-task in one model; WF/Temporal same engine + a wait primitive). So our instinct is the
industry default, not a novelty.

What got abandoned everywhere was never the principle — it was the **heavyweight, view-bound,
designer/serialized incarnations** of it.

---

## 1. Statecharts as the flow core (closest match to our design)

The "which screen shows is a pure function of flow state" idea, formalized.

- **[Statecharts in user interfaces](https://statecharts.dev/use-case-statecharts-in-user-interfaces.html)**
  — The canonical case that the view is a function of state and never owns navigation. Start here.
- **[Testable behaviour (statecharts.dev)](https://statecharts.dev/benefit-testable-behaviour.html)**
  — Why a headless machine is testable without a UI. The argument that the flow definition must be presentation-free.
- **[Using XState for UI flows — Bakken & Bæck](https://bakkenbaeck.com/tech/using-xstate-for-ui-flows)**
  — Production write-up: each state → a screen, host renders a component per state, buttons only `send` events. The most concrete "this is what you're building."
- **[The Wizard Problem — Chris Zempel](https://chriszempel.com/posts/thewizardproblem/)**
  — The single best wizard-specific essay. Argues for **flow-as-declarative-data-structure** and decomposes wizard rot into three axes (variance of *kind* / *phase* / *circumstance*). Read for *why* ad-hoc wizards rot and how flow-as-data fixes it.
- **[XState v5 announcement](https://stately.ai/blog/2023-12-01-xstate-v5)** /
  **[Introducing XState Store — tkdodo](https://tkdodo.eu/blog/introducing-x-state-store)**
  — The maintainers reframing XState toward actors **and** shipping a deliberately *lighter* tool, conceding machines are "likely overkill for most state." The honest "ceremony" caveat, from the source.
- **[Selecting an FSM library for React — Rainforest QA](https://www.rainforestqa.com/blog/selecting-a-finite-state-machine-library-for-react)**
  — A team that escaped "boolean salad" but chose **Robot over XState** on bundle size / API. Useful "we wanted the pattern, not the heavy lib" data point.
- **[Persistent serverless state machines with XState + Restate](https://www.restate.dev/blog/persistent-serverless-state-machines-with-xstate-and-restate)**
  — Same machine running **durably headless** server-side ("every transition is a durable event"). Directly relevant to our background runner surface.
- Reference impl / standard: **[XState](https://xstate.js.org/)** · **[statelyai/xstate](https://github.com/statelyai/xstate)** · **[W3C SCXML](https://www.w3.org/TR/scxml/)**.

**Why SCXML never broke through:** it's a machine *interchange format, not a human language*
("hard to read, harder to write by hand"), missing higher-level features, heavy interpreters —
survives only in telephony/IVR/embedded. Lesson: **don't adopt SCXML or a full actor framework**;
adopt the statechart *shape* (states = steps, guarded transitions = validation gates).

---

## 2. Coordinator / flow-controller pattern (navigation pulled out of screens)

The "extract flow decisions into a flow object; screens stay dumb" lineage — and how it broke.

- **[Coordinators Redux — Khanlou (2015)](https://khanlou.com/2015/10/coordinators-redux/)**
  — The origin. Diagnoses the "Massive View Controller"; screens should know nothing beyond their own data. The founding "why."
- **[Coordinators in SwiftUI — vbat.dev](https://vbat.dev/coordinators-swiftui)**
  — How the pattern **broke** when SwiftUI arrived: view concerns (`rootView`, `destination`) leaked back into the coordinator. This is *why our core must stay headless* — a headless core can't leak view concerns because it has none.
- **[NStack — John Patrick Morgan](https://johnpatrickmorgan.github.io/2021/07/03/NStack/)**
  — `NavigationLink` re-introduces the exact coupling Khanlou removed; the fix is to model the flow as an explicit owned stack. (Became the FlowStacks library.)
- **[Coordinator: backward events & passing values back](https://medium.com/@starecho/coordinator-pattern-how-to-handle-backward-events-and-passing-values-back-886f8ef2fd11)**
  — Documents the ugliest failure mode: passing a value *back* to a prior step has no natural home → hacks. A guardrail we must decide on up front.
- **[Jetpack Navigation 3 — official Android docs](https://developer.android.com/guide/navigation/navigation-3)**
  — Android's 2026 resolution, and it validates us: "**you, the developer, own the back stack — it's a simple list backed by Compose state.**" Our owned, inspectable flow state.
- **[Why a new navigation system (Nav3 rationale)](https://medium.com/@kemal_codes/jetpack-navigation-3-why-a-new-navigation-system-for-compose-13a05bd38ac7)**
  — What broke in Nav2: string routes → runtime crashes, back-stack chaos, shared-state-across-steps pain.
- **[The hidden trap of state loss in Nav3](https://medium.com/@boobalaninfo/jetpack-compose-navigation-3-the-hidden-trap-of-state-loss-and-how-to-fix-it-d3f3637fc535)**
  — Even the modern answer has a resume/state-restoration trap. Relevant to our resumability requirement.

**Convergence:** iOS's "Router owns a NavigationPath" and Android's "developer owns a
Compose-state list" are the *same idea*. **Three guardrails** this lineage demands:
1. **Accumulate all cross-step data in the core**, hand each step only its slice — never peer-to-peer between steps.
2. **Decide up front whether backward edits are supported** — make "pass a value back" a first-class core op or deliberately forbid it.
3. **Make the core's state serializable/reconstructible** — so deep-link-into / resume a half-finished flow is state-rehydration, not screen-replay. This broke the most teams.

---

## 3. Windows Workflow Foundation (the .NET precedent — copy one thing, avoid the rest)

Most stack-relevant. Bottom line: **the bookmark/idle/resume runtime model is sound and worth
copying; the designer-coupled, XAML-serialized definition model is the mistake that killed it.**

- **[Bookmarks — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/framework/windows-workflow-foundation/bookmarks)** /
  **[Pausing and Resuming a Workflow](https://learn.microsoft.com/en-us/dotnet/framework/windows-workflow-foundation/pausing-and-resuming-a-workflow)**
  — The mechanism to steal: an activity creates a *named* bookmark and returns without completing; the workflow goes **idle**, the host **persists and unloads** it (zero compute while waiting), then `ResumeBookmark(name, data)` reloads and continues — returning `Success/NotReady/NotFound`. **This is our human-input step.**
- **[Bookmarks Revisited — InformIT](https://www.informit.com/articles/article.aspx?p=680838&seqNum=3)**
  — Deeper on the persist/resume mechanics.
- **[Alternative to Windows Workflow Foundation — workflowengine.io](https://workflowengine.io/blog/alternative-to-windows-workflow-foundation/)**
  — The concrete autopsy: designer view-state stored *in the same XAML as logic* → noise commits + unmergeable; 100KB workflows take >1 min to load; meaningless error traces; unit-test friction; **no version-migration story for already-persisted long-running instances** (the most damaging omission); "I could've written this in plain C#."
- **[WF in 2017 — andreioros.com](https://andreioros.com/blog/windows-workflow-foundation-2017/)** /
  **[Windows Workflow Foundation — Wikipedia](https://en.wikipedia.org/wiki/Windows_Workflow_Foundation)**
  — The XAML dependency as the main impediment to porting to .NET Core; WF dropped at .NET 5. The designer/runtime coupling killed portability.
- **[UiPath/CoreWF](https://github.com/UiPath/corewf)** — the community port (no designer, "not an official Microsoft release"). Status, for completeness.

**Copy:** the named-bookmark suspend/persist/resume primitive + a `Success/NotReady/NotFound`
resume result. **Avoid:** any visual designer / declarative-serialized document as the *definition*
of a flow. **Design up front:** the persisted-state **versioning/migration** story for in-flight runs.

---

## 4. BPMN / human-task engines (heavyweight enterprise version — steal 3 ideas, leave the rest)

- **[Camunda — User tasks](https://docs.camunda.io/docs/components/modeler/bpmn/user-tasks/)** /
  **[Understanding human task management](https://docs.camunda.io/docs/components/best-practices/architecture/understanding-human-tasks-management/)**
  — How a process *pauses and waits for a human* as a first-class step, exposed via a **task inbox / Tasklist API** that's separate from the task UI. The clean **def/UI seam**.
- **[Human workflow — Camunda](https://camunda.com/solutions/human-workflow/)**
  — One model governs human + automated (+ AI) steps in one audit trail. The "mixed step kinds, one engine" proof.
- **[BPMN modeling vs execution — JointJS](https://www.jointjs.com/blog/bpmn-modeling-vs-execution)**
  — The spec separates *semantic* from *visual* layers (engine ignores the diagram). But vendor-specific extensions (`camunda:`/`zeebe:`/`flowable:`) make "portable XML" vendor-locked in practice.
- **[Camunda 7 vs 8 — RST Software](https://www.rst.software/blog/camunda-7-vs-camunda-8---key-differences-and-considerations-before-migration)** /
  **[Conceptual differences — Camunda docs](https://docs.camunda.io/docs/guides/migrating-from-camunda-7/conceptual-differences/)**
  — The 7→8 re-platform: the **embeddable in-process engine is gone**, replaced by distributed Zeebe over gRPC. "Not a migration in the technical sense." Teams inherited Kubernetes-grade operational tax.
- **[Licensing changes to C8 Self-Managed — Camunda forum](https://forum.camunda.io/t/important-licensing-changes-to-camunda-8-self-managed/51669)** /
  **[CIB Seven fork announcement](https://www.openpr.com/news/3769431/open-source-fork-cib-seven-now-available-as-a-real-alternative)**
  — The fallout: 8.6 requires a production license; C7 CE reached EoL (Nov 2025); the community forked C7 as **CIB Seven**, explicitly because C8 dropped the embedded use case. The cautionary "rug pull."
- **[Is Camunda overkill? — Latenode community](https://community.latenode.com/t/is-camunda-overkill-for-lightweight-automations-thinking-aloud-about-n8n-and-ai-copilot-workflow-generation/50197)** /
  **[Build or Buy a Workflow Engine — InfoQ (2009)](https://www.infoq.com/news/2009/07/WFEngine/)**
  — The "sledgehammer to crack a nut" critique, old and current. Genuine payoff is **regulatory/audit + business-analyst-authored + many human touchpoints** — none of which apply to a developer-authored engine yet.

**Steal:** (1) the **def/UI seam** via a stable task/inbox API; (2) **resumable-pause** semantics
(a step that waits days, resumes with prior input recorded, no re-approval); (3) a **single uniform
run/audit log** across human/side-effect/validation steps. **Leave on the shelf:** BPMN XML, the
visual designer, and the distributed runtime — until a non-engineer author or an auditor demands them.

---

## 5. Multi-step forms & "screen flow" platforms (web/product scale — and the sprawl warning)

- **[The Wizard Problem — Chris Zempel](https://chriszempel.com/posts/thewizardproblem/)** *(also in §1)*
  — Why hand-rolled wizards rot: the step views *become* the data structure, so variance oozes into steps as `if`s. The fix is an explicit graph with conditional edges.
- **[xstate-wizards](https://github.com/xstate-wizards/xstate-wizards)**
  — A mature flow-as-JSON-config wizard system (50–200+ question questionnaires): persistence/resume/no-code-edit fall out for free; reusable sub-flows as spawned actors; hard logic/UI package split. The durable end-state of our pattern.
- **[Multi-step form with React Hook Form — ClarityDev](https://claritydev.net/blog/build-a-multistep-form-with-react-hook-form)** /
  **[Multi-step forms with XState — mayashavin](https://mayashavin.com/articles/manage-multi-step-forms-vue-xstate)**
  — Practical "tame the wizard" patterns: top-level state container persisting across steps; components only `matches()`/`send()`.
- **[Flow sprawl is the silent killer — Equals11](https://www.equals11.com/blog/flow-sprawl-is-the-silent-killer-of-your-salesforce-org-6-signs-you-have-it)** /
  **[Salesforce Flow technical debt — Digital Mass](https://digitalmass.com/how-we-think/salesforce-flow-technical-debt-automation-sprawl-2026/)** /
  **[Flow best practices 2026 — salesforcemonday](https://salesforcemonday.com/2026/02/19/salesforce-flow-best-practices-2026/)**
  — The low-code cautionary tale: no compiler/test-gate → 30+ flows per object, "FINAL v2" names, nondeterministic ordering, undebuggable nested subflows. The 2026 fix is **re-imposing software engineering** (naming, tests, versioning, modular subflows). A code-defined flow buys most of this *by default* — but only if flows stay a **governed, named, tested, versioned catalog**.
- **[Server-Driven UI talk — InfoQ](https://www.infoq.com/presentations/sduie/)** /
  **[SDUI discussion — MobileNativeFoundation](https://github.com/MobileNativeFoundation/discussions/discussions/47)** /
  **[Fintech onboarding — Eleken](https://www.eleken.co/blog-posts/fintech-onboarding-simplification)**
  — When/why to move flow logic server-side (iterate without app-store release; KYC risk-score decides next screen) — and the sobering tradeoffs (API surface explosion, lost test/release safety net, data/layout sync bugs). **Skip server-driven dynamism unless flows must change without redeploy.**
- **[Temporal replaces state machines](https://temporal.io/blog/temporal-replaces-state-machines-for-distributed-applications)** /
  **[Human-in-the-loop — Temporal](https://temporal.io/blog/human-in-the-loop-approvals)**
  — The durable-execution argument: hand-rolled state machines accumulate plumbing (persist transitions, dispatch, timeouts) and "missing corner cases → stuck workflows." If flow state must survive process restarts, **push persistence/resume into the engine**, not each step. The modern generalization of WF bookmarks (signals/`wait_condition`).

---

## What to copy / what to avoid (cross-cutting synthesis)

**Copy / honor:**
- **Flow-as-explicit-data-structure.** A graph of steps with guarded/conditional transitions; "what shows or runs next" is computed from carried state. Not an implicit structure scattered across step views.
- **Headless, testable core.** Zero view dependency — this is what lets the *same* definition drive the wizard host and the autonomous runner, and it sidesteps the SwiftUI-coordinator leak by construction.
- **One engine, step *kinds*.** human-input / validate / side-effect / agent are step types, not separate engines (Salesforce/BPMN/WF/Temporal all do this).
- **The bookmark primitive.** Named suspend → persist & unload (zero compute) → resume-by-name with delivered data → `Success/NotReady/NotFound` result. This is the human-input step *and* the autonomous-runner's human-exception, unified — they differ only in *how the resume event is delivered*.
- **Def/UI seam + uniform run log.** Steps never hard-code presentation; every step logs through one model so "what happened on this run?" is always answerable.

**Avoid / preempt:**
- **A visual designer or declarative-serialized doc as the *source of truth*.** Killed WF; risks BPMN. Keep flows as plain typed C# (where we already are); any visual layer is an optional *view*.
- **Letting variance ooze into step views as `if`s.** Keep branch conditions as small pure functions on carried state, testable in isolation; model reusable sub-flows as child flows, not inlined branches.
- **Conflating committed/server-truth state with in-progress step state.** Keep them split so back-nav / save-draft / resume can't prematurely commit or corrupt. Validate the active step incrementally; validate the whole graph only at the final gate.
- **Skipping the persisted-state versioning/migration story.** WF's single most damaging omission for long-running runs. Design it up front for the background runner.
- **Premature heaviness.** No SCXML, no full actor framework, no BPMN runtime, no server-driven UI — a near-linear flow is a degenerate state machine and our typed `Steps` + carried context already *is* one. Add hierarchy on the second real flow that needs it.
- **Un-catalogued flow sprawl.** Treat flows as a governed, named, versioned, tested catalog from day one (the Salesforce lesson).

**Names to use when describing this to others:** a **statechart-driven flow engine** (the headless
core) with **multiple presentation hosts / flow coordinators** (wizard host + autonomous runner) —
i.e. a small **flow-orchestration** system. Salesforce's **Screen Flow vs. Autolaunched Flow under
one Orchestrator** is the closest off-the-shelf vocabulary for our two-host split.
