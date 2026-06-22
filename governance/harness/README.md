# `governance/harness/` — the portable enforcement engine

The repo-agnostic half of governance: the machinery that reads one policy and
enforces it. A new repo adopts the whole apparatus by **copying this one folder**.
The *instance* it acts on (this repo's `governance/policy/` rows, `decisions/`,
`plans/`, `design/`, `registry/`) lives beside it, never inside it. Why this split:
[ADR 0014](../decisions/0014-governance-agent-first-root.md).

## What's in here

| Piece | Role |
|---|---|
| [`protected-paths-check.sh`](protected-paths-check.sh) | The one checker. Reads `governance/policy/protected-paths`; gates (exit 1 on a protected path) or lists a tier. Every enforcer calls it. |
| [`generate-codeowners.sh`](generate-codeowners.sh) | Regenerates `.github/CODEOWNERS` from the policy. Never hand-edit CODEOWNERS. |
| [`hooks/`](hooks) | `pre-commit` / `pre-push` — fast local catch. Enable with `core.hooksPath` (below). |
| [`notify-critical.sh`](notify-critical.sh) + [`notify.yml`](notify.yml) | Critical-tier push alerts via Apprise (fail-safe, never gates — ADR 0012). Knobs: [`notify.md`](notify.md). |
| [`conventions/`](conventions) | The portable operating rules `CLAUDE.md` `@`-imports: code standards, agent guardrails, test-rulebook. |

The checker is zero-dependency POSIX `sh`, so the policy is a flat
`glob \| owner \| tier \| reason` list, not YAML (ADR 0010).

## Adopt in a new repo

1. **Copy** `governance/harness/` into the new repo.
2. **Write the instance policy** — `governance/policy/protected-paths`, fresh rows for
   that repo (start by protecting `governance/harness/**` + `governance/policy/**`).
3. **Generate CODEOWNERS:** `./governance/harness/generate-codeowners.sh`.
4. **Enable local hooks:** `git config core.hooksPath governance/harness/hooks`.
5. **Drop in the CI job** — a `policy-guard` job that calls the checker + the generator
   (copy the shape from this repo's [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)).
6. *(optional)* `@`-import the `conventions/` from the repo's `CLAUDE.md`; set the
   `NTFY_TOPIC` secret to turn on critical-tier alerts.

Instance folders (`decisions/`, `plans/`, `design/`) start empty. The distribution
mechanism (copy vs subtree/submodule) is deliberately left open — ADR 0014 Q2.

## Tiers

`critical` gates by code-owner review **and** fires a push alert; `review`/`attention`
gate by review only (`attention` flags an elevated change). Blank/unknown = `review`.
All gating is CODEOWNERS required review — the tier is signal on top, surfaced as a PR
label. The merge gate of record and the full controls story:
[`governance/README.md`](../README.md).
