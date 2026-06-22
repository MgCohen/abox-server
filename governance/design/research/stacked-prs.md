# Stacked PRs — prior art & mechanics (research)

> **Status:** Research / prior-art for the **S2 (PR / Git stack)** track of
> [`PLANS/the-box-implementation.md`](../../plans/the-box-implementation.md). Feeds the
> technical spike that retires the rebase-merge / retarget unknown
> ([`design/the-box.md`](../the-box.md) §16). Not a decision record — when a
> finding becomes a locked choice it graduates to an ADR. Every claim carries a source;
> low-confidence claims are flagged inline.

## Why we looked

The Box's two-level merge (PRs target the box branch, box branch → `main`) and cascade
restack (B3) depend on one mechanical unknown the plan calls S2's top technical risk:
**merging one PR rewrites commit SHAs, so every descendant must be rebased onto the new
parent and have its PR base retargeted.** Stacked-PR tooling (Graphite, Sapling, git-town,
ghstack, spr, git-branchless) has solved exactly this; this doc harvests how, so our spike
copies a known-good sequence instead of discovering it.

## TL;DR for the spike

1. **Merge method is the whole ballgame.** `squash` and `rebase` **rewrite SHAs** and break
   the stack; only a true **merge commit** preserves descendant ancestry. **Decision:** Level-1
   stacked merges use a **merge commit** (`design/the-box.md` §16) — the merged parent stays an
   ancestor of the box branch, so on the happy path descendants only **retarget** their base
   (often automatically), no rebase. The local `rebase --onto` cascade is reserved for
   **reject/rebuild** (§8), where SHAs genuinely change.
2. **There is no GitHub API that rebases descendants for you.** Auto-retargeting only moves
   the base *pointer*; it never rebases commits. The orchestrator does the rebase locally
   (`git rebase --onto`) and force-pushes with a lease, then `PATCH`es each PR's `base`.
3. **Never key state on a SHA** — they're rewritten by every rebase/squash/amend/force-push.
   Key on PR number / branch name / a stable change-id. (Our `Node` identity already is
   SHA-independent — §2.1; this research validates that hard.)
4. **Order of operations matters** to dodge GitHub's "base branch deleted" and stale-approval
   traps. Retarget descendants *before* deleting a merged head branch; expect force-push to
   dismiss approvals (ground-up review makes that cheap, not catastrophic).

---

## 1. Merge method → SHA rewrite (the core mechanic)

| Method | SHAs | Safe for a stack's descendants? |
|---|---|---|
| **Merge commit** (`merge`) | originals preserved + 1 new merge commit | **Yes** — descendants keep their ancestor; GitHub can even auto-retarget |
| **Squash** (`squash`) | all commits replaced by **1 new** commit | **No** — descendants point at commits that no longer exist on base |
| **Rebase** (`rebase`) | every commit **replayed → new SHA** | **No** — descendants are based on old SHAs; must be rebased |

- GitHub's `rebase-and-merge` "will **always** update the committer information and create new
  commit SHAs," unlike a local `git rebase`. ([GitHub Docs — About merge methods](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/about-merge-methods-on-github))
- Squash/rebase "destroy the shared commit hashes that git uses to recognize the dependency
  relationship between dependent branches." ([LogRocket — Stacked PRs in GitHub](https://blog.logrocket.com/using-stacked-pull-requests-in-github/))

**Consequence for us:** we use a **merge commit** at Level 1, so a *clean* merge keeps the
parent's commits as ancestors of the box branch — descendants only retarget (no rebase). The
local `rebase --onto` cascade is needed only on **reject/rebuild** (§8), where the rebuilt
phase's SHAs change. The spike must therefore prove *both* paths: clean merge-commit
(retarget-only) and rebuild (rebase-onto + force-push).

## 2. The merge API and the retarget API

The exact calls our .NET adapter will drive (`IStackHost`):

- **Create PR onto a non-`main` base** — `POST /repos/{owner}/{repo}/pulls` with
  `base=<box-or-parent-branch>`, `head=<phase-branch>`. Any existing branch in the *same
  repo* is a valid base (cross-repo base is rejected). ([GitHub REST — Pulls](https://docs.github.com/en/rest/pulls/pulls))
- **Retarget a PR after its parent merges** — `PATCH /repos/{owner}/{repo}/pulls/{n}` with
  `base=<new-base>`. Changes the merge target only; does **not** rebase commits, and review
  comments on now-absent lines may go stale. ([GitHub REST — Pulls](https://docs.github.com/en/rest/pulls/pulls))
- **Merge with a chosen method** — `PUT /repos/{owner}/{repo}/pulls/{n}/merge` with
  `merge_method = merge | squash | rebase`. Returns `{ sha, merged, message }`; `sha` is the
  resulting commit (merge commit / squashed commit / new base tip). ([GitHub REST — Pulls](https://docs.github.com/en/rest/pulls/pulls))
- **Force-update a branch ref** (after a local rebase) — `PATCH /repos/{owner}/{repo}/git/refs/{ref}`
  with `{ sha, force: true }`; create via `POST .../git/refs` (`ref: refs/heads/...`).
  ([GitHub REST — Git refs](https://docs.github.com/en/rest/git/refs))

**No "rebase descendants" endpoint exists.** GitHub's own stacked workflow rebases locally
and force-pushes; you orchestrate the cascade yourself. ([GitHub REST — Pulls](https://docs.github.com/en/rest/pulls/pulls))

## 3. The cascade-rebase algorithm (what the tools actually do)

The shared primitive across tools is `git rebase --onto`:

```
git rebase --onto <new-base-tip> <old-parent-tip> <descendant-branch>
```

i.e. replay the descendant's *unique* commits off the *old* parent tip onto the *new* base,
then force-push (with lease) and `PATCH` its PR base. Walk the stack from the merge point
upward, rebasing each node onto its just-rebased parent. ([gh-stack FAQ](https://github.github.com/gh-stack/faq/))

- **Bottom-up only.** You cannot land a middle PR in isolation — merging in the middle lands
  it *with everything below it*. git-town enforces "ship the oldest branch first." ([gh-stack FAQ](https://github.github.com/gh-stack/faq/), [git-town — Stacked changes](https://www.git-town.com/stacked-changes.html))
- **Atomic retarget.** Graphite routes through temporary `graphite-base/*` branches so "there
  is never a moment where the new base points to a branch that no longer exists" — a pattern
  worth copying to avoid the base-deleted race. ([Graphite — Merge PRs](https://graphite.com/docs/merge-pull-requests))
- **Cost is per-conflict, not per-stack.** A clean cascade is cheap; only conflicting nodes
  pay. ([Graphite — Understanding merge conflicts](https://graphite.com/guides/understanding-merge-conflicts-prs))
- **`git rebase --update-refs`** (Git ≥ 2.38) rebases a branch and moves *all* dependent refs
  pointing into it in one shot — a native partial substitute for tool-side bookkeeping.
  ([git-town / general practice](https://www.git-town.com/stacked-changes.html))

## 4. How the tools track parent edges (model comparison)

The decision each tool makes about *where the stack lives* — directly relevant to our
`Node.parent` edge + persistence store (§12).

| Tool | Parent-edge model | Stack unit | Force-push? | Restack trigger |
|---|---|---|---|---|
| **Graphite** | local metadata (`.git/refs/branch-metadata/` JSON) | 1 **branch**/PR | avoided via `gt restack` | `gt restack` / `gt modify` |
| **git-town** | git-config parent pointers (`git-town.<br>.parent`) | 1 **branch**/PR | `--force-with-lease` on sync | `git town sync --all` |
| **Sapling** | **mutation history** (commit identity survives rewrite; server-shared) | 1 **commit**/PR | none — stable identity | automatic from mutation log |
| **ghstack** | commit-message trailers (`ghstack-source-id`) + 3 branches/commit (`base`/`head`/`orig`) | 1 **commit**/PR | none — merge commits onto base | recompute base/head branches |
| **spr** | GitHub PR state (minimal local metadata) | 1 **commit**/PR | `--force-with-lease` via `git spr sync` | `git spr sync` |
| **git-branchless** | SQLite event log (`post-rewrite` hooks) | 1 **commit**/PR | none — event-driven | event-log replay |

Sources: [Graphite docs](https://graphite.com/docs/track-branches), [git-town](https://www.git-town.com/stacked-changes.html),
[Sapling — visibility & mutation](https://sapling-scm.com/docs/dev/internals/visibility-and-mutation/),
[ghstack README](https://github.com/ezyang/ghstack/blob/master/README.md), [ejoffe/spr](https://ejoffe.github.io/spr/),
[git-branchless architecture](https://github.com/arxanas/git-branchless/wiki/Architecture).

**Read for us:** we're closest to the **Graphite / git-town "one branch per node, parent edge
in our own store"** model — which matches `Node` (stable id, nullable `parent`) + the hybrid
persistence store (§12), *not* the commit-identity model (Sapling/jj) that would require
adopting their VCS. The lesson from Sapling/jj is conceptual and we already apply it: **a
stable logical id that survives history rewrite** — our `Node` id is that change-id.

## 5. Conflict handling during restack

The §9 conflict-tier ladder, validated against prior art:

- **`git rerere`** (reuse recorded resolution): records a conflict's resolution and replays it
  on identical conflicts across repeated rebases — purpose-built for "resolve the same conflict
  over and over on a long-lived branch," i.e. our cascade. Enable with
  `git config rerere.enabled true`; stored in `.git/rr-cache/`; `git merge`/`rebase` invoke it
  automatically. ([git-scm — rerere](https://git-scm.com/docs/git-rerere))
- **Mergiraf** — a **Tree-sitter-based syntax-aware merge driver** (25+ languages incl. **C#**).
  Hybrid: tries line-based first, falls back to structural only on conflict; keeps conflict
  markers when unsure rather than guessing. Wired via a `[merge "mergiraf"]` driver +
  `.gitattributes`; requires `diff.conflictStyle = diff3`. Our Tier-0/1 mechanical resolver.
  ([mergiraf.org](https://mergiraf.org/), [usage](https://mergiraf.org/usage.html))
- **Tier escalation is real and ordered.** Academic study (15,886 Java merge scenarios) ranks
  conflicts textual → build → test in rising cost/severity, matching our §9 ladder (git sees
  tier 1, compiler tier 2, tests tier 3). ([NSF — Merge conflicts in Java](https://par.nsf.gov/servlets/purl/10515782))

## 6. Pitfalls the orchestrator must defend against

- **Base-branch-deleted race.** Since May 2020 GitHub *auto-retargets* a PR whose base is
  merged+deleted (older: it *closed* them) — but only moves the pointer, never rebases.
  **Defense:** retarget descendants explicitly *before* deleting a head branch; don't rely on
  the auto-behavior. ([GitHub changelog — PR retargeting](https://github.blog/changelog/2020-05-19-pull-request-retargeting/))
- **Force-push clobber.** `--force` blindly overwrites concurrent pushes; use
  `--force-with-lease --force-if-includes` (lease alone is defeated by background fetches).
  ([git-scm — git-push](https://git-scm.com/docs/git-push), [Adam Johnson](https://adamj.eu/tech/2023/10/31/git-force-push-safely/))
- **Stale base → phantom diff.** An un-restacked PR shows unrelated commits from a moved base;
  the diff looks wrong until rebased. **Defense:** keep descendants restacked eagerly.
  ([Graphite — stacked diffs](https://graphite.com/guides/stacked-diffs))
- **Approval invalidation.** Force-push / merge-base change dismisses stale approvals
  ("merge-base changed after approval"). **Defense (already in our design):** **ground-up
  review** — never approve a descendant before its ancestor settles, so there's rarely a stale
  approval to void (§6 invariant 2). ([GitHub — required-approval security](https://github.blog/changelog/2023-06-06-security-enhancements-to-required-approvals-on-pull-requests/),
  [community #58535](https://github.com/orgs/community/discussions/58535))
- **No self-approval.** GitHub forbids a PR author approving their own PR — structural backing
  for our bot-reads / owner-approves split (§5). A bot can only "approve" via a separate PAT/App
  identity explicitly allowlisted, which we deliberately do **not** do. ([Graphite — approval rules](https://graphite.com/guides/pull-request-approval-permissions-rules-github))

## 7. Low-confidence / verify-before-relying

> **Update (2026-06-17):** the §2 / §8-Leg-A happy path is now **empirically confirmed live** —
> see §9. The auto-cascade / auto-retarget-on-base-delete claim below is **still unverified**
> (branch deletion was blocked in the spike environment).

- **"GitHub native stacked-PR support" (`gh-stack`, an InfoQ 2026 article).** Several findings
  trace to `github.github.com/gh-stack` and a 2026 InfoQ piece describing first-party stacked
  PRs with auto-cascade. Treat as **unverified** — possibly an experiment, a third-party
  extension, or overstated. The spike must **not** assume a native cascade exists; build on the
  REST primitives in §2, which are solid first-party docs.
- **Sapling's exact restack algorithm** is inferred from fold/split docs, not spelled out.
- **Tier-escalation study** is Java-specific; the *ordering* generalizes, the *rates* may not.

## 8. What this means for the spike (S2.1)

Minimum sequence to prove against a throwaway repo, end to end — both legs:

**Leg A — clean merge (merge commit, the happy path):**
1. Create base branch `box/x`; create `phase-1` and `phase-2` (2-node stack) with PRs:
   `phase-1 → box/x`, `phase-2 → phase-1`.
2. Merge `phase-1` into `box/x` via the merge API with `merge_method=merge` (merge commit).
3. `PATCH` `phase-2`'s base `phase-1` → `box/x`; delete the `phase-1` head branch **after** the
   retarget.
4. Confirm `phase-2`'s diff is clean (no phantom commits — `phase-1`'s commits are now ancestors
   of `box/x`), PR mergeable, nothing auto-closed.

**Leg B — rebuild cascade (the reject path, where SHAs do change):**
5. Rebuild/amend `phase-1` (new SHAs); locally `git rebase --onto <new-phase-1-tip>
   <old-phase-1-tip> phase-2`; force-push with `--force-with-lease --force-if-includes`; confirm
   `phase-2` is clean against the rebuilt parent.

Output of the spike = a verified call/command transcript + the gotchas that actually bit, fed
into **S2.2 (base git unify + real adapter)** and **S2.3 (`IStackHost` stack ops)**. The spike
is throwaway; the transcript is the kept artifact.

## 9. Spike results — S2.1a/b verified live (2026-06-17)

Both S2.1 legs ran (`spikes/git-stack/FINDINGS-local.md`, `FINDINGS-github.md`). The happy path
and rebuild cascade are **empirically confirmed**; the auto-retarget-on-base-delete question
remains **unverified** (branch deletion blocked in this environment).

**Confirmed — S2.1a (local, bare-repo remote):**
- **Merge-commit preserves descendant ancestry.** After `git merge --no-ff`,
  `merge-base --is-ancestor <parent-tip> <box>` exits 0; the descendant diff is clean →
  retarget-not-rebase holds.
- **Rebuild cascade.** `git rebase --onto <new-parent> <old-parent> <descendant>` replays cleanly.
- **Force-push.** `--force-with-lease` *alone* was **defeated by a background `git fetch`** (it
  clobbered un-integrated work); `--force-with-lease --force-if-includes` rejected that exact race.
  **`--force-if-includes` is mandatory** for an automated cascade — `Domain/Git/Git.cs` PushOp is
  lease-only today (line 81) and must add it (S2.3).
- **New "clean" oracle.** Three-dot diff (`base...head`) is **not** sufficient alone — robust to a
  fast-forwarded base but leaks a phantom on a *rewritten* base. The reliable check is
  `merge-base --is-ancestor <base-tip> <head>`, which the merge-commit happy path satisfies for free.

**Confirmed — S2.1b (live GitHub API, `MgCohen/abox-server`, `spike/` branches):**
- Create-PR-onto-non-`main`-base works (`base=spike/box-x` and `base=spike/phase-1` both accepted).
- `merge_pull_request` with `merge_method=merge` returns the merge-commit sha = new box-branch tip.
- Retarget is a **pointer-only** PATCH (`update_pull_request base=…`); **PR #64's diff was
  byte-identical before and after retarget — only the descendant's one file, no phantom.** §1/§8
  Leg-A is now proven end-to-end on the real API.
- **New:** `mergeable_state:unstable` = checks *pending*, NOT a conflict (distinct from `dirty`) —
  adapters must not treat `unstable` as unmergeable.
- **New:** auto-delete-head-on-merge is **OFF** for this repo; a merged head branch survives —
  don't assume merged heads vanish.

**Still unverified (carry forward):** GitHub's auto-retarget vs auto-close on base-branch *delete*,
and approval-dismissal-on-push — both untestable here (branch deletion blocked; no branch protection).
Per §6/§7 they stay **verify-don't-rely**; the orchestrator must **explicitly retarget** descendants
and never depend on auto-behavior.

**Environment constraints (matter for S2.2/S2.3 tooling):** the authenticated `origin` proxy permits
create/push but **403s on ref deletion**; the GitHub MCP server has **no ref-delete tool**; no `gh`
CLI. Branch cleanup needs owner rights, and the real adapter (S2.2b) needs a delete-ref capability the
current toolset lacks.

## Sources

Primary (first-party docs):
- GitHub REST — [Pulls](https://docs.github.com/en/rest/pulls/pulls), [Git refs](https://docs.github.com/en/rest/git/refs)
- GitHub Docs — [About merge methods](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/about-merge-methods-on-github)
- GitHub changelog — [PR retargeting](https://github.blog/changelog/2020-05-19-pull-request-retargeting/), [required-approval security](https://github.blog/changelog/2023-06-06-security-enhancements-to-required-approvals-on-pull-requests/)
- git-scm — [git-rerere](https://git-scm.com/docs/git-rerere), [git-push](https://git-scm.com/docs/git-push)
- [mergiraf.org](https://mergiraf.org/) · [usage](https://mergiraf.org/usage.html)

Tooling prior art:
- Graphite — [track branches](https://graphite.com/docs/track-branches), [merge PRs](https://graphite.com/docs/merge-pull-requests), [merge conflicts](https://graphite.com/guides/understanding-merge-conflicts-prs), [stacked diffs](https://graphite.com/guides/stacked-diffs)
- [git-town — stacked changes](https://www.git-town.com/stacked-changes.html)
- Sapling — [visibility & mutation](https://sapling-scm.com/docs/dev/internals/visibility-and-mutation/), [stacks](https://sapling-scm.com/docs/overview/stacks/)
- [ghstack README](https://github.com/ezyang/ghstack/blob/master/README.md) · [ejoffe/spr](https://ejoffe.github.io/spr/) · [git-branchless architecture](https://github.com/arxanas/git-branchless/wiki/Architecture)

Practitioner / academic:
- [LogRocket — stacked PRs in GitHub](https://blog.logrocket.com/using-stacked-pull-requests-in-github/)
- [Dave Pacheco — stacked PRs on GitHub](https://www.davepacheco.net/blog/2025/stacked-prs-on-github/)
- [Aviator — stacked PRs as narrative](https://www.aviator.co/blog/stacked-prs-code-changes-as-narrative/)
- [NSF — merge conflicts in Java projects](https://par.nsf.gov/servlets/purl/10515782)
</content>
</invoke>
