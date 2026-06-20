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
  **stubbed**, on **Minimal API**: `PrListEndpoint` (`GET /git/prs`, `IPullRequests.List()`) and
  `MergeEndpoint` (`POST /git/prs/{number}/merge` → a stub `MergeResult`), both backed by
  `StubPullRequests` (three canned PRs: 101/102/99); `MergeResult` / `PullRequestDto` DTOs.
  **4 assemblies** (`ABox.Git.Contracts`, `ABox.Git.Module`, `ABox.Git.PrList`, `ABox.Git.Merge`)
  — ADR 0011 says this must become 2. Note: `Features/Git/Contracts` is *already* a correctly
  shaped Contracts leaf (Arch rules.md calls it "the first per-feature leaf"); only the impl side
  needs the 4→1 consolidation + framework port.

**The gap:** no real GitHub adapter (the merge endpoint is a stub, not a real git merge), no
branch/PR CRUD, no base retarget, no stack notion — and the Features impl doesn't yet match the
canonical shape.

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
- **Read-as-bot** (`ABox-Agent`); no self-approval, no owner credentials (`design/the-box.md` §5;
  CLAUDE.md).
- **Force-push lean (now empirically grounded, S2.1a):** use `--force-with-lease --force-if-includes`
  (`research` §6/§9 — lease *alone* was clobbered by a background fetch in the spike). Still a lean,
  not an ADR; **today `Domain/Git/Git.cs` PushOp (line 81) adds only `--force-with-lease`** — S2.3
  must add `--force-if-includes` when it wires the cascade. Don't treat it as already-true.

## Preconditions (escalate to owner — block S2.2; S2.1 is done)

These are owner-only and unspecified today; S2.1 needed none of them (S2.1b ran on the
authenticated GitHub MCP tools, which proved sufficient for create/merge/retarget but **not** for
ref deletion — see below).

- **Delete-ref capability (surfaced by S2.1b).** Branch/ref deletion is **impossible from this
  environment**: the `origin` proxy `403`s on delete refspecs, the GitHub MCP server has no
  delete-ref tool, and there's no `gh`. The real adapter (S2.2b) and any cleanup path need delete
  rights wired. **Immediate owner action:** delete the leftover spike refs `spike/box-x`,
  `spike/phase-1`, `spike/phase-2` (PRs #63/#64 already closed; only refs linger).
- **GitHub authentication.** No credential/auth wiring exists anywhere (`grep` over `src/` for
  `GITHUB_TOKEN`/`Octokit`/`gh auth` → nothing), and the repo remote here is a local proxy, not
  real github.com. Any GitHub API call (S2.1b spike, S2.2 real adapter) needs the bot's auth
  established first: token/App, scope, where it's stored, and the **owner/repo target**. Identity
  is load-bearing (`design/the-box.md` §5); credential provisioning is **owner-only** — escalate.
- **Protected-path edits for the canonical migration.** Making Git conformant trips two
  *critical* protected-path files that carry staleness checks (fail the build if Git is conformant
  *and* still listed): `tests/Tests/Arch/Support/EndpointConformance.cs`
  (`PendingFastEndpointsMigration`) and `tests/Tests/Structure/Support/FeatureShape.cs`
  (`PendingConsolidation`) — both list `{ "Flows", "Git", "Tasks" }`, both under `critical |
  @MgCohen` in `governance/protected-paths`. Removing `Git` from them is a **reviewed PR to the
  owner**, not a self-merge (CLAUDE.md: stop at the permission wall, ask the owner). S2.2 cannot
  reach green without it.
- **Box abort/discard mechanic.** `the-box.md` §16 says "specify **before S2**." It is a
  branch-delete-of-the-box-branch + `.box/` drop — the *mechanic* falls inside S2's branch/PR CRUD
  (S2.3); the *lifecycle trigger* is B-tier. **Deferral (reasoned):** S2.3's branch-delete CRUD
  covers the mechanic; the trigger waits for the Box lifecycle. Confirm with owner that this
  satisfies the §16 "specify before S2" obligation.

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

### S2.1 — Spikes  ✅ **COMPLETE (2026-06-17)** *(gated everything below)*

Retire the unknowns before building the real adapter. **Throwaway code** in `spikes/git-stack/`
(matches the existing `spikes/` home); kept **out of `ABox.slnx`**. The kept artifacts are
`spikes/git-stack/FINDINGS-local.md` + `FINDINGS-github.md` and the consolidated
`research/stacked-prs.md` **§9**. Two legs, because the risk splits by where it lives.

> **Result:** both legs proven. The central bet — **merge-commit → clean retarget, no rebase** — is
> confirmed locally *and* live on the GitHub API (PR #64 diff byte-identical before/after retarget,
> no phantom). New facts that change downstream tasks are inlined below and in research §9.

#### S2.1a — Git mechanics *(local, no GitHub)*

**Goal:** prove the pure-git choreography with **no remote** — where most of the risk lives.

- **Prove:** (1) a **merge commit** keeps the merged phase's commits as ancestors of the box
  branch, so a descendant retargets clean (no rebase); (2) `git rebase --onto <new-parent-tip>
  <old-parent-tip> <descendant>` correctly replays a descendant after the parent is *rebuilt*
  (SHAs change); (3) `--force-with-lease --force-if-includes` rejects a clobbering push; (4) what a
  **phantom diff** (stale base) looks like, and how to detect "clean."
- **How:** a local temp repo — reuse `tests/Tests/Support/TempGitRepo.cs`. No network.
- ✅ **Done — all 4 proven** (`FINDINGS-local.md`). Two facts to carry into S2.3: (a)
  **`--force-if-includes` is mandatory** — lease *alone* was empirically clobbered by a background
  fetch; `Domain/Git/Git.cs` PushOp (line 81, lease-only) must add it. (b) The reliable "clean"
  oracle is **`merge-base --is-ancestor <base-tip> <head>`**, not a three-dot diff (which leaks a
  phantom on a *rewritten* base).

#### S2.1b — GitHub-API choreography *(real remote)*

**Goal:** prove the GitHub-side behaviors that can't be reproduced locally.

- **Blocked on:** the GitHub-auth precondition above (no credentials wired today).
- **Prove the call sequence (`research` §2):** `POST /pulls` with `base=<non-main>`;
  `PATCH /pulls/{n}` base retarget; `PUT /pulls/{n}/merge` with `merge_method=merge`; force-update
  a ref. **Verify-don't-rely** on GitHub's auto-retarget/auto-close behavior on head-branch delete
  (`research` §7 flags native auto-cascade as *unverified* — drive the retarget explicitly via
  `PATCH` regardless of what auto-behavior is observed); record whether/when a force-push dismisses
  approvals.
- **How:** *target repo is an Open decision below* — a sandbox repo (per the authoritative
  box-impl plan) vs `spike/`-prefixed branches in this repo; needs owner sign-off either way.
  Delete branches + close PRs after. Drive via the GitHub MCP tools / `gh` (library choice is
  S2.2's, not the spike's).
- ✅ **Done — happy path proven live** (`FINDINGS-github.md`, on `MgCohen/abox-server` `spike/`
  branches, `main` untouched): create-PR-onto-non-`main`-base, `merge_method=merge`, and pointer-only
  base retarget all confirmed; PR #64's diff stayed clean (no phantom). New facts: `mergeable_state:
  unstable` = checks *pending*, not a conflict (≠ `dirty`); auto-delete-head-on-merge is **off** here.
  ❓ **Not testable here** (carry forward, stay verify-don't-rely): auto-retarget/close on base
  *delete* and approval-dismissal — branch deletion was blocked (proxy `403`, **no MCP ref-delete
  tool**, no `gh`). Orchestrator must **always explicitly retarget**, never rely on auto-behavior.
- ⚠️ **Surfaced for S2.2/S2.3:** the current toolset has **no delete-ref capability** — the real
  adapter and any branch cleanup will need delete rights wired (see Preconditions). It also left 3
  `spike/*` branches that need **owner** deletion.

> **S2.1 done-when (gate):** both legs proven; findings folded into `research/stacked-prs.md`; any
> decision they surface (e.g. client library, race-ordering) locked or escalated. Spike code is
> deleted or left clearly under `spikes/` — never mistaken for the real adapter.

### S2.2 — Base git *(two sub-steps: framework port, THEN real adapter)*

> **Why split (review C1):** a behavior-parity *port* and a stub→real *behavior change* are
> mutually exclusive — you cannot prove "HTTP behavior unchanged" in the same commit that swaps
> canned PRs for real ones. So the canonical migration (parity-gated, over the stub) and the real
> adapter (a deliberate behavior change, Live-tested) are **separate steps**.

> **Ordering vs doc-08 (review M3):** doc-08 Gate 5 says "**Flows first**" — because Flows is the
> representative spike (SSE + `Shared/FlowMapping` + a `Module` with real catalog-build logic; the
> canonical shape has "no Module-with-logic slot"). Git's `GitModule` is logic-light (DI only, no
> catalog build), so Git-before-Flows forfeits none of that de-risking — **but** confirm with the
> owner before reordering, since doc-08's stated order is Flows→Git→Tasks.

#### S2.2a — Canonical framework port *(parity-gated, stub stays)*  ✅ **DONE (2026-06-17, pending owner review)**

> **Result:** Git ported to FastEndpoints `internal sealed` endpoints, 4 assemblies → 2; behavior
> unchanged, proven by a byte-level Wire characterization test (`GitPrsWireTests`). Independently
> verified: `dotnet build` warning-free, `ABox.Tests` 209✓/12 skip, Meta parity 6✓. Touches 4
> protected files (ABox.slnx + Wire rules.md + the 2 guard lists) — **awaiting owner CODEOWNERS
> review to merge to `main`**. The 415-on-typed-request gotcha was solved with
> `EndpointWithoutRequest` + `Route<int>`; the custom `{error}` 404 uses the arbitrary-object send.

- **Migrate to canonical shape:** Minimal API → FastEndpoints `Endpoint<,>` classes for **both**
  `PrListEndpoint` and `PrMergeEndpoint`; consolidate 4 assemblies → 2 (impl + `Contracts` leaf);
  endpoints `internal sealed`; `GitModule` exposes `EndpointsAssembly`; wire it into
  `Composition.cs`'s `o.Assemblies` list. Copy the `Features/Projects` template verbatim. **Keep
  `StubPullRequests`** — behavior must not change here.
- **Behavior-parity gate (required by doc-08; baseline does not exist yet):** there is **no**
  existing Wire test of the Git PR endpoints — so **first** write a **Wire** characterization test
  (`WebApplicationFactory` booting real `Program`) capturing the *current* stub behavior
  (`GET /git/prs` → the 3 canned PRs; `POST /git/prs/{n}/merge` → `{number,"merged"}` / 404),
  then port, and prove the response is byte-identical before/after.
- **Protected-path gate:** removing `Git` from `EndpointConformance.PendingFastEndpointsMigration`
  and `FeatureShape.PendingConsolidation` is the **owner-PR precondition** above — it lands *with*
  this step but as a reviewed change.
- **Done-when:** Wire parity green; slice matches the canonical shape; warning-free; one commit.

#### S2.2b — Real GitHub adapter *(deliberate behavior change, Live-tested)*

- Replace `StubPullRequests` with a real GitHub reader behind the `IPullRequests` seam (lib per
  Open decisions; auth per Preconditions); grow the seam toward `IStackHost` operations.
- **Done-when:** the feature lists/reads **real** PRs from the target repo (no canned data); new
  behavior covered by **Live** tests as Rulebooks (`test-rulebook` skill); warning-free; one commit.

### S2.3 — Stack system *(`IStackHost` — the novel part, on top)*

**Goal:** the stack primitives the Box consumes (`design/the-box.md` §13), built for real on the
spike's proven choreography and the canonical S2.2 base.

> **Note (cleanup):** an early `IStackHost` port + `InMemoryStackHost` fake + value types
> (`BranchRef`/`PullRef`/`PullView`/`MergeOutcome`) were added ahead of this step and reverted —
> consumerless (no `Domain/Box` yet), single-backend (GitHub only), and the fake's test only
> exercised the fake. YAGNI: the seam is introduced here, with the Box as its first real consumer,
> and faked at the `HttpClient` boundary rather than via a hand-written behavioral double. The spike
> choreography lives in git history + `research/stacked-prs.md`.

- **Precondition — port placement (settle before writing `IStackHost`, review M4):** the Box
  Orchestrator lives in `Domain/Box` and will consume `IStackHost`, and `Domain ↛ Features` is
  arch-enforced (`ArchitectureModel.cs`). So `IStackHost` **must land in the Domain band (or a
  Domain-side Contracts leaf) from the start** — *not* alongside today's `IPullRequests` in
  `Features/Git/Contracts` (which would force a move the moment B1's Domain orchestrator references
  it). The GitHub *adapter* stays in Features. This likely means `IPullRequests` itself migrates to
  Domain as it grows into `IStackHost`. Decide the home before, not during.
- `IStackHost`: branch/PR CRUD, **base retarget**, **two-level merge** (L1 merge-commit into the
  box branch; L2 box → `main`), and the **cascade-rebase** primitive (`rebase --onto` +
  lease-force-push, with `--force-if-includes` added to `Domain/Git` PushOp) the restack engine
  (B3) will drive.
- Linear stacks first; nothing assumes a single child (DAG-capable), matching `Node.parent`
  (`design/the-box.md` §7) — but **defer** sibling/merge-ordering rules (§7).
- **Two PR seams, decided (review M1):** `IStackHost` (Domain) is the Box's stack seam — it operates
  on *one* PR/branch at a time (CRUD, retarget, merge, get). The existing `IPullRequests` (Features,
  list-only) stays the **UI list seam** for `GET /git/prs`; it is *not* folded into `IStackHost`.
  They merge only when a **Domain** consumer needs to *list* PRs (none today — YAGNI); until then a
  list op on a Domain port would be speculative. `PullView` (Domain) and `PullRequestDto` (Features
  Contracts) stay separate DTOs for the same reason.
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
- **S2.1b target repo — sandbox vs in-repo `spike/` (OWNER call, not self-decidable):** the
  authoritative build-order doc specifies S2 runs **"against a throwaway GitHub repo"**
  (`the-box-implementation.md` §S2). In-repo `spike/` branches are the cheaper alternative (the bot
  is scoped to this repo) but would open/close junk PRs and may trip the protected `.github/**` CI
  workflows / branch protection in the production repo — unconfirmed. **Do not self-decide against
  the governing doc:** the owner either provisions a sandbox repo (matching box-impl) or ratifies
  the in-repo deviation and confirms `spike/*` won't fire required checks. Open until then.
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
