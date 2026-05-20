---
type: plan
status: draft
tags: [#infra, #claude-code, #unity, #agents, #remote, #tmux]
---

# Unity Agent Infrastructure — implementation plan

> **Goal.** Stand up a single always-on Linux VM that hosts Unity (headless) and multiple git worktrees of this repo, so Claude Code can drive Unity-aware work on N branches in parallel — both as **remote chat sessions** I open from anywhere, and as **dispatched agent runs** triggered by webhook/REST. Replaces the current "leave my desktop on 24/7 and RDP in" workflow.
>
> **Ordering.** Interactive chat first (Track A) — it solves the immediate pain (desktop-off, work-from-phone) with the least moving parts. Agent dispatch second (Track B) — built on top of the same VM, same auth, same worktrees.

---

## How to execute this plan (executor notes)

Read in this order before doing anything: **§2 (Key principles)** — non-negotiable; **§5 (Services & tools)** — the binding trust filter; **§7 Track A** — start here. Track B is only after A0–A7 are all checked. §11 has the research history if you need to understand *why* a decision is the way it is.

- **Gates that stop the world**:
  - **A0** — if Unity Personal EULA doesn't qualify for our entity, or `ANTHROPIC_API_KEY` won't unset cleanly, stop and surface to the user. Do not work around. The whole plan re-costs if either fails.
  - **A1** — if Unity batch-mode against two concurrent worktrees has any conflict, stop. The worktree premise is wrong and the plan needs rework.
  - **Phase boundary** — complete one phase, tick the checkboxes in this file, commit, summarize findings, wait for user go-ahead before starting the next. Exception: if the user says "run through A unattended," chain A1→A7 but still stop at any failed gate.
- **Trust filter is binding (§5)**: do not `npm install`/`pip install`/`git clone` anything outside the "Required" and "Optional" tables. `siteboon/claudecodeui` is "Under evaluation" — gated on a separate security review, do not install. "Reference-only" tools (Herdr, Flue, HolyClaude, sugyan/claude-code-webui, claude-bridge) are for reading source on GitHub, never cloning or installing.
- **Subscription audit before any `claude` invocation**: run `claude /status` (must show Max plan) and `env | grep -i anthropic` (must show OAuth token only, no `ANTHROPIC_API_KEY`). If an API key ever appears, stop and surface — we're silently API-billing when we shouldn't be.
- **Artifact discipline**: any reusable script, systemd unit, workflow YAML, or config you produce goes into `infra/` on this branch (create the directory if needed). Inline one-off commands run on the user's machine don't get committed. Plan-doc checkbox updates + script commits happen together at each phase boundary.
- **VM access**: phases A4 onward need SSH to the Hetzner box. Until provisioned, do not write scripts assuming SSH access — write them as files the user/you will later `scp` or `git pull` onto the VM.

---

## 1. What we're building

Two surfaces on one VM, sharing one Unity install, one license, one Claude subscription, and N git worktrees:

- **Interactive Chat (Track A)** — a browser-accessible `claude` TUI running inside a git worktree on the VM. Open from phone or laptop, work normally, leave the session running, come back. Same `claude` you'd run locally — just persistent and reachable.
- **Agent Dispatcher (Track B)** — accepts task requests (REST / webhook / GitHub Actions), drives `claude` against a free worktree slot, streams logs, pushes a branch + opens a PR. Two execution models supported (see §2):
  - **Session-driven** (long-lived tmux + send-keys) — fits "iterate with the same agent over time", "small fix", "follow-up question."
  - **Fire-and-forget** (fresh worktree + GH Actions run) — fits "implement this feature from scratch, push PR, done."

Both surfaces are the same underlying primitive: **an interactive `claude` TUI in a tmux session.** Track B just programmatically drives what Track A exposes manually.

---

## 2. Key principles

These are the load-bearing decisions everything else follows from. Re-read before changing the shape of the plan.

### 2.1 Subscription-only, no API

The whole plan is structured around billing against the existing Claude Max subscription. Anthropic's policy (Feb 2026 + April 2026 updates) reserves subscription pricing for **Anthropic's own surfaces only**: Claude.ai, the official `claude` CLI, and Claude Code on the web. Any third-party harness using the Agent SDK or OAuth tokens directly = API-billed. The strict reading we follow:

- ✅ **Safe**: running the official `claude` binary (TUI or `-p`) anywhere — VM, container, GH Actions runner, whatever. Wrapping shell scripts around it is fine because the binary itself is "the surface."
- ❌ **Unsafe**: Claude Agent SDK, LangGraph-with-Anthropic-nodes, Flue, any framework that calls Anthropic via OAuth tokens lifted from the CLI's credentials file. Forces API billing or hits ToS.

This rules out a category of "agent framework" tools and forces all orchestration to be **process-level** (spawning the CLI), not **API-level** (calling Anthropic).

### 2.2 The tmux-harness pattern

Instead of the SDK pattern (frameworks spawn typed "agents" that talk to Anthropic), we use a flipped model: **the agent is just an interactive `claude` TUI in a tmux session; the harness sits *around* it and drives it via send-input / fetch-output.**

Why this is interesting:

- **One auth model** (subscription) for chat and automation alike.
- **Persistent sessions** = no cold start on follow-ups. "Fix that lint error you just made" is sub-second; you don't spin up a new agent, you poke the existing one.
- **Natural shared mental model** between human chat and automated dispatch — they're the same thing, just one has a webhook instead of a keyboard.
- **Right-sized for small fixes** that wouldn't justify a full new agent run.
- **Composable with fire-and-forget** for the cases that want a fresh worktree per task (long features, parallel exploration).

Tools in this space worth referencing (read-only — see §5 trust filter): `Herdr`, `claude-bridge`, the various tmux+REST control-mode wrappers. We likely write our own thin wrapper (~150 lines), borrowing patterns.

### 2.3 Trust filter on dependencies

Not all OSS is equal. We grade tools before adopting:

| Tier | What | How we use it |
|---|---|---|
| **Enterprise / SaaS / industry-standard** | GitHub, Hetzner, Daytona, Tailscale, Windmill, n8n, code-server, ttyd, tmux | Adopt freely. |
| **High-trust OSS with strong adoption (>10k stars, multi-year history, active maintainer base)** | `siteboon/claudecodeui` (~11k) | **High-alert**: security review, supply-chain check, sandboxed first run, validate auth/token handling before pointing it at the subscription. |
| **Smaller OSS** | `Herdr`, `Flue`/PyFlue, `HolyClaude`, `sugyan/claude-code-webui`, `claude-bridge`, similar | **Reference-to-copy only**: read the source for patterns, do not install or pipe a token through. |

This is a defensive default — Anthropic OAuth tokens and a Unity-equipped VM are valuable enough that "I trust this random repo" is the wrong heuristic.

### 2.4 Local first, VM second

Every component is validated on the user's existing machine before moving to Hetzner. The migration is then mostly `rsync + systemd units + tailscale up`. This derisks the unknowns (Unity + worktrees behavior, claude subscription drive-through, tmux harness viability) before any infra spend.

---

## 3. Final shape

```
┌─────────────────────────────────────────────────────────────────┐
│                External (you, GitHub)                           │
└──────┬──────────────────────┬─────────────────────┬─────────────┘
       │ browser/SSH          │ POST /sessions/...  │ webhook /
       │ via Tailscale        │ (small fix, poke)   │ workflow_dispatch
       ▼                      ▼                     ▼
   ┌────────┐         ┌───────────────────┐    ┌──────────────────┐
   │ ttyd / │         │ Session manager   │    │  GH Actions      │
   │ code-  │◄────────┤ (tmux + REST,     │    │  self-hosted     │
   │ server │ attach  │  Track B1)        │    │  runner          │
   └───┬────┘         └────────┬──────────┘    │  (Track B2)      │
       │                       │               └────────┬─────────┘
       │ humans                │ webhooks/scripts       │ fresh job
       │                       │ send keys / read pane  │ checkout
       ▼                       ▼                        ▼
              ┌───────────────────────────────────────────┐
              │     tmux sessions (one per worktree)      │
              │       ▼                                   │
              │     claude TUI (subscription-billed)      │
              └────────────────────┬──────────────────────┘
                                   ▼
              ┌───────────────────────────────────────────┐
              │  ~/work/main  ~/work/slot1  ~/work/slot2  │
              │  (git worktree; own Library/ each)        │
              └────────────────────┬──────────────────────┘
                                   ▼
              ┌───────────────────────────────────────────┐
              │  Unity -batchmode + Unity Accelerator     │
              └───────────────────────────────────────────┘
```

**Hardware**: one Hetzner CCX33 (8 vCPU / 32 GB / 240 GB NVMe), Ubuntu 24.04, behind Tailscale.

**Auth**: `claude` authenticated via `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`). `ANTHROPIC_API_KEY` is unset everywhere — verified by hook on shell startup.

**Concurrency**: 1 main worktree + 2 agent slots initially (3 tmux sessions). Easily grown to 4–5 when warranted.

---

## 4. What we can do once it's built

After **Track A** (interactive only):
- Open a browser tab from anywhere → live `claude` session inside the Unity project, persistent.
- Disconnect, reconnect, session is intact.
- Work from phone, work while desktop is off.
- Run multiple parallel chat sessions, one per worktree/branch.

After **Track B** (agent layer added):
- `POST /sessions/{slot}/input "implement X"` → existing session picks it up, no cold start.
- Comment `@bot implement issue #123` on a PR → GH Actions workflow runs in a fresh worktree, pushes PR.
- Call `workflow_dispatch` from a script anywhere → ephemeral run.
- "Poke" running sessions with follow-ups for small fixes.
- 2–3 Unity batch-mode tasks in parallel, warm Library cache per worktree.

**Non-goals** (explicitly out of scope for v1):
- No multi-VM scaling. One box.
- No GPU / no Editor with rendering. Batch mode only — visual verification on local Unity.
- No durable workflow engine (Temporal/Restate). Tmux + GH Actions = enough.
- No custom Anthropic-facing harness. Official CLI is the only thing calling Anthropic.

---

## 5. Services & tools

### Required
| Tool | Purpose | Trust tier |
|---|---|---|
| Hetzner Cloud | The VM | Enterprise |
| Tailscale | Private network, no public ports | Enterprise |
| GitHub | Repo, PRs, Actions self-hosted runner, webhooks | Enterprise |
| Claude Max | Auth for `claude` | Enterprise (already paid) |
| Unity Personal license | Unity Editor (free, revenue-cap dependent) | Enterprise — verify EULA |
| `claude` CLI | The agent | Enterprise (Anthropic official) |
| `tmux` | Persistent sessions, programmatic drive | System tool |
| `ttyd` or **code-server** | Browser access to TUI | Standard OSS — small, well-known |
| GitHub Actions self-hosted runner | Dispatch via `workflow_dispatch` / webhooks | Enterprise |
| GitHub PAT (fine-grained) | Runner auth + dispatcher PR pushes | Enterprise |

### Optional
| Tool | Purpose | Trust tier |
|---|---|---|
| Windmill (self-host) | Alt control plane if we outgrow GH Actions | Enterprise-grade OSS |
| Caddy | TLS + reverse proxy | Standard OSS |
| Domain + Cloudflare DNS | Nicer URLs | Standard |
| Unity Accelerator | Shared asset import cache | Enterprise (Unity official) |

### Under evaluation (do NOT install until reviewed)
| Tool | Purpose | Trust tier | Action |
|---|---|---|---|
| `siteboon/claudecodeui` | Nicer chat UI than ttyd, mobile-first, purpose-built for `claude` | High-alert OSS (~11k stars) | Security review + sandboxed trial before considering. ttyd/code-server are the default. |

### Reference-only (do NOT install — read for patterns)
- `Herdr` (tmux-driving harness pattern)
- `Flue` / `PyFlue` (agent framework — ruled out by §2.1 anyway)
- `HolyClaude` (Docker chat-UI bundle)
- `sugyan/claude-code-webui` (small claude wrapper)
- `claude-bridge` and similar tmux/PTY wrappers

---

## 6. Costs

| Item | Monthly | Notes |
|---|---|---|
| Hetzner CCX33 | ~€57 (~$62) | Can start on CCX23 (~€29) and scale. |
| Hetzner backups (20%) | ~€11 | Recommended on at first. |
| Tailscale | $0 | Personal plan. |
| Claude Max | (already paid) | — |
| Unity Personal | $0 | Free if revenue cap qualifies. |
| GitHub | $0 | Free tier covers self-hosted runners on private repos. |
| Domain | ~$1/mo | Optional. |
| **Total new spend** | **~$60–75/mo** | One line item. |

**Anthropic billing risk (June 15, 2026)**: `claude -p` headless mode moves to a separate Agent SDK credit pool at API rates. Interactive TUI sessions stay on Max quota.
- **Track A is unaffected** (interactive TUI).
- **Track B-session** (tmux + send-keys against an interactive `claude`) is unaffected — drives the TUI, not `-p`.
- **Track B-fire-and-forget** via GH Actions running `claude -p` *will* be API-billed post-June 15. Acceptable for v1; the session-driven path is the long-term default for cost.

---

## 7. Track A — Interactive (start here)

Outcome: live `claude` chat in a browser from anywhere, against a worktree of this repo. Desktop can be off. No agent layer yet.

### Phase A0 — Local prerequisites (1 hour)
- [ ] `claude /status` → confirm Max subscription is active.
- [ ] `env | grep -i anthropic` → confirm **no** `ANTHROPIC_API_KEY` set. If present, unset and verify `/status` still shows Max.
- [ ] Confirm Unity Personal EULA eligibility for our entity. If not eligible, full plan needs a re-cost with Pro (~$2,200/yr/seat). **Blocker for everything below.**

### Phase A1 — Local worktrees + Unity smoke test (½ day)
- [ ] `git worktree add ../proj-slot1 <some-branch>` next to existing checkout. Add a second.
- [ ] Run `Unity -batchmode -nographics -quit -projectPath ../proj-slot1 -logFile -` and confirm clean import + exit 0.
- [ ] Run two batch-mode jobs simultaneously against the two worktrees — confirm no UnityLockfile conflict, both succeed.
- [ ] Note Library/ disk usage per worktree for VM sizing.
- [ ] **Gate**: if any of the above fails, the whole worktree premise is wrong — stop and rethink before provisioning.

### Phase A2 — Local tmux + claude TUI (1 hour)
- [ ] `tmux new -s chat -c ../proj-slot1` → `claude` inside.
- [ ] Detach (`C-b d`), reattach (`tmux attach -t chat`). Confirm session survives.
- [ ] Send a trivial task, confirm it executes against the worktree.

### Phase A3 — Local web terminal (½ day)
- [ ] Install `ttyd` (or `code-server` if we want the editor too).
- [ ] Launcher: `tmux new-session -A -s chat -c ../proj-slot1 claude`.
- [ ] `ttyd -i 127.0.0.1 -p 7681 ./start-chat.sh` — open in laptop browser, confirm TUI works.
- [ ] (Optional) `cloudflared tunnel --url http://localhost:7681` to test phone access end-to-end before the VM exists.
- [ ] **Decision point**: if ttyd's mobile UX is fine, lock it in. If it's painful, queue a security review of `siteboon/claudecodeui` as the Phase A7 upgrade — do **not** install it until reviewed.

### Phase A4 — VM provisioning (½ day)
- [ ] Hetzner: create CCX33, Ubuntu 24.04, SSH key, optional 100 GB volume for Library/.
- [ ] Harden: non-root user, `ufw` allow 22 only, unattended-upgrades, fail2ban.
- [ ] Tailscale up on VM + laptop + phone. SSH over Tailscale works.
- [ ] Install base: `git`, `tmux`, `build-essential`, `python3.12`, `nodejs`, `ttyd`.

### Phase A5 — VM Unity install + license (½ day)
- [ ] Install Unity Hub headless + dependencies.
- [ ] Install Editor at `ProjectSettings/ProjectVersion.txt` version.
- [ ] License activation: `-createManualActivationFile`, complete on Unity site, `-manualLicenseFile`. **Once**, never per-job.
- [ ] Install Unity Accelerator on the same box.
- [ ] Disable Parallel Import in `EditorSettings.asset` (Linux headless bug).
- [ ] Smoke test: clone repo to `~/work/main`, run batch-mode import to completion.

### Phase A6 — VM worktrees + claude auth (¼ day)
- [ ] `git worktree add ~/work/slot1 <branch>` and `slot2`. Confirm independent Library/.
- [ ] On laptop: `claude setup-token` → 1-year OAuth token.
- [ ] On VM: `CLAUDE_CODE_OAUTH_TOKEN=...` in `/etc/profile.d/claude.sh`.
- [ ] Audit: `env | grep -i anthropic` on VM shows OAuth token only, no API key.
- [ ] `claude /status` in a worktree → confirms Max.

### Phase A7 — VM chat deployment (½ day)
- [ ] systemd user units for: `tmux-main.service` (persistent tmux session with `claude` in `~/work/main`), `ttyd-chat.service` (binds to `tailscale0`, attaches to `tmux-main`).
- [ ] (Optional) Caddy in front for `https://chat.<domain>` with TLS.
- [ ] Test from phone over Tailscale → live `claude` TUI in a browser.
- [ ] **Done with Track A.** Desktop can be off. Work from anywhere.

---

## 8. Track B — Agent (after A is solid)

Outcome: programmatic dispatch into the same `claude` sessions Track A exposes, plus a fire-and-forget path for fresh-worktree tasks.

### Phase B0 — Decide execution split (1 hour, no code)
- [ ] Read §2.2. Decide v1 scope:
  - [ ] **B-session only** (tmux + send-keys wrapper, REST + webhook on top). Long-term cost-optimal. ~1–2 days build.
  - [ ] **B-fire-and-forget only** (GH Actions self-hosted runner + workflow.yml). Faster to ship (~½ day) but API-billed post-June 15.
  - [ ] **Both** (start fire-and-forget for speed, add session-driven for cost + small-fix UX). Recommended.
- [ ] Decide concurrency: 2 slots (CCX33 comfy) or 4 (needs CCX43 or memory check).

### Phase B1 — Local session-manager prototype (1–2 days)
Validate the tmux-harness pattern on the laptop before deploying.

- [ ] Tiny FastAPI service, single file, in-memory session registry:
  - `POST /sessions {name, worktree}` → `tmux new-session -d -s <name> -c <worktree> claude`
  - `GET /sessions` → list with `tmux list-sessions -F`
  - `POST /sessions/{name}/input {text}` → `tmux send-keys -t <name> "<text>" Enter`
  - `GET /sessions/{name}/output?lines=N` → `tmux capture-pane -t <name> -p -S -<N>` (with ANSI strip)
  - `WS /sessions/{name}/stream` → tail capture-pane on interval
  - `DELETE /sessions/{name}` → `tmux kill-session -t <name>`
- [ ] Read patterns from `Herdr` and `claude-bridge` (reference-only, do not install).
- [ ] Test loop locally: create a session, send a prompt, fetch output, send a follow-up — confirms the "small fix" UX works.
- [ ] **Gate**: if send-keys + capture-pane is too fragile to drive `claude` reliably, fall back to `pexpect`/`node-pty` PTY mode, or accept Path B-fire-and-forget only.

### Phase B2 — Local fire-and-forget prototype (½ day)
- [ ] Write `.github/workflows/agent-task.yml` with `workflow_dispatch` inputs `(branch, prompt)`.
- [ ] Workflow steps: checkout branch in worktree slot, `claude -p "$PROMPT"`, commit, push, open PR via `gh`.
- [ ] Install GH Actions self-hosted runner on the laptop, labeled `unity-agent`.
- [ ] Trigger via `gh workflow run agent-task.yml -f branch=test -f prompt=...`. Watch live logs in GitHub UI.
- [ ] Confirm PR appears.

### Phase B3 — VM deployment (½ day)
- [ ] Move session-manager service to VM as systemd unit (bind to Tailscale interface only).
- [ ] Install GH Actions self-hosted runner on VM, labeled `unity-agent`.
- [ ] Re-run smoke tests from B1/B2 against the VM.

### Phase B4 — Webhook trigger (½ day)
- [ ] GH workflow: on `issue_comment.created` matching `@bot <prompt>` → trigger `agent-task.yml` (fire-and-forget) or POST to session-manager (session-driven), depending on prefix.
- [ ] Verify HMAC signature handling if we expose any direct webhook endpoint (vs. routing through GH).
- [ ] Test by commenting on a real PR.

### Phase B5 — Minimal control UI (½ day, optional)
- [ ] One-page htmx UI listing tmux sessions, with input box + log pane per session.
- [ ] Same surface area as the REST API; just a thin frontend.
- [ ] Skip if `tmux ls` + `tmux attach` from a terminal is good enough.

### Phase B6 — If `siteboon/claudecodeui` passes review (½ day, optional)
- [ ] Only if Phase A3/A7 surfaced ttyd UX pain *and* the security review of claudecodeui passed.
- [ ] Sandbox first: separate VM user, no PAT in environment, route through Caddy with strict CSP.
- [ ] Run in parallel with ttyd, not replacing it, until confidence builds.

---

## 9. Risks & landmines

- **April 2026 third-party-harness restriction** — confirmed: subscription billing only valid for Anthropic's own surfaces. Mitigation: never call Anthropic from our code; always shell out to the official `claude` binary. Re-validate quarterly.
- **June 15, 2026 `-p` billing split** — Track B-fire-and-forget becomes API-priced. Mitigation: session-driven path (B-session) stays on subscription; switch defaults to it if API cost shows up.
- **Unity EULA on Personal for headless** — fuzzy "one machine" clause when "machine" is a VM. If borderline, need Pro. **Phase A0 blocker.**
- **Anthropic could harden against PTY-driving the TUI** — Path B-session relies on the interactive CLI being scriptable via tmux. If they detect/block this, fallback is API-billed dispatch. Watch policy updates.
- **Unity headless Parallel Import bug** — known, disabled by config. Re-test on each Editor version bump.
- **`siteboon/claudecodeui` is a high-alert dependency** — handles tokens and shells, ~11k stars but smaller maintainer base than enterprise tools. Do not adopt without review.
- **Single VM = single point of failure** — Hetzner outage stops everything. Acceptable for solo dev; revisit when load-bearing.
- **tmux send-keys quoting** — prompts with special characters need careful escaping (or write to a file and have `claude` read it).

---

## 10. Open questions

- [ ] Track B execution split: session-only, fire-and-forget-only, or both? (Recommendation: both, session-driven as the long-term default.)
- [ ] Chat UI: ttyd vs. code-server (full editor) vs. queue siteboon/claudecodeui review? (Recommendation: ttyd, queue claudecodeui as Phase B6 only if pain.)
- [ ] Webhook trigger now (Phase B4) or later?
- [ ] CCX33 vs CCX43? Depends on whether 2 or 4 concurrent Unity jobs is realistic.
- [ ] Library/ on root disk vs. separate Hetzner Volume? (Volume = cheaper per GB, detachable for backup, slightly slower I/O.)
- [ ] Should the session-manager auto-restart `claude` if it crashes inside tmux? (Probably yes — supervisor pattern.)
- [ ] Do we want a "watchdog" Claude on the VM that monitors PR CI and auto-fixes? Out of scope for v1, but the infra supports it.

---

## 11. Findings & journey notes

Captures the research that shaped this plan, so future revisits don't re-derive from scratch.

### Unity + git worktrees (verified, green light)
- Unity locks per project path, not per repo. N worktrees with N independent Library/ folders work concurrently. Same pattern GameCI uses.
- Mitigations baked into the plan: Unity Accelerator (shared cache), disable Parallel Import on Linux headless (known bug), activate license once, never symlink PackageCache across worktrees.
- Library/ is 5–20 GB per worktree — sized CCX33 around this.

### Claude Code subscription on a remote VM (verified, with caveats)
- `claude setup-token` produces a 1-year OAuth token. Export as `CLAUDE_CODE_OAUTH_TOKEN` on the VM. Billing flows through Max.
- `ANTHROPIC_API_KEY` silently overrides OAuth — must be unset.
- Personal-device use across multiple devices is allowed per ToS.
- **Feb 2026 policy**: Agent SDK + OAuth tokens = banned. Affects framework choice (§2.1).
- **April 2026 policy**: third-party harnesses blocked from Max billing. Reinforces "official CLI only" rule.
- **June 15, 2026 policy**: `-p` headless mode moves to separate Agent SDK credit pool at API rates. Interactive TUI unaffected. Drives the §2.2 tmux-harness pattern as the long-term default.

### Off-the-shelf platform survey (no single product fits)
- **Claude Code on the web** (Anthropic-hosted): only managed product that bills Max natively, has Routines for dispatch. Killed by 30 GB disk cap (Unity install + project is 15–25 GB, tight) and no base-image override (Unity reinstalls every cold start, ~10 min tax). Worth a 30-min experiment but not the foundation.
- **Coder / Gitpod / Daytona** (self-host CDEs): give "VM + dev env" but neither chat nor dispatch nor subscription auth. Same shape as our plan, more moving parts.
- **Cursor BG agents / Devin / Factory / OpenHands / Replit Agent**: all bill on their own model or force API. Subscription-incompatible.
- **HolyClaude** (Docker bundle): interesting reference, smaller maintainer base — read-for-patterns only.

### Off-the-shelf composition (collapses the build)
- **GitHub Actions self-hosted runner** replaces a custom FastAPI dispatcher for the fire-and-forget half. `workflow_dispatch` REST = dispatch API. Runner = executor. GH UI = log streaming (mobile-friendly, free). Enterprise trust.
- **Windmill** is the backup if we ever want a non-GitHub control plane (AGPLv3, well-funded). n8n is a similar tier.
- **ttyd + tmux** covers the chat UI surface with system-tool-grade trust. `siteboon/claudecodeui` is the upgrade path if mobile UX needs more — gated on security review.

### The tmux-harness pattern (new framing)
- Inverts the SDK model: instead of frameworks spawning typed "agents," persistent interactive `claude` sessions are the agents; the harness drives them via send-input / fetch-output.
- Tools in the space (reference-only): Herdr, claude-bridge, various tmux+REST wrappers.
- Implications: one auth model for chat + automation, no cold start on follow-ups, naturally cost-optimal post–June 15, simpler mental model.

### Local-validate-then-VM-deploy (recommended ordering)
- Phases A1–A3 and B1–B2 can all run on the user's existing machine.
- Migration to VM = `rsync + systemd units + tailscale up`, not a re-architecture.
- Derisks every unknown (worktrees, subscription billing, tmux-harness viability) before infra spend.
