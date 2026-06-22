# Governance Relocation — Proposal

> **Status: proposal / iterating — not yet decided.** Captures the design for
> consolidating the repo's agent-first surface under one root and extracting a
> portable enforcement engine. Produced 2026-06-17. If accepted it lands as an ADR
> amending **[ADR 0010](../../decisions/0010-agent-repo-controls.md) D2** (which
> currently scopes `governance/` to *controls only*). Until then nothing moves —
> this is the shape we're agreeing on first.

## Summary

Today the repo's **agent-first material** is scattered: control machinery in
`governance/`, decisions in `design/adr/`, plans in `PLANS/`, design notes in
`design/`, operating instructions in `CLAUDE.md`. This proposes to **consolidate the
relocatable parts under `governance/`** and, inside it, **split a portable
*enforcement engine* (`governance/harness/`) from the repo-specific *instance***
(this repo's policy rows, decisions, plans, design). Tool-pinned surfaces that
*can't* move (`CLAUDE.md`, `.claude/`, `.github/`, `tests/Harness/**`) stay where
their tools require and are governed in place. The payoff: a new repo adopts the
whole apparatus by copying one folder.

## Why now — multi-repo is real but thin

This repo is a **base for scaling how we develop**, not a one-off. Other projects
(a gear engine, a car framework, others) are parked waiting for this agent harness
to mature, then they'll adopt it. So "easy to stand the governance up in another
repo" is a genuine near-term requirement — thin (a handful of repos, copied
deliberately), not a published-product concern. That requirement is what justifies
revisiting ADR 0010-D2's "controls only" scope.

## Context — where governance is today (cold-read)

The repo protects its **enforcement surface** — test harness, ADRs, CI, build
config, and the controls themselves — from any agent (Claude, Codex, Cursor,
aider, Windsurf). The design is **one declarative policy, many enforcers**
(ADR 0010; how-to in [`governance/README.md`](../../README.md)).

- **Single source of truth:** `governance/protected-paths` — a flat list, one rule
  per line: `glob | owner | tier | reason`. Deliberately not YAML so the enforcers
  stay zero-dependency POSIX shell (ADR 0010-D3).
- **Shared checker:** `governance/protected-paths-check.sh` — every enforcer calls
  it. Gate mode blocks any matched path; `--tier <name>` lists matches of a tier.
- **Enforcers, by bypassability:** **CODEOWNERS required review** (the merge gate of
  record; a non-admin machine account authors PRs, the owner approves) > CI
  `policy-guard` job (advisory annotation + labels) > git hooks in `.githooks/`
  (fast local catch). `generate-codeowners.sh` compiles the policy into
  `.github/CODEOWNERS` (never hand-edited).
- **Tier model (review | attention | critical):** all three gate identically via
  code-owner review; the tier adds signal on top. Every protected-path change
  projects a PR label naming its tier (`review` / `attention` / `critical-path`);
  `attention` marks an elevated change; `critical` *additionally* fires a push
  notification via Apprise (`notify-critical.sh`, `notify.yml`). The notifier may use
  a dependency precisely because it is fail-safe and never gates (ADR 0012). *(The
  three-tier model landed recently; before that, tier was a thin alerting flag —
  `review` and `critical` were otherwise identical.)*
- **What's protected today:** `tests/Harness/**`, `tests/Meta/**`,
  `tests/**/Rulebook/**`, `tests/Tests/{Arch,Structure}/**`, `.github/**`,
  `.githooks/**`, `governance/**` (critical); `design/adr/**`, `CLAUDE.md`,
  `Directory.Build.props`, `.editorconfig`, `.gitattributes`, `ABox.slnx` (review).

**Prior decisions this proposal touches:**
- **ADR 0010-D2** — "Governance home: `governance/` for *controls only*." This
  proposal amends that: `governance/` becomes the agent-first root (controls +
  decisions + plans + design), with the controls isolated in `harness/`.
- **ADR 0010-D3** — flat glob list + POSIX-shell enforcers. Unchanged; the engine
  we extract *is* exactly this machinery.
- **ADR 0012** — dependency budget by failure mode. Unchanged; the notifier stays
  the only component allowed a dependency.

## The core idea — engine vs instance

"Portable across repos" only applies to the parts that are **repo-agnostic**. The
agent-first surface sorts into two piles, and conflating them *defeats* portability
(you'd drag this repo's ADRs and plans into a new repo, then delete them):

| Pile | What it is | Portable? |
|---|---|---|
| **Engine** | policy *format* + the enforcer scripts, git hooks, CI job shape, ADR/plan *templates*, operating *conventions* | **Yes** — copy into any repo |
| **Instance** | the `protected-paths` *rows* (globs for *this* tree), the actual ADRs, the actual plans, the behavioral oracle | **No** — rewritten per repo |

So the portable unit is the **engine**, and the instance simply lives beside it. A
new repo copies the engine and starts the instance folders empty.

## Constraint — some agent-first surfaces are tool-pinned

"Everything under `governance/`" can't be literal: several surfaces have their
location fixed by an external tool and must stay put. They are **governed in place**
(the policy already protects `tests/Harness/**` and `.github/**` where they live
without owning their location):

| Surface | Pinned by | Move? |
|---|---|---|
| `CLAUDE.md` | Claude Code auto-loads it from repo **root** | No — keep a thin root file (see Mechanisms) |
| `.claude/` (hooks, skills, settings) | Claude Code reads `.claude/` at root | No |
| `.github/workflows/`, `.github/CODEOWNERS` | GitHub reads fixed locations | No — workflow *calls* engine scripts; CODEOWNERS is generated into place |
| `tests/Harness/**`, Rulebooks, Meta | MSBuild / `ABox.slnx` — compiled projects | No — relocating breaks the build |
| `.githooks/` | `core.hooksPath` is configurable | **Yes** — moves into the engine |

## Proposed structure

Top level:

```
abox-server/
├── CLAUDE.md                  ← PINNED at root, now THIN (imports conventions + points at instance)
├── .claude/                   ← PINNED. skills, hooks, settings.json
├── .github/
│   ├── workflows/ci.yml       ← PINNED. policy-guard job CALLS governance/harness/ scripts
│   └── CODEOWNERS             ← PINNED, generated from the policy
│
├── governance/                ← THE AGENT ROOT
│   ├── harness/               ← PORTABLE ENGINE (copy this into a new repo)
│   │   ├── protected-paths-check.sh
│   │   ├── generate-codeowners.sh
│   │   ├── notify-critical.sh
│   │   ├── notify.yml
│   │   ├── hooks/             ← pre-commit, pre-push (set core.hooksPath here)
│   │   ├── ci/
│   │   │   └── policy-guard.yml   ← reusable workflow / paste-source for a new repo
│   │   ├── templates/
│   │   │   ├── adr.md
│   │   │   └── plan.md
│   │   ├── conventions/       ← the PORTABLE half of today's CLAUDE.md
│   │   │   ├── code-standards.md
│   │   │   ├── agent-guardrails.md
│   │   │   └── test-rulebook.md
│   │   └── README.md          ← how the engine works + "adopt in a new repo" steps
│   │
│   ├── policy/
│   │   └── protected-paths    ← INSTANCE: globs for THIS tree
│   ├── decisions/             ← INSTANCE: was design/adr/
│   ├── plans/                 ← INSTANCE: was PLANS/
│   └── design/                ← INSTANCE: was design/ minus adr (oracle, research)
│
├── tests/                     ← PINNED (compiled). Governed BY REFERENCE, not relocated.
│   ├── Harness/  Meta/  …
├── src/                       ← product, untouched
└── ABox.slnx
```

### Migration map

| Today | → Target | Class |
|---|---|---|
| `governance/*.sh`, `notify.*` | `governance/harness/` | engine |
| `.githooks/pre-*` | `governance/harness/hooks/` (repoint `core.hooksPath`) | engine |
| portable half of `CLAUDE.md` | `governance/harness/conventions/*.md` | engine |
| `governance/protected-paths` | `governance/policy/protected-paths` | instance |
| `design/adr/` | `governance/decisions/` | instance |
| `PLANS/` | `governance/plans/` | instance |
| `design/{behavioral-oracle,remote-access,…,research}` | `governance/design/` | instance |
| `.github/workflows/ci.yml` | stays — now references `harness/` | pinned |
| `.github/CODEOWNERS` | stays (regenerated) | pinned |
| `.claude/`, `tests/Harness/**` | stay | pinned, governed by reference |

## Naming — why `harness`, and the `tests/Harness` parallel

`tests/Harness/` is already defined in the policy as *"the enforcement engine
(ParityGuard, Rule, TestTypes)"* — the portable machinery, held apart from the
instance (the actual tests and Rulebooks). The governance engine plays the **same
role** for the repo. So `harness` is not an overloaded word here; it is the repo's
**noun for "reusable enforcement engine,"** and reusing it makes the parallel
explicit:

```
tests/                          governance/
├── Harness/   ← engine         ├── harness/   ← engine   (SAME ROLE)
├── Tests/  Meta/  Rulebooks    ├── policy/  decisions/  plans/  design/
└──   ↑ instance                └──   ↑ instance
```

Each top-level domain = **one Harness (the portable engine) + domain-specific
instance.** Keep the casing domain-appropriate: `tests/Harness/` stays PascalCase
(a .NET project); `governance/harness/` is lowercase (matches the shell/docs world).
Same word carries the concept; different case keeps any given path unambiguous.

This also settles the root name: **root stays `governance/`**, with `harness/`
inside it — mirroring how `Harness/` sits inside `tests/`. No root rename needed.

## Mechanisms

**CLAUDE.md splits.** The root file stays thin and auto-loaded, and pulls the
portable conventions in via Claude's `@`-import, so "how we operate" travels with
the engine while only "what *this* repo is" stays local:

```md
# CLAUDE.md  (root — thin, pinned)
@governance/harness/conventions/code-standards.md
@governance/harness/conventions/agent-guardrails.md
## What we're doing   ← the only repo-specific prose
Re-authoring the spine… see governance/plans/, governance/decisions/.
```

**The policy rewires to the new paths, and the engine protects itself.** Rows
re-glob to the consolidated locations; `harness/` and `policy/` are `critical` so a
copied engine can't be quietly weakened. Illustrative (tiers per the open question
below):

```
governance/harness/**   | @owner | critical  | Portable engine — machinery + conventions.
governance/policy/**    | @owner | critical  | The policy itself.
governance/decisions/** | @owner | review    | ADRs — frozen history.
governance/plans/**     | @owner | attention | Living plans. (tier = open question)
governance/design/**    | @owner | review    | Oracle + research.
.github/**  tests/Harness/** …  ← unchanged, governed in place
```

**Adopting in a new repo** becomes: `cp -r governance/harness new-repo/governance/`
→ set `core.hooksPath` → drop the `policy-guard` job into `.github/workflows/` →
write fresh `policy/protected-paths` rows → run `generate-codeowners.sh`. The
instance folders start empty. That one-folder copy is the whole point of the split.

## Open questions (still iterating)

1. **`plans/` tier.** With the current three tiers, `attention` still *hard-gates*
   via code-owner review — fine for stable specs (oracle, PRD), but every working-
   plan edit would then need owner approval. Options: leave volatile plans
   ungoverned, govern only the stable specs, or revisit an advisory (non-gating)
   tier. Resolve before assigning rows.
2. **Distribution mechanism.** Copy-paste template (simplest, divergence over time)
   vs git subtree/submodule (one source of truth, more ceremony) vs published
   setup-script/reusable Action (most automated). Thin multi-repo use leans toward
   copy-or-subtree; not required to decide now, but it sets how *clean* the
   engine/instance boundary must be.
3. **`research/` and `spikes/`.** Top-level `research/` overlaps `design/research/`;
   `spikes/` is throwaway code, not docs. Fold doc-research under
   `governance/design/research/`; leave code `spikes/` where it is — confirm.

## Out of scope / non-goals

- **No behavior change.** Pure relocation + naming; enforcers, tiers, and the merge
  gate keep their current semantics.
- **No relocation of compiled or tool-pinned surfaces** (`tests/Harness/**`,
  `.claude/`, `.github/`) — governed in place.
- **Not publishing the harness** as a package/product — the multi-repo need is a few
  internal repos, adopted deliberately.

## Next steps

1. Settle the open questions (tier for `plans/`, distribution intent).
2. Promote this to an ADR amending ADR 0010-D2, citing the `tests/Harness` parallel
   as the naming precedent.
3. Execute the move behind that ADR: `git mv` (preserve history), rewrite policy
   globs + regenerate CODEOWNERS, rewrite cross-doc links (~30 files reference
   `PLANS/`/`design/`), split `CLAUDE.md`, repoint `core.hooksPath` and the CI job.
