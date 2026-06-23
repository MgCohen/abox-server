<!-- docType: feature-plan -->
<!-- distilled from dump: PLANS/git-feature.md -->

## 1 - Summary

**Objective.** Build **S2 — the git/PR stack capability**: an `IStackHost` seam
and a real GitHub adapter behind it, built on the choreography proven in the S2.1
spikes, plus bringing `Features/Git` up to the canonical feature shape (ADR 0011).
This is the substrate the larger "Box" feature composes but does not own — it is
also independently useful, since a real PR surface helps the current flows UI.
**Done** = real branch/PR CRUD, base retarget, two-level merge, and a
cascade-rebase primitive, all behind a seam the Box can consume.

## 2 - Context

Git already exists in the repo, split across two bands and stubbed at the remote
seam. `Domain/Git/Git.cs` (the Domain band, which stays) runs local working-copy
operations — `Commit`, `Push`, `Pull`, `Diff` and friends — as `Operation`s over
real git, with guardrails like refusing a force-push to `main`. It is **local
only**: no remote, branch, or PR notion. `Features/Git/` (the Features band) is
the PR slice, but almost everything in it is stubbed: `PrListEndpoint` and
`MergeEndpoint` on Minimal API, backed by `StubPullRequests` serving three canned
PRs. It also sprawls across four assemblies where ADR 0011 wants two.

**The gap.** There is no real GitHub adapter — the merge endpoint is a stub, not
a git merge — no branch/PR CRUD, no base retarget, and no notion of a stack. The
Features impl also doesn't yet match the canonical slice shape.

## 3 - Scope - In scope
kind: in

- Branch + PR **CRUD**, including PR creation onto a **non-`main`** base.
- **Base retarget** — `PATCH` a PR's base after its parent merges.
- **Two-level merge**: Level-1 merge-commit into the box branch; Level-2 box → `main`.
- **Cascade-rebase** primitive (`rebase --onto` + lease-force-push) for rebuilds.
- Bringing `Features/Git` to the canonical shape, `StubPullRequests` → real adapter.

## 4 - Scope - Out of scope
kind: out

- approve-as-owner identity → **S5**; S2 is read-as-bot only.
- Agent builder / resolver → the agent frontier.
- Node projection / `CardPayload` → **S4**.
- Orchestrator graph, state machine, persistence → **B1/B2**.
- Conflict classification + resolution → **B3**; S2 ships only the mechanical rebase primitive.

## 5 - Decision - Canonical feature shape (ADR 0011)

The canonical feature shape is ratified: FastEndpoints, one implementation
assembly plus one `Contracts` leaf, `internal sealed` endpoints. Git's 4→2
consolidation and the Minimal-API → FastEndpoints port is therefore the planned
migration, not an open choice. Reference implementation: `Features/Projects`.

## 6 - Decision - Merge-commit for Level-1

Use a merge commit for L1 stacked merges. A clean merge keeps the parent an
ancestor of the box branch, so descendants only **retarget** — no rebase. The
rebase cascade is reserved for the reject/rebuild path.

## 7 - Decision - Never key state on a SHA

Stable identity is the PR number, branch name, or (later) `Node` id — never a
commit SHA, which moves under rebases and force-pushes.

## 8 - Decision - Read-as-bot

All git/PR actions run as the bot (`ABox-Agent`): no self-approval and no owner
credentials. Owner identity is a separate substrate (S5).

## 9 - Decision - Force-push lean

Force-pushes use `--force-with-lease --force-if-includes`. The lease *alone* was
empirically clobbered by a background fetch during the spike. `Domain/Git/Git.cs`
PushOp adds only the lease today, so S2.3 must add `--force-if-includes` when it
wires the cascade.

## 10 - Phase - Spikes: retire the unknowns
status: done

**Goal.** Prove the git + GitHub choreography before building the real adapter,
with throwaway code in `spikes/git-stack/`. Both legs landed. S2.1a (local git)
proved merge-commit → clean retarget, `rebase --onto` replay, that
`--force-if-includes` is mandatory, and that `merge-base --is-ancestor` is the
reliable "clean" oracle. S2.1b (GitHub API) proved create-PR-onto-non-`main`,
`merge_method=merge`, and pointer-only base retarget live — PR #64's diff stayed
byte-identical across the retarget.
**Done-when.** Both legs proven; findings folded into `research/stacked-prs.md` §9. ✓

## 11 - Phase - Canonical framework port (stub stays, parity-gated)
status: done

**Goal.** Port Git to the canonical shape — FastEndpoints `internal sealed`
endpoints, four assemblies down to two — without changing behaviour, reusing the
`Features/Projects` template verbatim. A byte-level Wire characterization test
(`GitPrsWireTests`) captures the stub behaviour and proves it unchanged.
**Done-when.** Wire parity green; slice matches the canonical shape; warning-free;
one commit. *Pending owner CODEOWNERS review — it touches protected files.*

## 12 - Phase - Real GitHub adapter
status: blocked

**Goal.** Replace `StubPullRequests` with a real GitHub reader behind the
`IPullRequests` seam, then grow that seam toward `IStackHost` operations.
**Blocked on the owner:** GitHub auth is wired nowhere today (no `GITHUB_TOKEN`,
Octokit, or `gh`); the toolset has **no delete-ref capability** (the proxy 403s,
the MCP server has no delete tool); and removing `Git` from the two protected
staleness guards (`EndpointConformance.PendingFastEndpointsMigration`,
`FeatureShape.PendingConsolidation`) is a reviewed owner PR.
**Done-when.** Lists/reads **real** PRs; covered by Live Rulebook tests;
warning-free; one commit.

## 13 - Phase - Stack system (IStackHost — the novel part)
status: todo

**Goal.** Build the stack primitives the Box consumes, on the proven
choreography and the canonical S2.2 base. `IStackHost` must land in the **Domain
band** from the start, because the Box orchestrator in `Domain/Box` will consume
it and `Domain ↛ Features` is arch-enforced. It carries branch/PR CRUD, base
retarget, two-level merge, and the cascade-rebase primitive. Linear stacks first,
DAG-capable but with sibling/merge-ordering deferred. The existing `IPullRequests`
(Features, list-only) stays the UI list seam — it is not folded in.
**Done-when.** A hand-made 2-branch stack opens, merge-commits one node and
retargets the descendant (no rebase), then a rebuild leg cascade-rebases the
descendant onto the rewritten parent — all driven by test code against a real
repo. Tests land as Rulebooks.

## 14 - Verification

Every task clears the same bar as the rebuild: warning-free build, green tests,
behaviour actually **run** (not just compiled), and one coherent commit. Kept
code ships behind its seam with a fake so the next task can start; tests land as
Rulebooks (`test-rulebook`). Spike code is exempt — it stays throwaway under
`spikes/`.

## 15 - Open Question - GitHub client library
lean: Octokit.NET

Octokit.NET (typed, official) vs raw `HttpClient` vs shelling `gh`. This is
feature code, not the enforcement surface, so ADR 0012's zero-dependency rule
does not apply — but flag the new dependency for the owner. Decide at S2.2.

## 16 - Open Question - Spike/adapter target repo
lean: owner provisions a sandbox repo

A throwaway sandbox repo (per the box-impl doc) vs in-repo `spike/`-prefixed
branches. In-repo branches are cheaper but may trip the protected `.github/**`
CI workflows and branch protection. This is an owner call — don't self-decide
against the governing doc.

## 17 - Open Question - Level-2 merge method
lean: defer to S2.3/B2

Squash vs merge-commit for the box → `main` final merge. Independent of the L1
decision; `main` sees one reviewed Box either way. Defer until S2.3/B2.
