# ADR 0002 — Tools, Steps, Flows: the engine's three layers

- **Status:** Accepted (2026-06-02).
- **Scope:** the rebuild (`/src`). Applies going forward; existing L1–L3 code aligns
  as Phase 2 moves it into the buckets.
- **Supersedes:** the implementation plan's `L4 Core primitives` / `L6`/`L7` split as
  a *structural* layering. The L-numbers survive **only as a build sequence**, not as
  namespaces. There is no `Primitives/` layer.

## Context

The plan carried a `Core primitives` layer (L4: `Shell`, `RunCommand`) distinct from
`Tooling` (L8) and `Concrete agents` (L6). Working the concept through, the
primitive/tool/substrate distinction turned out to be **descriptive, not
load-bearing**: it predicted neither the folder nor any enforced boundary. Two
findings settled it:

1. **Location ≠ category.** `EnvScrub` is a stateless static verb (looks like a
   "primitive") but carries vendor knowledge, so it lives with the agents; a folder
   named for the category never matched where things belonged.
2. **The only real axis is *intent*.** Strip statefulness (a *shape* detail, not a
   category) and what's left is one line: a thing either has a purpose of its own or
   it doesn't. That single axis collapses primitive/substrate/tool into **one**
   layer and explains every placement.

This extends ADR 0001 §1 (Kinds → Definitions → Instances) to the whole engine: the
**Kinds** are exactly Tools, Steps, and Flows.

## Decision

### 1. Three layers, one engine

The engine (`RemoteAgents`) is organized as three buckets plus the `Contracts` leaf:

- **Tools** — intent-free capabilities.
- **Steps** — units of work *with* intent; implement `IStepHandler<T>`.
- **Flows** — composed intent; the flow framework + the recipes.

`L1`'s generic infra (`Paths`, `ProjectRegistry`) is **Tools** — intent-free
look-ups. There is no separate "infra" or "primitives" bucket.

### 2. Tool — the rules

A **Tool** is a capability with **no intent**: it acts only when invoked and decides
nothing on its own.

- **Depends only on the OS/BCL and `Contracts`** — *never* on another of our types
  (no Step, no Flow, no other Tool). Tools are independent leaves; the graph is
  acyclic by construction.
- **Any shape** — static class, injected service, or `IAsyncDisposable` resource.
  Statefulness is an implementation detail, not a category.
- **Any size** — one class or a folder of many. `Git` can be a whole folder and still
  be "one tool." `RunCommand` + `SubprocessSession` are **one** tool (command-line):
  the session is the tool's guts, not a separate dependency.
- **May carry domain knowledge, never intent.** `EnvScrub` knows the subscription key
  literals but decides nothing — it blanks what it's told to. Still a tool.

The "do any tools depend on tools?" question resolved to **no**: the things that
appeared to (Git, validators, gh) are **Steps**, and the one shared substrate —
running a process — *is* a tool (command-line), whose internal machinery is not a
second tool. So the invariant is clean and unqualified: **tools are independent.**

### 3. Step — the rules

A **Step** is a unit of work that **has intent** — the flow tracks it.

- **Implements `IStepHandler<T>`** (`Name` + `RunAsync(FlowContext, ct)`); the result
  owns its display via `ToString()` (per the L3 step base).
- **Uses many tools**; **does not use other steps.** Composition is the flow's job
  (ADR 0001 §2; "no agent-calls-agent"). *Asterisk:* composite steps — a step that
  sequences sub-steps — are deferred to flow-implementation time (impl-plan L10) and
  are the one sanctioned exception (a composite is a mini-flow wearing a step's
  interface).
- **Owns its guards/policy** (see §5).
- **Agents are Steps.** An agent's intent is "produce work from this provider"; it
  drives the terminal + file tools and implements `IStepHandler<AgentResult>`
  directly. No privileged agent layer — just the richest step family.

### 4. Flow — the rules

A **Flow** is composed intent. The flow **framework** (`Flow`, `FlowContext`,
`SnapshotStream`, registry, catalog, history, SSE) and the **recipes** (`claude-only`,
`claude-validate`, `full-review`, `unity-review`) share the bucket, split by
namespace (`Flows/` vs `Flows/Recipes/`), not by a fourth layer. Framework ≠
implementation (R-ARCH-1) is honored within the bucket.

### 5. Guards are step policy, not tool walls

A safety rail — "no force-push to `main`" (FR-C7), "refuse on API key set"
(`SubscriptionGuard`) — is a **decision**, so it lives with **intent**, in the Step.
The command-line tool stays generic (it runs what it's handed). The guarantee is
enforced by **one path per capability** (all git goes through Git steps; nobody
scatters raw `git push --force` through the command-line tool) — reviewable, same
philosophy as R-SPINE-1, escalated to an analyzer only if someone actually bypasses
it. Contrast the *intent-free* `EnvScrub` (blanks keys, no refusal), which is a tool.

### 6. Granularity rule

A shared intent-free helper (git-arg-build, verdict-parse, jsonl-resolve) is a small
**tool** when it has multiple consumers, or a **private helper of its step** when it
has one. Either way it's a leaf and the invariants hold — decide per case, it is not
a structural question.

## Consequences

- **`Primitives/` is deleted as a concept.** Shell, RunCommand, terminal, file,
  paths, project-registry, env-scrub are all Tools.
- **The L-numbers are build order only.** We still build the command-line tool before
  the steps that need it, and the terminal/ConPTY tool last (it stays the riskiest
  port). Killing the *layer* changes no build *sequence*.
- **The invariant set is small and testable by reading a file:** a Tool that imports
  a Step/Flow is the smell; a Step that constructs another Step is the smell; a guard
  in a tool is the smell.
- **Folders match categories** (the prototype's grab-bag `Primitives/` was the
  anti-pattern this avoids).

## Alternatives considered

- **Keep `Primitives/` as a layer** (the original plan) — a category that predicts no
  boundary and no folder; the prototype's grab-bag version is exactly what produced
  the `EnvScrub`/`SubprocessSession` mislabeling. Rejected.
- **A blind/opinionated × stateless/stateful 2×2** — a useful *thinking* tool for
  picking a tool's shape, but a bad *foldering* tool; "intent" subsumes it in one
  axis. Kept as a design lens, rejected as structure.
- **A separate `Agents/` top-level layer** — agents satisfy `IStepHandler<T>`; a
  privileged layer adds a category the interface already expresses. They live under
  `Steps/Agents/` as a family. Rejected as a top-level.
