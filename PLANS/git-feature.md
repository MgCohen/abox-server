# Git feature — implementation plan  *(Box S2 track)*

> **Status:** Exploration / not part of the locked rebuild. **Self-contained — readable
> without prior context.** This is the **how** for the **S2 — PR / Git stack** substrate of
> [`the-box-implementation.md`](the-box-implementation.md): it builds the git + pull-request
> capability that the larger "Box" feature *composes* but does not own. Standalone and
> run-not-just-compiled behind its own seam. Grounds in the canonical feature shape
> ([ADR 0011](../design/adr/0011-canonical-feature-slice-shape.md)) with
> [`src/Features/Projects`](../src/Features/Projects) as the worked reference, plus
> [`design/the-box.md`](../design/the-box.md) and [`research/stacked-prs.md`](../research/stacked-prs.md).

## Orientation (for a cold reader)

- **The Box** is a planned feature: a "stream of work" whose phases land as a **stack of
  dependent pull requests** on a per-Box integration branch, reviewed bottom-up, then merged to
  `main` as one final PR. It is built on top of independent **substrate** capabilities; this
  doc is one of them — **S2**, the git/PR mechanics. (Full design: `design/the-box.md`.)
- **A stacked PR** is a PR whose base is *another branch under review*, not `main`
  (`phase-2 → phase-1 → box-branch`). Merging one rewrites or moves what's beneath it, so the
  rest of the stack must be **retargeted** (point a PR at a new base) or **rebased** (replay
  commits onto a new base). Why that's tricky, and what we settled, is in `research/stacked-prs.md`.
- **The repo's feature pattern** (what "build a feature" means here): a vertical slice is
  `Domain/<Concept>` (the model/ports, a deeper band) + `Features/<Feature>` (the HTTP slice —
  endpoints, contracts, DI module). Dependency direction is **Features → Domain → Infrastructure;
  nothing → Host; `Contracts` is a leaf** — enforced by ArchTests over namespaces
  (`tests/Tests/Arch`), not prose. The **canonical shape** is fixed by **ADR 0011** and the
  cleanest example is **`Features/Projects`**; both are the template this plan conforms to.

## Why this is its own plan

S2 is the highest *technical* risk in the Box build — rebase/merge SHA mechanics + descendant
retargeting (`research/stacked-prs.md`). It is also a first-class capability with value beyond
the Box (a real PR surface helps the current flows UI). So it gets its own ordered plan,
**spike-first**: validate the unknowns before building the real adapter on top of guesses.

## Where we are (current state)

Git already exists in the repo, in two bands, **stubbed at the remote seam**:

- **`Domain/Git/Git.cs`** *(Domain band — stays)* — local working-copy ops as `Operation`s:
  `CheckDirty`, `Diff`, `ChangedFiles`, `Commit`, `Push`, `Pull`. Real git via `RunCommand`,
  with guardrails (refuses force-push to `main`/`master`, requires explicit file lists). **Local
  only** — no remote / branch / PR notion.
- **`Features/Git/`** *(Features band — non-canonical)* — the PR slice, almost entirely
  **stubbed**: `IPullRequests.List()` only, backed by `StubPullRequests` (three canned PRs);
  `MergeResult` / `PullRequestDto` DTOs; `PrList` / `PrOps` endpoints on **Minimal API**.
  **4 assemblies** (`Contracts`, `Module`, `PrList`, `PrOps`) — ADR 0011 says this must become 2.

**The gap:** no real GitHub adapter, no branch/PR CRUD, no base retarget, no merge primitive, no
stack notion — and the Features slice doesn't yet match the canonical shape.

## Scope — what S2 builds

The `IStackHost` capability (`design/the-box.md` §13) and a real GitHub adapter behind it:

- Branch + PR **CRUD**; PR creation onto a **non-`main` base** (the box / parent branch).
- **Base retarget** (`PATCH` a PR's base after its parent merges).
- **Two-level merge** primitives: Level-1 **merge-commit** of a node into the box branch;
  Level-2 box → `main` final merge.
- **Cascade-rebase** primitive (`rebase --onto` + lease-force-push) for the reject/rebuild path —
  the operation B3's restack engine drives.
- Bringing `Features/Git` to the **canonical shape** (ADR 0011) with `StubPullRequests` replaced
  by a real adapter.

## Out of scope — owned elsewhere (don't build here)

- **approve-as-owner identity** → S5. S2 is **read-as-bot** only (`design/the-box.md` §5).
- **Agent builder / resolver** (`IPhaseBuilder`, `IResolver`) → agent frontier.
- **Node projection / `CardPayload`** → S4.
- **Orchestrator graph, state machine, persistence store** → B1 / B2. GitHub is the source of
  truth for PR/branch facts; S2 keys nothing on a private graph (`design/the-box.md` §12).
- **Conflict classification + resolution** (`IConflictClassifier`, the tier ladder, `rerere` /
  Mergiraf wiring) → B3. S2 ships only the *mechanical* rebase/force-push primitive.

## Decisions carried in (already settled — do not re-litigate)

- **Canonical feature shape is ratified (ADR 0011).** FastEndpoints (D1), per-feature assemblies
  (D2: one impl + one `Contracts` leaf), `internal sealed` endpoints (D3). Git's consolidation
  and Minimal-API → FastEndpoints port is **not an open choice** — it is the planned migration in
  [`08-vsa-feature-template.md`](rebuild/08-vsa-feature-template.md) Gate 5 (`Flows → Git → Tasks`),
  done behind a Wire-level behavior-parity gate. Reference impl: `Features/Projects`.
- **Merge-commit for Level-1** stacked merges (`design/the-box.md` §16; `research` §1). Clean
  merges keep the parent an ancestor → descendants only **retarget**, no rebase. Rebase cascade
  is reject/rebuild-only.
- **Never key state on a SHA** (`research` §1). Stable identity = PR number / branch name /
  (later) `Node` id.
- **All force-push uses `--force-with-lease --force-if-includes`** (`research` §6).
- **Read-as-bot** (`ABox-Agent`); no self-approval, no owner credentials (`design/the-box.md` §5;
  CLAUDE.md).

## The canonical shape we build toward (ADR 0011)

```
src/Features/Git/
  ABox.Git.csproj                 ← ONE implementation assembly (the whole feature)
    <Verb>/<Verb>Endpoint.cs        each use-case a FOLDER; `internal sealed … : Endpoint<,>`
    Module/GitModule.cs             DI registration + `static Assembly EndpointsAssembly`
  Contracts/
    ABox.Git.Contracts.csproj       ← leaf: request/response/DTO only, zero deps
```

Mirrors `Features/Projects` exactly: `ProjectsModule` exposes `EndpointsAssembly =>
typeof(ListProjectsEndpoint).Assembly`; `AddProjectEndpoint` is `internal sealed … :
Endpoint<CreateProjectRequest, ProjectDto>`; `ProjectDto` is a zero-dep record in `Contracts`.
`Domain/Git` stays in the Domain band — it is **not** merged into the Features slice (that would
invert the layer direction).

---

## Build order

### S2.1 — Spikes *(validate first — gates everything below)*

Retire the unknowns before building the real adapter. **Throwaway code** in `spikes/git-stack/`
(matches the existing `spikes/` home — like the Gate-1 spike ADR 0011 cites); the *kept* artifact
is a verified call/command transcript + the gotchas that actually bit, appended to
`research/stacked-prs.md` §7. Two legs, because the risk splits by where it lives.

#### S2.1a — Git mechanics *(local, no GitHub)*

**Goal:** prove the pure-git choreography with **no remote** — where most of the risk lives.

- **Prove:** (1) a **merge commit** keeps the merged phase's commits as ancestors of the box
  branch, so a descendant retargets clean (no rebase); (2) `git rebase --onto <new-parent-tip>
  <old-parent-tip> <descendant>` correctly replays a descendant after the parent is *rebuilt*
  (SHAs change); (3) `--force-with-lease --force-if-includes` rejects a clobbering push; (4) what a
  **phantom diff** (stale base) looks like, and how to detect "clean."
- **How:** a local temp repo — reuse `tests/Tests/Support/TempGitRepo.cs`. No network.
- **Done-when:** a 2-node local stack demonstrates clean merge-commit ancestry **and** a rebuild
  cascade-rebase, both asserted by throwaway code; findings written up.

#### S2.1b — GitHub-API choreography *(real remote)*

**Goal:** prove the GitHub-side behaviors that can't be reproduced locally.

- **Prove the call sequence (`research` §2):** `POST /pulls` with `base=<non-main>`;
  `PATCH /pulls/{n}` base retarget; `PUT /pulls/{n}/merge` with `merge_method=merge`; force-update
  a ref. **Observe** GitHub's auto-retarget/auto-close behavior when a merged head branch is
  deleted, and whether/when a force-push dismisses approvals.
- **How:** `spike/`-prefixed branches **in this repo** (no separate throwaway repo needed — see
  Open decisions); delete branches + close PRs after. Accept the CI-noise cost on junk branches.
  Drive via the GitHub MCP tools / `gh` to move fast (library choice is S2.2's, not the spike's).
- **Done-when:** the full create → retarget → merge-commit → delete sequence runs end-to-end
  against real GitHub; the auto-retarget-on-delete and approval-dismissal behaviors are observed
  and recorded; the ordering that avoids the base-deleted race is confirmed.

> **S2.1 done-when (gate):** both legs proven; findings folded into `research/stacked-prs.md`; any
> decision they surface (e.g. client library, race-ordering) locked or escalated. Spike code is
> deleted or left clearly under `spikes/` — never mistaken for the real adapter.

### S2.2 — Base git *(canonical shape + real adapter)*

**Goal:** bring `Features/Git` to the canonical ADR 0011 shape **and** replace the stub with a
real GitHub adapter. This *is* the Git slot of the doc-08 Gate-5 migration — do them together so
Git is ported once, not twice.

- **Migrate to canonical shape:** Minimal API → FastEndpoints `Endpoint<,>` classes;
  consolidate 4 assemblies → 2 (impl + `Contracts` leaf); endpoints `internal sealed`; `GitModule`
  exposes `EndpointsAssembly`. Copy the `Features/Projects` template verbatim.
- **Real adapter:** replace `StubPullRequests` with a real GitHub reader behind the existing
  `IPullRequests` seam; grow that seam past list-only toward the `IStackHost` operations.
- **Behavior-parity gate (required by doc 08):** a **Wire** test (`WebApplicationFactory` booting
  real `Program`) proves the existing HTTP behavior is unchanged across the framework port — the
  rebuild's prime directive is "the user can't tell the difference."
- **Done-when:** the feature lists/reads real PRs from a real repo (no canned data); the slice
  matches the canonical shape; build warning-free; tests green as Rulebooks (use the
  **`test-rulebook`** skill — GitHub-touching tests are **Live**, the parity test is **Wire**);
  one coherent commit.

### S2.3 — Stack system *(`IStackHost` — the novel part, on top)*

**Goal:** the stack primitives the Box consumes (`design/the-box.md` §13), built for real on the
spike's proven choreography and the canonical S2.2 base.

- `IStackHost`: branch/PR CRUD, **base retarget**, **two-level merge** (L1 merge-commit into the
  box branch; L2 box → `main`), and the **cascade-rebase** primitive (`rebase --onto` +
  lease-force-push) the restack engine (B3) will drive.
- Linear stacks first; nothing assumes a single child (DAG-capable), matching `Node.parent`
  (`design/the-box.md` §7) — but **defer** sibling/merge-ordering rules (§7).
- **Done-when (mirrors S2's done-when):** a hand-made 2-branch stack opens, **merge-commits** one
  node into the box branch and **retargets** the descendant (no rebase), then a **rebuild** leg
  **cascade-rebases** the descendant onto the rewritten parent — all driven by test code against a
  real repo, no agent; tests land as Rulebooks.

---

## Done-when bar (every task)

Same as the rebuild: warning-free build, green tests, behavior **run** (not just compiled), one
coherent commit. Kept code ships behind its seam with a fake so the next task can start; tests
land as Rulebooks (`test-rulebook` skill). Spike code is exempt — throwaway under `spikes/`.

## Open decisions

- **GitHub client library (S2.2):** Octokit.NET vs raw `HttpClient` vs shelling `gh`. *Lean:
  Octokit.NET* (official, typed). This is **feature code, not the enforcement surface**, so ADR
  0012's zero-dependency rule does not apply (that rule governs fail-open *guards*, not product
  dependencies) — but flag the new dependency for the owner. Decide at S2.2; the spike (S2.1b) may
  use `gh` / MCP regardless.
- **`IStackHost` port placement (S2.3):** the Box **Orchestrator lives in `Domain/Box`** and will
  consume `IStackHost`, so the *port* must sit in the Domain band (or a contracts leaf) to keep
  the **Features → Domain** direction the ArchTests enforce — while the GitHub *adapter* is the
  Features/Infrastructure implementation. Today `IPullRequests` lives in `Features/Git/Contracts`
  because only endpoints consume it; confirm the right home when the Orchestrator becomes a
  consumer. (Not a blocker for S2.1/S2.2.)
- **Throwaway repo vs `spike/` branches (S2.1b):** *Decided — `spike/` branches in this repo +
  local for S2.1a.* A separate sandbox repo would need the owner (the bot is scoped to this repo);
  revisit only if CI noise on junk branches becomes annoying.
- **Level-2 merge method (S2.3):** squash vs merge-commit for box → `main` is **independent** of
  the L1 decision; `main` sees one reviewed Box either way. Defer until S2.3 / B2.
- **`rerere` / Mergiraf wiring:** deferred to **B3** (conflict resolution); S2 provides only the
  rebase primitive.

## References

- Canonical feature shape: [ADR 0011](../design/adr/0011-canonical-feature-slice-shape.md) ·
  migration plan + gates: [`rebuild/08-vsa-feature-template.md`](rebuild/08-vsa-feature-template.md) ·
  HTTP boundary: [ADR 0009](../design/adr/0009-fastendpoints-http-boundary.md) ·
  dependency budget: [ADR 0012](../design/adr/0012-dependency-budget-by-failure-mode.md)
- Reference implementation: [`src/Features/Projects`](../src/Features/Projects) ·
  bands + layer rules: [`PLANS/architecture-vsa.md`](architecture-vsa.md), `tests/Tests/Arch`
- Box design: [`design/the-box.md`](../design/the-box.md) (§4.2, §7, §12, §13, §16) ·
  prior art + the spike sequence: [`research/stacked-prs.md`](../research/stacked-prs.md)
