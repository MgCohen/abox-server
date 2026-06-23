<!-- docType: feature-plan -->
<!-- distilled from dump: PLANS/git-feature.md -->

:::summary
**Objective.** Build **S2 — the git/PR stack capability** (`IStackHost` + a real
GitHub adapter behind it) on the choreography proven in the S2.1 spikes, and bring
`Features/Git` to the canonical feature shape (ADR 0011). **Done** = real branch/PR
CRUD, base retarget, two-level merge, and a cascade-rebase primitive, behind a seam
the Box can consume.
:::

:::context
Git already exists in two bands, **stubbed at the remote seam**:
- `Domain/Git/Git.cs` — local working-copy `Operation`s (`Commit`, `Push`, `Pull`, …) via `RunCommand`. **Local only.**
- `Features/Git/` — the PR slice, almost entirely stubbed on Minimal API: `PrListEndpoint`, `MergeEndpoint`, backed by `StubPullRequests` (canned PRs 101/102/99). **4 assemblies** (ADR 0011 says 2).

**Gap:** no real GitHub adapter, no branch/PR CRUD, no base retarget, no stack notion, and the impl side isn't canonical.
:::

:::scope{kind=in}
- Branch + PR **CRUD**, PR creation onto a **non-`main`** base.
- **Base retarget** (`PATCH` a PR's base after its parent merges).
- **Two-level merge**: L1 merge-commit into the box branch; L2 box → `main`.
- **Cascade-rebase** primitive (`rebase --onto` + lease-force-push).
- `Features/Git` → canonical shape, `StubPullRequests` replaced by a real adapter.
:::

:::scope{kind=out}
- approve-as-owner identity → **S5** (S2 is read-as-bot only).
- Agent builder / resolver → agent frontier.
- Node projection / `CardPayload` → **S4**.
- Orchestrator graph / state machine / store → **B1/B2**.
- Conflict classification + resolution → **B3** (S2 ships only the mechanical rebase primitive).
:::

:::decision{id=d-shape}
Canonical feature shape is ratified (**ADR 0011**): FastEndpoints, one impl + one `Contracts` leaf, `internal sealed` endpoints. Git's 4→2 consolidation + Minimal-API→FastEndpoints port is the planned migration, not an open choice. Reference: `Features/Projects`.
:::

:::decision{id=d-merge}
**Merge-commit for Level-1.** A clean merge keeps the parent an ancestor → descendants only **retarget**, no rebase. Rebase cascade is reject/rebuild-only.
:::

:::decision{id=d-sha}
**Never key state on a SHA.** Stable identity = PR number / branch name / (later) `Node` id.
:::

:::decision{id=d-bot}
**Read-as-bot** (`ABox-Agent`): no self-approval, no owner credentials.
:::

:::decision{id=d-force}
Force-push lean: `--force-with-lease --force-if-includes` — lease *alone* was empirically clobbered by a background fetch. `Domain/Git/Git.cs` PushOp (lease-only today) must add `--force-if-includes` when S2.3 wires the cascade.
:::

:::phase{id=p-s2.1 title="Spikes — retire the unknowns" status=done}
**Goal.** Prove the choreography before building the adapter. Throwaway code in `spikes/git-stack/`.
Both legs proven: **S2.1a** (local git — merge-commit→clean retarget, `rebase --onto`, `--force-if-includes` mandatory, `merge-base --is-ancestor` is the clean oracle); **S2.1b** (GitHub API — create-PR-onto-non-`main`, `merge_method=merge`, pointer-only retarget; PR #64 diff byte-identical).
**Done-when.** Both legs proven; findings folded into `research/stacked-prs.md` §9.
:::

:::phase{id=p-s2.2a title="Canonical framework port (stub stays, parity-gated)" status=done}
**Goal.** Port Git to FastEndpoints `internal sealed` endpoints, 4 assemblies → 2, behavior unchanged — proven by a byte-level Wire characterization test (`GitPrsWireTests`). Reuses the `Features/Projects` template verbatim.
**Done-when.** Wire parity green; canonical shape; warning-free; one commit. *Pending owner CODEOWNERS review (touches protected files).*
:::

:::phase{id=p-s2.2b title="Real GitHub adapter" status=blocked}
**Goal.** Replace `StubPullRequests` with a real GitHub reader behind `IPullRequests`; grow the seam toward `IStackHost`.
**Blocked on (owner):** GitHub auth wiring (no `GITHUB_TOKEN`/Octokit/`gh` anywhere today); **delete-ref capability** (proxy `403`s, no MCP delete tool); removing `Git` from the two protected staleness guards (`EndpointConformance.PendingFastEndpointsMigration`, `FeatureShape.PendingConsolidation`).
**Done-when.** Lists/reads **real** PRs; covered by Live Rulebook tests; warning-free; one commit.
:::

:::phase{id=p-s2.3 title="Stack system (IStackHost — the novel part)" status=todo}
**Goal.** The stack primitives the Box consumes, on the proven choreography + canonical base.
`IStackHost` lands in the **Domain band** (`Domain ↛ Features` is arch-enforced): branch/PR CRUD, base retarget, two-level merge, cascade-rebase. Linear stacks first; DAG-capable, sibling/merge-ordering deferred. `IPullRequests` (Features, list-only) stays the UI list seam — not folded in.
**Done-when.** A 2-branch stack opens, merge-commits one node + retargets the descendant (no rebase), then a rebuild leg cascade-rebases onto the rewritten parent — driven by test code against a real repo; tests as Rulebooks.
:::

:::verification
Per-task bar (same as the rebuild): warning-free build, green tests, behavior **run** not just compiled, one coherent commit. Kept code ships behind its seam with a fake; tests land as Rulebooks (`test-rulebook`). Spike code is exempt (throwaway under `spikes/`).
:::

:::open-question{lean="Octokit.NET"}
**GitHub client library (S2.2):** Octokit.NET vs raw `HttpClient` vs shelling `gh`. Feature code, not enforcement surface, so ADR 0012's zero-dep rule doesn't apply — but flag the new dependency for the owner.
:::

:::open-question{lean="owner provisions a sandbox repo"}
**S2.1b/S2.2 target repo:** sandbox repo (per box-impl) vs in-repo `spike/` branches. Owner call — in-repo branches may trip protected `.github/**` CI / branch protection. Don't self-decide against the governing doc.
:::

:::open-question{lean="defer to S2.3/B2"}
**Level-2 merge method:** squash vs merge-commit for box → `main`. Independent of the L1 decision; `main` sees one reviewed Box either way.
:::
