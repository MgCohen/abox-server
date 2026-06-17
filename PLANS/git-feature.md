# Git feature — implementation plan  *(Box S2 track)*

> **Status:** Exploration / not part of the locked rebuild. The **how** for the **S2 — PR /
> Git stack** substrate of [`the-box-implementation.md`](the-box-implementation.md); builds the
> git + PR-stack capability the Box *composes* but does not own. Standalone and run-not-just-
> compiled behind its own seam (`IStackHost`), per substrate-first strategy. Grounds in
> [`design/the-box.md`](../design/the-box.md) (§4.2 two-level merge, §13 ports, §16 decisions)
> and [`research/stacked-prs.md`](../research/stacked-prs.md). Refine a task into concrete
> steps when you start it.

## Why this is its own plan

S2 is the highest *technical* risk in the Box build — rebase/merge SHA mechanics + descendant
retargeting (`research/stacked-prs.md`). It's also a first-class capability with value beyond
the Box (a real PR surface helps the current flows UI). So it gets its own ordered plan,
spike-first: **validate the unknowns before building the real adapter on top of guesses.**

## Where we are (current state)

Git already exists in the repo, split across two homes, **stubbed at the remote seam**:

- **`Domain/Git/Git.cs`** — local working-copy ops as `Operation`s: `CheckDirty`, `Diff`,
  `ChangedFiles`, `Commit`, `Push`, `Pull`. Real git via `RunCommand`, with guardrails
  (refuses force-push to `main`/`master`, requires explicit file lists). **Local only** — no
  remote/branch/PR notion.
- **`Features/Git/`** — the PR surface, almost entirely **stubbed**: `IPullRequests.List()`
  only, backed by `StubPullRequests` (three canned PRs); `MergeResult` / `PullRequestDto`
  DTOs; `PrList` / `PrOps` endpoints. Currently **5 assemblies** (`Contracts`, `Module`,
  `PrOps`, `PrList`, + `Domain.Git`).

**The gap:** no real GitHub adapter, no branch/PR CRUD, no base retarget, no merge primitive,
no stack notion. Everything that makes a *stack* possible is unbuilt.

## Scope — what S2 builds

The `IStackHost` port (`design/the-box.md` §13) and a real GitHub adapter behind it:

- Branch + PR **CRUD**; PR creation onto a **non-`main` base** (the box / parent branch).
- **Base retarget** (`PATCH` a PR's base after its parent merges).
- **Two-level merge** primitives: Level-1 **merge-commit** of a node into the box branch;
  Level-2 box → `main` final merge.
- **Cascade-rebase** primitive (`rebase --onto` + lease-force-push) for the reject/rebuild
  path — the operation B3's restack engine drives.
- Unifying the two homes (local `Domain/Git` ops + remote/PR `Features/Git`) into one coherent
  feature, with `StubPullRequests` replaced by a real adapter.

## Out of scope — owned elsewhere (don't build here)

- **approve-as-owner identity** → S5. S2 is **read-as-bot** only (`design/the-box.md` §5).
- **Agent builder / resolver** (`IPhaseBuilder`, `IResolver`) → agent frontier.
- **Node projection / `CardPayload`** → S4.
- **Orchestrator graph, state machine, persistence store** → B1 / B2. S2 keys nothing on the
  graph; GitHub is the source of truth for PR/branch facts (`design/the-box.md` §12).
- **Conflict classification + resolution** (`IConflictClassifier`, the tier ladder) → B3. S2
  ships only the *mechanical* rebase/force-push primitive the cascade calls; `rerere` /
  Mergiraf wiring lands with B3, not here.

## Decisions carried in (settled)

- **Merge-commit for Level-1** stacked merges (`design/the-box.md` §16; `research` §1). Clean
  merges keep the parent an ancestor → descendants only **retarget**, no rebase. Rebase
  cascade is reject/rebuild-only.
- **Never key state on a SHA** (`research` §1). Stable identity = PR number / branch name /
  (later) `Node` id. SHAs are rewritten by rebase/squash/amend/force-push.
- **All force-push uses `--force-with-lease --force-if-includes`** (`research` §6).
- **Read-as-bot** (`ABox-Agent`); no self-approval, no owner credentials (`design/the-box.md`
  §5; CLAUDE.md).

---

## Build order

### S2.1 — Spikes *(validate first — gates everything below)*

Retire the unknowns before building the real adapter. **Throwaway code** in `spikes/git-stack/`
(matches the existing `spikes/` home); the *kept* artifact is a verified call/command transcript
+ the gotchas that actually bit, appended to `research/stacked-prs.md` §7. Two legs, because the
risk splits by where it lives.

#### S2.1a — Git mechanics *(local, no GitHub)*

**Goal:** prove the pure-git choreography with **no remote** — this is where most of the
technical risk actually lives.

- **Prove:** (1) a **merge commit** keeps the merged phase's commits as ancestors of the box
  branch, so a descendant merges/retargets clean (no rebase); (2) `git rebase --onto
  <new-parent-tip> <old-parent-tip> <descendant>` correctly replays a descendant after the
  parent is *rebuilt* (SHAs change); (3) `--force-with-lease --force-if-includes` rejects a
  clobbering push; (4) what a **phantom diff** looks like when a base is stale, and how to
  detect "clean."
- **How:** a local temp repo — reuse `tests/Tests/Support/TempGitRepo.cs`. No network.
- **Done-when:** a 2-node local stack demonstrates clean merge-commit ancestry **and** a
  rebuild cascade-rebase, both asserted by throwaway code; findings written up.

#### S2.1b — GitHub-API choreography *(real remote)*

**Goal:** prove the GitHub-side behaviors that can't be reproduced locally.

- **Prove the call sequence (`research` §2):** `POST /pulls` with `base=<non-main>`; `PATCH
  /pulls/{n}` base retarget; `PUT /pulls/{n}/merge` with `merge_method=merge`; force-update a
  ref. **Observe** GitHub's auto-retarget/auto-close behavior when a merged head branch is
  deleted, and whether/when a force-push dismisses approvals.
- **How:** `spike/`-prefixed branches **in this repo** (no separate throwaway repo needed —
  see Open decisions); delete branches + close PRs after. Accept the CI-noise cost on junk
  branches. Drive via the GitHub MCP tools / `gh` to move fast (library choice is S2.2's call).
- **Done-when:** the full create → retarget → merge-commit → delete sequence runs end-to-end
  against real GitHub; the auto-retarget-on-delete and approval-dismissal behaviors are
  observed and recorded; the ordering that avoids the base-deleted race is confirmed.

> **S2.1 done-when (gate):** both legs proven; findings folded into `research/stacked-prs.md`;
> any decision they surface (e.g. library, race-ordering) locked or escalated. Spike code is
> deleted or left clearly under `spikes/` — never mistaken for the real adapter.

### S2.2 — Base git *(unify into a real feature)*

**Goal:** one coherent Git feature with a **real GitHub adapter** replacing the stub.

- Replace `StubPullRequests` with a real adapter behind the existing `IPullRequests` seam;
  grow that seam past list-only toward `IStackHost`.
- Unify the two homes: local `Domain/Git` ops + remote/PR `Features/Git` under one feature
  per the repo's `Domain/<Concept>` + `Features/<Feature>` pattern.
- **Sub-decision (assembly shape):** keep the current 5-assembly split, or **collapse to
  folders** per the box plan's "no new assemblies / least mechanism." *Lean: collapse* —
  flag for owner before acting (see Open decisions).
- **Done-when:** the feature lists/reads real PRs from a real repo (no canned data); local +
  remote ops live under one feature; build warning-free, tests green (as Rulebooks — use the
  `test-rulebook` skill; the GitHub-touching tests are **Live**).

### S2.3 — Stack system *(`IStackHost` — the novel part, on top)*

**Goal:** the stack primitives the Box consumes (`design/the-box.md` §13), built for real on the
spike's proven choreography.

- `IStackHost`: branch/PR CRUD, **base retarget**, **two-level merge** (L1 merge-commit into the
  box branch; L2 box → `main`), and the **cascade-rebase** primitive (`rebase --onto` +
  lease-force-push) the restack engine (B3) will drive.
- Linear stacks first; nothing assumes a single child (DAG-capable), matching `Node.parent`
  (`design/the-box.md` §7) — but **defer** sibling/merge-ordering rules (§7).
- **Done-when (mirrors S2's done-when):** a hand-made 2-branch stack opens, **merge-commits**
  one node into the box branch and **retargets** the descendant (no rebase), then a **rebuild**
  leg **cascade-rebases** the descendant onto the rewritten parent — all driven by test code
  against a real repo, no agent.

---

## Done-when bar (every task)

Same as the rebuild: warning-free build, green tests, behavior **run** (not just compiled), one
coherent commit. Kept code ships behind its seam with a fake so the next task can start; tests
land as Rulebooks (`test-rulebook` skill). Spike code is exempt — it's throwaway under `spikes/`.

## Open decisions

- **Assembly shape (S2.2):** collapse `Features/Git`'s 5 assemblies to folders, or keep the
  split? *Lean: collapse.* **Owner call** — touches solution structure.
- **GitHub client library (S2.2):** Octokit.NET vs raw `HttpClient` vs shelling `gh`. *Lean:
  Octokit.NET* (official, typed), decided at S2.2; the spike (S2.1b) is free to use `gh` / MCP
  to move fast.
- **Throwaway repo vs `spike/` branches (S2.1b):** *Decided — `spike/` branches in this repo +
  local for S2.1a.* A separate sandbox repo would need the owner (bot is scoped to this repo);
  revisit only if CI noise on junk branches becomes annoying.
- **Level-2 merge method (S2.3):** squash vs merge-commit for box → `main` is **independent** of
  the L1 decision; `main` sees one reviewed Box either way. Defer until S2.3 / B2.
- **`rerere` / Mergiraf wiring:** deferred to **B3** (conflict resolution); S2 provides only the
  rebase primitive.
