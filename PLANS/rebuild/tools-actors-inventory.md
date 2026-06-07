# Tools & Actors — Inventory

What the engine is made of, in the [ADR 0002](../../design/adr/0002-tools-steps-flows.md)
vocabulary: **Tools** (intent-free capabilities), **Actors** (intent + identity
that mint **Operations**, [ADR 0003](../../design/adr/0003-actors-operations-run-contract.md)),
and **Flows** (composed intent). This doc inventories the first two — what's
**built** and what's **still to build** — so the L8 tooling work has a checklist.
Flows are inventoried in the [feature map §B](01-feature-map.md).

This is a *what-exists / what's-left* ledger, not a design. Each entry cites the
requirement it satisfies; the shape rules live in the ADRs.

## The one test

- **Tool** — acts only when invoked, decides nothing, depends only on OS/BCL +
  `Contracts` (never on another of our types). Any shape, any size. May carry
  domain knowledge (vendor literals) but **never intent**. (ADR 0002 §2)
- **Actor** — a capability *with intent + identity*; mints `Operation<TArgs,TResult>`
  units the flow runs via `Flow.Run`. Owns its guards. (ADR 0003 §1, ADR 0005)

Status: **✅ built** · **🔨 to build** · **◐ placement open** (see §Open questions).

---

## Tools

| Tool | What it does | Status | Lives | Ref |
|---|---|---|---|---|
| **command-line** | One tool: `Shell`/`RunCommand` (process exec + quoting), `SubprocessSession` (codex `exec`), `PtySession` (ConPTY drive), `AnsiHelpers`. The session is the tool's guts, not a second tool. | ✅ | `Tools/CommandLine/` | A2, B1/B2 |
| **paths** | `OrchestratorPaths`, `RepoRoot` — intent-free location look-ups. | ✅ | `Tools/Paths/` | — |
| **projects** | `ProjectRegistry` — reads `projects.json`, resolves name→abs path. | ✅ | `Tools/Projects/` | FR-A1, A7 |
| **json** | `JsonLine` — JSONL line read/parse helper. | ✅ | `Tools/Json/` | A6 |
| **env-scrub** | Blanks subscription key literals on a child env. Tool by shape (decides nothing), placed with the agent for its vendor knowledge (ADR 0002 §1). | ✅ | `Actors/Agents/Claude/EnvScrub.cs` | FR-X1, A1/A3 |
| **isolation-scope** | Create + dispose a throwaway git worktree so a failed Unity compile never dirties the real tree. Intent-free: the *decision* to use it is the unity flow's. Needs a `FlowContext` child `ProjectDir` seam (L8 plan §Flagged). | 🔨 | `Tools/` (new) | FR-C8, B4 |

**SubscriptionGuard** currently sits in `Tools/CommandLine/` but *refuses to start
when API keys are set* — a refusal is a **decision**, so by ADR 0002 §5 / ADR 0003
§5 it is operation/actor policy, not a tool. Flagged below (§Open questions).

---

## Actors

Each actor mints one or more operations. The verb captures per-call inputs as
`TArgs`; `Flow.Run` injects the run-wide `FlowContext` (ADR 0005).

### Agent — ✅ built · `Actors/Agents/`

Intent: *produce work from a provider*. The richest actor family — one `Agent`
composes an `IProvider` (the `CodexProvider`/`ClaudeProvider`/`FakeProvider` seam,
[ADR 0004](../../design/adr/0004-provider-seam.md)) and mints the agent run as an
`Operation<AgentArgs, AgentOutcome>`.

- **Operation:** `agent.Run(prompt, session?)` → `AgentOutcome`
  (`Completed | NeedsInput | Faulted` — status is the type; Faulted beats
  NeedsInput). Returns last-turn text + resumable session id + transcript.
- **Roles** (`Agents` presets, minted via `IAgentFactory`, FR-N1):
  **implementer** (Claude / `acceptEdits` / ConPTY) · **reviewer** (Codex /
  read-only / `gpt-5.5`).
- **Guards:** subscription key-scrub (env-scrub + isatty), `--model gpt-5.5`-only
  codex safety, OS-aware sandbox/permission policy.
- **Resolvers:** `IDecisionResolver` (NonInteractive default; question-resolve →
  resume loop owned by the operation).
- Refs: FR-C1/C2/C3/N1, FR-X1/X2, A1–A9.

### Git — ✅ built · `Actors/Git/`

Intent: *mutate/inspect a repo working dir*, with guardrails. `Git(projectDir)`
exposes five operations as properties:

| Operation | Mints | Guard |
|---|---|---|
| `CheckDirty` | `DirtyResult` | — |
| `Diff` | `DiffResult` (text + file count) | — |
| `ChangedFiles` | `ChangedFilesResult` (porcelain paths) | — |
| `Commit` | `GitCommitResult` (sha + title) | requires explicit file list — **no `add -A`** |
| `Push` | `GitPushResult` | **no force-push to `main`/`master`** |

- Refs: FR-C7. *Parked:* per-agent git identity (env-var author/committer) is
  designed but not built at L8 — L8 plan §Open-questions.

### Validator — 🔨 to build · `Actors/Validators/` (new)

Intent: *check the work produced; report `{Ok, Summary, Errors}`*. A validator
**is** an operation (`Operation<…, ValidationResult>`) — **no `IValidator`
interface** (NG9). `ValidationResult.ToString()` → `PASSED`/`FAILED` line.

- **OrchestratorValidator** — Roslyn / project build checks. Used by
  `claude-validate` + `full-review`. (FR-C6, B2)
- **UnityValidator** — Unity batch-mode compile, run inside an **isolation-scope**
  worktree, with Unity-specific fix wording. Used by `unity-review`. (FR-C6, B4,
  A6-oracle / hooks gate at L11)
- Refs: FR-C6; ports validator *logic*, rebuilds the *shape* (becomes operations).

### Reviewer-verdict / commit-message — 🔨 to build · ◐ granularity call

The "review" itself is just the **reviewer Agent role** running; what's left are
two intent-free derivations:

- **verdict parse** — reviewer text → `Approve | Revise | Unclear`
  (`APPROVE:`/`REVISE:` prefixes, preserved exactly; Unclear ⇒ refuse commit).
  Consumed by `full-review` + `unity-review`. (FR-C4, NFR-AI1/AI3)
- **commit-message format** — truncated title + full prompt + `Reviewed by: <note>`
  + `Co-Authored-By:` trailer. Feeds `Git.Commit`. (FR-C5)

Both are *intent-free* → a small **tool** if they earn a second consumer, else a
**private helper** of the review operation (ADR 0002 §6). Decide when the review
step lands — do **not** front-load a `Reviews` actor.

---

## Coverage map (FR → home)

| FR | Capability | Home | Status |
|---|---|---|---|
| C1/C2/C3 | implementer / reviewer / session continuity | Agent | ✅ |
| N1 | named roles via factory | Agent / `IAgentFactory` | ✅ |
| C4 | verdict parse | review helper | 🔨 |
| C5 | commit message | commit-message helper | 🔨 |
| C6 | validators `{Ok,Summary,Errors}` | Validator (Orchestrator + Unity) | 🔨 |
| C7 | git ops + guardrails | Git | ✅ |
| C8 | isolation worktree | isolation-scope tool | 🔨 |
| X1 | subscription scrub | env-scrub + Agent guards | ✅ |
| X2 | anti-zombie teardown | command-line (PTY/subprocess) | ✅ |

The remaining 🔨 rows are exactly **L8 — Tooling** (impl-plan L8): validators,
isolation-scope, verdict/commit helpers. Agent + Git + the command-line substrate
are already in.

---

## Open questions (placement / identity — don't silently assume)

1. **SubscriptionGuard placement.** It's a refusal (intent) but lives under
   `Tools/CommandLine/`. Per ADR 0002 §5 / 0003 §5 a guard is actor/operation
   policy → it belongs with the agent, or stays a startup guard if it gates the
   *host* rather than an op. Resolve when L8 touches it.
2. **verdict / commit-message — tool or private helper?** Granularity rule (ADR
   0002 §6): tool on the second consumer, else private. Both currently have two
   flow consumers — lean tool, but decide at the review-step site.
3. **isolation-scope needs a `FlowContext` child-`ProjectDir` seam.** `FlowContext`
   is immutable with a private ledger; scoping the working dir to a worktree is its
   own small task when isolation lands (L8 plan §Flagged).
4. **Op-name identity (latent).** Op names are magic strings; the Agent reuses
   `config.Name`, so two agent ops in one flow collide on ledger identity. Per-call
   unique names (`"{role}:review"`) — fold in with the rest-of-L8 actors (L8 plan
   §Flagged).
