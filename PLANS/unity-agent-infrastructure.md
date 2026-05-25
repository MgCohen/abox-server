---
type: plan
status: draft
tags: [#infra, #claude-code, #unity, #agents, #remote, #tmux]
---

# Unity Agent Infrastructure — implementation plan

> **Goal.** Stand up a single always-on Linux VM that hosts Unity (headless) and multiple git worktrees of this repo, so Claude Code can drive Unity-aware work on N branches in parallel — both as **remote chat sessions** I open from anywhere, and as **dispatched agent runs** triggered via `gh workflow run` or REST. Replaces the current "leave my desktop on 24/7 and RDP in" workflow.
>
> **Ordering.** Interactive chat first (Track A) — it solves the immediate pain (desktop-off, work-from-phone) with the least moving parts. Agent dispatch second (Track B) — built on top of the same VM, same auth, same worktrees.

---

## Current state (last update 2026-05-21)

**Branch**: `phase-a/local-validation` (commits ahead of `main`: A0 → A1 → A2 rewrite → A2 reshape). No PR open yet.

**Resume here**: ⛔ **PAUSED — Unity licensing decision required.** A3.0 (Docker Desktop) ✅ and A3.1 (research gate) ✅ completed. A3.2 hit a hard block: **Unity Personal in 2026 cannot be activated inside a Linux container** (modern Unity uses XML license format bound to hardware fingerprint — machine-id + MAC + hostname — and manual `.alf`/`.ulf` activation for Personal was removed in Aug 2023). Full finding in §11 "Unity Personal in containers — blocking discovery (2026-05-25)." Decision required before any further work; three options laid out in that note. Once decided, A3.2 + later phases get rewritten and we resume.

| Phase | Status |
|---|---|
| A0 — Local prerequisites | ✅ subscription confirmed (Max, firstParty), `infra/check-a0.ps1` codifies the gate |
| A1 — Worktree + Unity batch-mode concurrency | ✅ validated on vanilla Unity 6000.3.11f1 (parallel imports exit 0; shared `LicensingClient` confirmed); Scaffold cold-build issue spawned as a separate task |
| A2 — Local Tailscale validation (reshaped) | ✅ W1–W3 done: laptop + phone joined tailnet, bidirectional `tailscale ping` confirmed. Browser-terminal validation moved to A3 / A4 (Linux primitives) after two Windows-native install attempts failed (code-server, code serve-web). See §11 for the journey. |
| A3 — Local Docker validation (Linux primitives, no VM) | ⛔ **BLOCKED at A3.2** by Unity Personal licensing model change (see §11). A3.0 + A3.1 ✅; A3.5/A3.6 (non-Unity ttyd+tmux+claude) could still run independently if licensing decision is deferred. |
| A4 — Linux VM: chat layer (Pass 1) | ⏸ Provision VM (free credit), install ttyd + tmux + claude, phone access via Tailscale. **No Unity yet.** Validates the chat layer on a non-personal-device host. |
| A5 — Linux VM: Unity layer (Pass 2 start) | ⏸ `docker pull unityci/editor:ubuntu-6000.3.11f1-...`, license activation once on host. **GameCI image is the substrate** — we take the image only, not the GitHub-Actions workflow. |
| A6 — VM worktrees + first batchmode in container | ⏸ |
| A7 — Permanent service (systemd) | ⏸ |
| Track B (agent dispatch) | ⏸ Comes after Track A is solid |

**Load-bearing constraints to re-load on resume**:
- **No Linux / WSL on the local Windows machine.** A2 reshape acknowledged this: browser-terminal validation belongs on Linux. Local Windows is for editing + Tailscale client only.
- **Multi-Unity-project host.** `C:\Unity\` holds Card Framework, Scaffold, Gear-Engine, and growing. VM layout mirrors this as `~/work/<project>/{main,slotN}/`.
- **Two-pass validation discipline.** Pass 1 (A4) validates the chat layer with no Unity. Pass 2 (A5–A6) adds Unity. The split isolates failures.
- **GameCI image only, not GameCI workflow.** We pull `unityci/editor` for Unity-on-Linux primitive. License activation stays manual on the host (`-createManualActivationFile` → upload `.alf` → download `.ulf` → mount into container). We never adopt `unity-license-activate` / `unity-builder` actions — those don't fit our persistent-session model.
- **Ubuntu 22.04 LTS on VM**, not 24.04 — Unity has more bug-repros on 24.04 (research note §11).
- **Free-tier validation before paid spend.** GCP $300 / 90-day credit, Hetzner referral €20, or Oracle Always Free can validate A4–A6 at $0. Paid Hetzner CCX23 (~€29/mo) is the target for graduation; CCX33 (~€57/mo) is the multi-project end state.
- **Side task open**: Scaffold's `com.scaffold.schemas` is committed at `Assets/Packages/` AND declared as a git URL in `manifest.json` → 50+ GUID conflicts on cold build. Spawned task addresses this; doesn't block this branch but must be resolved before Scaffold can be deployed to the VM.
- **Empty Unity dirs don't survive git** (Assets/ in particular) — any worktree-provisioning script must handle this.

---

## How to execute this plan (executor notes)

Read in this order before doing anything: **§2 (Key principles)** — non-negotiable; **§5 (Services & tools)** — the binding trust filter; **§7 Track A** — start here. Track B is only after A0–A7 are all checked. §11 has the research history if you need to understand *why* a decision is the way it is.

- **Gates that stop the world**:
  - **A0** — if Unity Personal EULA doesn't qualify for our entity, or `ANTHROPIC_API_KEY` won't unset cleanly, stop and surface to the user. Do not work around. The whole plan re-costs if either fails.
  - **A1** — if Unity batch-mode against two concurrent worktrees has any conflict, stop. The worktree premise is wrong and the plan needs rework.
  - **Phase boundary** — complete one phase, tick the checkboxes in this file, commit, summarize findings, wait for user go-ahead before starting the next. Exception: if the user says "run through A unattended," chain A1→A7 but still stop at any failed gate.
- **Trust filter is binding (§5)**: do not `npm install`/`pip install`/`git clone` anything outside the "Required" and "Optional" tables. `siteboon/claudecodeui` is "Under evaluation" — gated on a separate security review, do not install. "Reference-only" tools (Herdr, Flue, HolyClaude, sugyan/claude-code-webui, claude-bridge) are for reading source on GitHub, never cloning or installing.
- **Subscription audit before any `claude` invocation**: run `claude auth status` — must return JSON with `subscriptionType: "max"` and `apiProvider: "firstParty"`. Also check `ANTHROPIC_API_KEY` is unset *or* empty-string (the Claude Code harness itself injects an empty value into its own subprocess env, which is harmless; a non-empty value silently overrides OAuth and forces API billing — stop and surface if seen). `infra/check-a0.ps1` codifies both checks. (Note: `claude /status` is a TUI-only slash command and can't be used as a non-interactive gate; `claude auth status` is the scriptable equivalent.)
- **Artifact discipline**: any reusable script, systemd unit, workflow YAML, or config you produce goes into `infra/` on this branch (create the directory if needed). Inline one-off commands run on the user's machine don't get committed. Plan-doc checkbox updates + script commits happen together at each phase boundary.
- **VM access**: phases A4 onward need SSH to the Hetzner box. Until provisioned, do not write scripts assuming SSH access — write them as files the user/you will later `scp` or `git pull` onto the VM.

---

## 1. What we're building

Two surfaces on one VM, sharing one Unity install, one license, one Claude subscription, and N git worktrees:

- **Interactive Chat (Track A)** — a browser-accessible `claude` TUI running inside a git worktree on the VM. Open from phone or laptop, work normally, leave the session running, come back. Same `claude` you'd run locally — just persistent and reachable.
- **Agent Dispatcher (Track B)** — accepts task requests (REST to session-manager, or `gh workflow run` for fire-and-forget), drives `claude` against a free worktree slot, streams logs, pushes a branch + opens a PR. Two execution models supported (see §2):
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
- **Natural shared mental model** between human chat and automated dispatch — they're the same thing, just one has a programmatic trigger (REST / `gh workflow run`) instead of a keyboard.
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

### 2.4 Local first, VM second (revised after A2 reshape)

Every Linux-substrate question is validated on the laptop before any cloud spend, but **not via Windows-native tooling** (we tried twice in A2, both failed — see §11). Instead, Phase A3 uses Docker Desktop (WSL2 backend, hidden runtime) on the Windows host to run the same `unityci/editor` container, `ttyd + tmux + claude` container, and concurrent batchmode tests that the VM will run. From Docker's perspective there's no difference between "container on the laptop" and "container on the cloud VM" — same image, same x86, same syscalls.

What that validates locally: GameCI image + our projects, worktree+volume-mount semantics, claude TUI in ttyd on mobile, tmux send-keys reliability, concurrent batchmode behavior.

What still requires the cloud VM (A4+): OAuth billing on a non-personal-device host, cloud-provider-specific provisioning, real systemd-on-Ubuntu instead of Docker-managed PID 1.

Migration to VM is **not** `rsync from laptop` — it's a fresh Ubuntu 22.04 install per A4.1-A4.3. Reusable across hosts: GameCI image (pinned by digest), `.ulf` license file, our `infra/` scripts. Everything else gets rebuilt from the plan.

---

## 3. Final shape

```
┌─────────────────────────────────────────────────────────────────┐
│                External (you, GitHub)                           │
└──────┬──────────────────────┬─────────────────────┬─────────────┘
       │ browser/SSH          │ POST /sessions/...  │ gh workflow run /
       │ via Tailscale        │ (small fix, poke)   │ workflow_dispatch
       ▼                      ▼                     ▼
   ┌────────┐         ┌───────────────────┐    ┌──────────────────┐
   │ ttyd   │         │ Session manager   │    │  GH Actions      │
   │ (chat) │◄────────┤ (tmux + REST,     │    │  self-hosted     │
   │        │ attach  │  B-session path)  │    │  runner          │
   └───┬────┘         └────────┬──────────┘    │  (B-fire-and-    │
       │                       │               │   forget path)   │
       │                       │               └────────┬─────────┘
       │ humans                │ REST / scripts         │ fresh job
       │                       │ send keys / read pane  │ checkout
       ▼                       ▼                        ▼
              ┌───────────────────────────────────────────┐
              │     tmux sessions (one per worktree)      │
              │       ▼                                   │
              │     claude TUI (subscription-billed)      │
              └────────────────────┬──────────────────────┘
                                   ▼
              ┌───────────────────────────────────────────┐
              │  ~/work/<project>/main   ~/work/<project>/slot1  │
              │  (one tree per Unity project × N worktrees;       │
              │   each worktree gets its own Library/)            │
              └────────────────────┬──────────────────────┘
                                   ▼
              ┌───────────────────────────────────────────┐
              │  Unity -batchmode (via GameCI image:      │
              │  `unityci/editor`, per-Editor-version)    │
              └───────────────────────────────────────────┘
```

**Project scope**: the VM hosts **multiple distinct Unity projects** (e.g. `Card Framework`, `Scaffold`, …), each living under `~/work/<project>/` with its own `main` worktree plus N agent slot worktrees. Unity is not installed on the host; instead, one **GameCI Docker image (`unityci/editor`) is pulled per Editor version** pinned in `ProjectSettings/ProjectVersion.txt`. License activation happens once for the machine (one `.ulf` shared across all images, mounted read-only into each container).

**Hardware (default end-state, Hetzner path)**: one Hetzner CCX33 (8 vCPU / 32 GB / 240 GB NVMe), Ubuntu 22.04 LTS, behind Tailscale. (CCX23 / 4 vCPU / 16 GB is the validation-budget floor; resize/upgrade to CCX33 once Pass 2 passes.) **GCP path**: `n2-standard-4` (4 vCPU / 16 GB) equivalent — pricier per spec but the $300 / 90-day credit covers the whole validation window. Decision made at A4.1.

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
- `gh workflow run agent-task.yml -f branch=... -f prompt=...` from any device with `gh` installed (laptop, phone with Termius+gh, anywhere) → fresh-worktree ephemeral run, opens PR.
- "Poke" running sessions with follow-ups for small fixes.
- 2–3 Unity batch-mode tasks in parallel, warm Library cache per worktree.
- *(Post-v1, if commenting becomes the dominant trigger)*: `@bot implement issue #123` on a PR → same dispatch. Deferred per §10.

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
| Hetzner Cloud (or GCP / Oracle free tier for validation) | The VM | Enterprise |
| Tailscale | Private network, no public ports | Enterprise |
| GitHub | Repo, PRs, Actions self-hosted runner (`workflow_dispatch` triggers; webhooks deferred per §10) | Enterprise |
| Claude Max | Auth for `claude` | Enterprise (already paid) |
| Unity Personal license | Unity Editor (free, revenue-cap dependent) | Enterprise — verify EULA |
| `claude` CLI | The agent | Enterprise (Anthropic official) |
| `tmux` | Persistent sessions, programmatic drive | System tool |
| `ttyd` | Browser-accessible terminal on the VM | Standard OSS — small, well-known, Linux-native |
| Docker (CE on the VM) | Runtime for the GameCI Unity image | Enterprise |
| `unityci/editor` (GameCI image only) | Maintained Unity-on-Linux install | Standard OSS — **image only, not the GH Actions workflow** |
| GitHub Actions self-hosted runner | Dispatch via `workflow_dispatch` (gh CLI from any device); webhooks deferred to post-v1 per §10 | Enterprise |
| GitHub PAT (fine-grained) | Runner auth + dispatcher PR pushes | Enterprise |

### Optional
| Tool | Purpose | Trust tier |
|---|---|---|
| Windmill (self-host) | Alt control plane if we outgrow GH Actions | Enterprise-grade OSS |
| Caddy | TLS + reverse proxy + forward-auth (if we need TOTP/OIDC on ttyd) | Standard OSS |
| Domain + Cloudflare DNS | Nicer URLs | Standard |
| Authelia / Tinyauth / oauth2-proxy | OIDC/TOTP layer in front of ttyd via Caddy forward-auth | Standard OSS |
| Daytona (self-host) | Future scale path if worktree count exceeds ~4–6 active (Stripe-pattern warm pool); see §11 | Enterprise-grade OSS |

~~Unity Accelerator~~ — researched, dropped. Accelerator's value is cross-machine LAN cache; on a single-host multi-worktree setup, Asset Database V2's built-in cache delivers the same benefit. Unity's own forum guidance: "you would not need to have Unity Accelerator running for a single developer." See §11.

**Why GameCI image only, not the GameCI workflow** — GameCI is three separable pieces: (1) the `unityci/editor` Docker image with Unity pre-installed, (2) `unity-license-activate` / `unity-license-return` GitHub Actions, (3) `unity-builder` end-to-end CI workflow. We adopt (1) because it collapses Unity-on-Linux install from ~½ day of manual debugging to one `docker pull`, gives us version-pinned reproducibility, and isolates Unity's filesystem crufts inside the container. We reject (2) and (3) because they're GitHub-Actions-shaped and ephemeral-per-job — wrong shape for our persistent-host model where one license activation feeds long-lived tmux sessions for hours/days. License activation stays manual on the host (`Unity -batchmode -createManualActivationFile` → upload `.alf` → download `.ulf` → mount into container at run-time).

**Browser-terminal stack** — `ttyd + tmux` on the Linux VM. Original A2 Windows-local attempt (code-server, then `code serve-web`) failed twice (see §11) and was reshaped to "Tailscale validation only on local; browser-terminal validation moves to A4 on Linux." The two failed attempts are not retried — we don't need browser-terminal-on-Windows; we only need it on the VM, where Linux-native tools work.

### Under evaluation (do NOT install until reviewed)
| Tool | Purpose | Trust tier | Action |
|---|---|---|---|
| `siteboon/claudecodeui` | Nicer chat UI than ttyd, mobile-first, purpose-built for `claude` | High-alert OSS (~11k stars) | Security review + sandboxed trial before considering. ttyd is the default. |

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
| Hetzner CCX33 (Hetzner path) | ~€57 (~$62) | Can start on CCX23 (~€29) and scale. Free for ~30 days on Hetzner referral credit. |
| Hetzner Storage Box BX11 (backups) | ~€3.81 | 1 TB SFTP target, restic-compatible, free intra-Falkenstein egress. Replaces Hetzner Cloud built-in backups (20%, ~€11) — Storage Box is cheaper *and* survives instance loss, where built-in snapshots are tied to the project. |
| GCP n2-standard equivalent (GCP path) | ~$100 | If we end up on GCP instead of Hetzner. Free for 90 days on $300 credit. |
| Backblaze B2 (backups, GCP path) | ~$0.18 | 30 GB nightly. Replaces Storage Box if VM is on GCP. |
| Tailscale | $0 | Personal plan (6 users / unlimited devices / 50 tagged resources). |
| Claude Max | (already paid) | — |
| Unity Personal | $0 | Free if revenue cap qualifies. |
| GitHub | $0 | Free tier covers self-hosted runners on private repos. |
| Domain | ~$1/mo | Optional. |
| **Total new spend (Hetzner path)** | **~€61/mo (~$66)** | CCX33 + Storage Box + optional domain. |
| **Total new spend (GCP path)** | **~$101/mo** | Pricier per spec but generous free trial; reconsider after 90 days. |

**Anthropic billing risk (June 15, 2026)**: `claude -p` headless mode moves to a separate Agent SDK credit pool at API rates. Interactive TUI sessions stay on Max quota.
- **Track A is unaffected** (interactive TUI).
- **Track B-session** (tmux + send-keys against an interactive `claude`) is unaffected — drives the TUI, not `-p`.
- **Track B-fire-and-forget** via GH Actions running `claude -p` *will* be API-billed post-June 15. Acceptable for v1; the session-driven path is the long-term default for cost.

---

## 7. Track A — Interactive (start here)

Outcome: live `claude` chat in a browser from anywhere, against a worktree of this repo. Desktop can be off. No agent layer yet.

### Phase A0 — Local prerequisites (1 hour)
- [x] `claude auth status` → JSON shows `subscriptionType: "max"` and `apiProvider: "firstParty"`. (TUI's `/status` isn't scriptable; this is the gate.)
- [x] `ANTHROPIC_API_KEY` is **unset or empty-string** (value-based check — the Claude Code harness itself sets an empty value into subprocess env; a non-empty value silently overrides OAuth → API billing). `ANTHROPIC_BASE_URL` may be present at the default `https://api.anthropic.com` — informational only.
- [x] Confirm Unity Personal EULA eligibility for our entity. If not eligible, full plan needs a re-cost with Pro (~$2,200/yr/seat). **Blocker for everything below.**
- [x] Verification codified at `infra/check-a0.ps1` — re-runnable on laptop and (later) the VM.

### Phase A1 — Local worktrees + Unity smoke test (½ day)
- [x] Worktree mechanic validated: `git worktree add --detach C:/Unity/worktrees/<project>/slotN HEAD` produces a clean cold worktree (no Library/) that Unity treats as an independent project (its own license slot, lockfile, Library/).
- [x] Single batch-mode import: clean exit on the vanilla 6000.3.11f1 throwaway, 12.4s for an empty project (Library 11 MB → ~10 MB warm).
- [x] **Concurrent batch-mode imports against two worktrees**: both exit 0 in parallel (slot1 12.4s, slot2 10.7s, launched 3s apart). No UnityLockfile contention. Both built independent Library/.
- [x] License-IPC behavior under concurrency: first batch-mode instance spawns `Unity.Licensing.Client` and the version-specific channel `LicenseClient-mtgco-<editorVer>`; second instance attaches to the same LicensingClient instantly (connect 0.00s, handshake 0.09s). **Unity Personal serves multiple simultaneous batch-mode instances on one machine through a shared LicensingClient.** Relevant for the multi-project VM scenario.
- [x] Library/ sizing (for VM): vanilla project = ~10 MB; real projects much larger — Scaffold = 12 GB warm. **Plan for 5–20 GB Library/ per worktree per real project.**
- [x] **Gate cleared**: worktree premise holds. Proceed to A2.

**Gotchas surfaced (load-bearing for the VM):**

1. **Empty Unity directories don't survive git.** A vanilla Unity project has an empty `Assets/` directory by default; git doesn't track empty dirs, so a fresh worktree comes up without `Assets/` and Unity then fails to recognize the path as a project (printing the misleading `Couldn't set project path to: <cwd>/<input>` because it falls back to relative-path resolution). Fix: add a `.gitkeep` to any directory Unity expects to exist but might be empty, or have the worktree-setup script create empty `Assets/` if missing. **Affects A6 and Track B agent worktree provisioning.**
2. **Project repo cold-build hygiene matters.** Scaffold can't cold-build because `com.scaffold.schemas` is committed at `Assets/Packages/com.scaffold.schemas/` AND referenced as a git package in `Packages/manifest.json` (same files, same GUIDs → 50+ conflicts). The parent works only because its 12 GB Library cached a one-time GUID reassignment. **Every Unity project deployed to the VM must cold-build cleanly.** Spawned as a separate task for Scaffold; needs the same check applied to every project before VM onboarding.
3. **Stale Unity.Licensing.Client state can break new batch-mode launches.** Closing the interactive Editor while leaving the licensing daemon running put the base `LicenseClient-mtgco` channel in a half-initialized state, where new batch-mode instances connect "successfully" but then bail. First batch-mode instance after a clean state spawns a fresh LicensingClient and clears this. **Minor; document in VM bring-up: kill any orphan `Unity.Licensing.Client` before the first batch-mode run after an Editor close.**

Test artifacts (`C:\Unity\a1-vanilla`, `C:\Unity\worktrees\a1-vanilla\{slot1,slot2}`, and the partial Scaffold worktrees at `C:\Unity\worktrees\Scaffold\{slot1,slot2}`) are throwaway and can be removed without consequence.

### Phase A2 — Local Tailscale validation (reshaped)

**History.** A2 originally aimed to run a persistent browser-terminal stack on Windows, so the laptop would temporarily act as the "always-on server" during validation. We attempted this twice — first with `code-server` (failed: postinstall.sh vendor-yarn step assumes Linux/macOS; Node 24 / Node 20 both errored during the inner VS Code build), then with Microsoft's `code serve-web` (UI loaded but proved buggy: missing browser-mode extension files, unclear terminal access, menu-bar visibility issues that the owner couldn't navigate around in practice). Two failed attempts → reshape decision: **browser-terminal validation belongs on Linux, not Windows.** We're not running a Windows-native server in the end-state architecture anyway, so we stop fighting Windows and validate the Linux primitives directly on the VM in Phase A4.

**Reshape scope.** A2 now covers only the local Tailscale validation (W1–W3), which is genuinely useful: phone ↔ laptop reachability over Tailscale is the same primitive that phone ↔ VM will use in A4. W4–W10 are deleted; their goals (browser terminal, persistence, phone access, Unity from phone) move to Phase A4 + A6 on the Linux VM.

**What this means in practice.**
- Laptop is **not** a server in the end-state architecture (was an interim convenience that turned out impractical).
- Validation happens on the Linux VM with proper tools (`ttyd + tmux`), not on Windows with Microsoft's experimental `code serve-web`.
- Phone-to-VM access via Tailscale uses the same tailnet we already validated phone-to-laptop access with — no new networking to prove.

#### W1 — Tailscale on the laptop (10 min, owner action)
- [x] Sign up for Tailscale Personal (free) at `https://login.tailscale.com/start`. (Account: `tecocohen@gmail.com`.)
- [x] Install the Windows client from `https://tailscale.com/download/windows`. Run, sign in.
- [x] Laptop joined tailnet as `matheus` (100.86.249.67).

#### W2 — Tailscale on the phone (5 min, owner action)
- [x] Tailscale installed on phone (Android). Joined tailnet as `matheuss-z-fold6` (100.98.201.119). Both devices visible in admin console.

#### W3 — Phone-to-laptop reachability check (2 min)
- [x] Validated via `tailscale ping 100.98.201.119` from the laptop: 5/5 pongs returned, ~400–1500ms via DERP(fra) relay. Bidirectional tailnet routing confirmed.
- [x] **Note**: traffic is going via DERP relay, not direct UDP — likely CGNAT on home/mobile side. Harmless for HTTP from phone to VM later.
- [x] **Gate passed**: tailnet works. Proceed to A4.

**A2 complete.** Tailscale validated; browser-terminal validation deferred to A4 (Linux VM, Pass 1).

### Phase A3 — Local Docker validation of Linux primitives (½ day, no cloud spend)

**Goal**: prove the Linux/Docker/GameCI/ttyd primitives work for *our specific projects* on the Windows laptop, using Docker Desktop (WSL2 backend) as a hidden Linux runtime. Everything we'd otherwise discover for the first time on a paid VM gets discovered here at $0. A4 only starts if A3 passes.

**Why local works**: Docker Desktop's WSL2 backend gives us a real Linux kernel + containerd runtime. From Docker's perspective there's no difference between "container on my laptop" and "container on a Hetzner CCX23" — same image, same x86, same syscalls. The only things we *can't* validate locally are (a) OAuth-token billing on a non-personal-device host and (b) cloud-provider-specific provisioning. Both belong on the VM.

**The "no WSL" rule preserved**: WSL2 here is the hidden container runtime, not a dev environment we open or operate. We never SSH into WSL, never edit files there, never run `claude` there directly — we just `docker run` from PowerShell and the containers happen to live inside the WSL2 VM.

#### A3.0 — Install Docker Desktop ✅ (2026-05-22)
- [x] Installed Docker Desktop for Windows (v29.4.3) with WSL2 backend.
- [x] `docker run --rm hello-world` exit 0 — image pulled, container ran.
- [x] Host mount confirmed: `docker run --rm -v C:\Unity:/host alpine ls /host` lists all C:\Unity\* projects.
- [x] Versions captured at `infra/docker-desktop-version.txt`: Docker 29.4.3, WSL 2.7.3.0, kernel 6.6.114.1-1, Windows 26200.8457. (Originally drafted into `infra/logs/` but that's gitignored — moved to top of `infra/` since version pins are tracked artifacts, not runtime logs.)
- **Gotcha worth flagging**: WSL was in `REGDB_E_CLASSNOTREG` ("class not registered") state pre-install. Fix was `wsl --install --no-distribution` from elevated PowerShell + reboot. Documented in the version log under "Recovery notes" — same pattern likely on any fresh Windows host where WSL was never used.

#### A3.1 — GameCI image research gate (already answered — see §11)

**Conclusion (2026-05-21 research)**: ✅ **conditional pass**. The `unityci/editor` image has no ENTRYPOINT; it ships a `/usr/bin/unity-editor` wrapper that runs `xvfb-run -ae /dev/stdout Unity -batchmode "$@"`. License injection via `UNITY_LICENSE` env var is *opt-in* through hooks in `/usr/bin/unity-editor.d/`. **Pre-mounting `.ulf` at `/root/.local/share/unity3d/Unity/Unity_lic.ulf` works without any GameCI bootstrap** (the hook is inert if `UNITY_LICENSE` is unset). Filename must be exactly `Unity_lic.ulf` — no extra dots (Unity Issue Tracker bug). For non-root containers, path is `/home/<user>/.local/share/unity3d/Unity/Unity_lic.ulf`.

Caveat — "folkloric, not blessed": GameCI has no official doc for standalone usage (open request: game-ci/documentation#386). John Austin's 2020 blog ("Running Unity 2020.1 in Docker") is the closest community reference. We're somewhat pioneering — A3.2 confirms empirically.

- [ ] No new search needed; research already done. Read §11 → "GameCI image standalone confirmed" for the full finding before A3.2.

#### A3.2 — Pull GameCI image + smoke against our real projects (1 hour) ⛔ BLOCKED

**Status (2026-05-25)**: Image pulled successfully (`unityci/editor:ubuntu-6000.3.11f1-base-3.2.2`, digest `sha256:ef80dca0…8021`, 14.3 GB unpacked, `unity-editor -version` returns 6000.3.11f1). `infra/docker/unity-images.txt` committed. **Then licensing hit a wall** — see §11 "Unity Personal in containers — blocking discovery." Container can't activate Personal license; no Unity-blessed workflow for Personal-in-container exists. Three pivot options in the §11 entry; decision required before this step can complete.

The `Unity_v6000.3.11f1.alf` file at `C:\Unity\unity-license\` is now useless (Unity removed the manual ALF→ULF flow for Personal in Aug 2023). Left in place as evidence of the dead-end exploration; can be deleted once the new licensing path is decided.

- [ ] Pull the GameCI image for our pinned Editor version: `docker pull unityci/editor:ubuntu-6000.3.11f1-linux-il2cpp-<N>`. Record the image SHA256 digest.
- [ ] Activate Unity license inside the container: `docker run --rm -v C:\Unity\unity-license:/license unityci/editor:... unity-editor -batchmode -quit -createManualActivationFile -logFile -`. Outputs `.alf` file. Upload to https://license.unity3d.com/manual, get `.ulf`, save to `C:\Unity\unity-license\Unity_lic.ulf` (mode-restricted; gitignored).
- [ ] **Backup the `.ulf` immediately** to a separate location on the laptop *and* to whatever cloud backup we have available (OneDrive, B2, etc.) — same discipline as A5.4.
- [ ] Smoke against Card Framework: `docker run --rm -v C:\Unity\unity-license\Unity_lic.ulf:/root/.local/share/unity3d/Unity/Unity_lic.ulf:ro -v C:\Unity\CardFramework:/project unityci/editor:... unity-editor -batchmode -quit -nographics -projectPath /project -desiredWorkerCount 0 -logFile -`. Watch for exit 0, Library/ populated in `C:\Unity\CardFramework\Library\`.
- [ ] **Why `-desiredWorkerCount 0`**: forces zero Parallel Import workers per-invocation. Cleaner than mutating `EditorSettings.asset` (which doesn't reliably serialize the field; see §11 research) and avoids the known headless-Linux Parallel Import bug.
- [ ] Repeat for Scaffold (after the spawned Scaffold cold-build task is resolved) and Gear-Engine.
- [ ] **Gate**: at least one of our real projects must build cold via the GameCI image. If all three fail, the Unity-on-Linux substrate is fundamentally broken for us — stop and surface.

#### A3.3 — Concurrent docker-run Unity batchmode against two worktrees (½ hour)

**Heads-up from research (§11)**: GameCI docs explicitly warn that concurrent batchmode on Personal license can fail server-side seat-limit checks. Each container has its own LicensingClient daemon, but Unity's activation server may reject simultaneous activations. **This is not a fatal gate — it informs Track B concurrency planning.** If it fails, we serialize batchmode runs in B-session rather than running them in parallel.

- [ ] `git worktree add C:\Unity\worktrees\CardFramework\slot1 HEAD` and `slot2` (per A1 mechanic — handle the empty `Assets/` gotcha).
- [ ] Run two concurrent `docker run` instances of unity-batchmode, one per worktree, started ~3 seconds apart (matching A1's timing). Each container mounts its own worktree and the shared `.ulf`.
- [ ] **Outcome capture (not a hard gate)**:
  - Both exit 0 → concurrent batchmode in container works; Track B can run parallel agents.
  - One fails with license error → expected; document for Track B (B-session must serialize, B-fire-and-forget queues).
  - Both crash unrelated to license → real bug, escalate.

#### A3.4 — Worktree + container volume-mount semantics (¼ hour)
- [ ] Mount one worktree from `C:\Unity\worktrees\...\slot1` into a container, run batchmode, confirm Library/ written *back to the host filesystem* (not lost when container exits). Same for Assets changes (if any).
- [ ] Test the empty-Assets/ gotcha in container context: create a fresh worktree, do NOT seed `Assets/`, run batchmode in container — confirm same failure mode as host-native (per A1 finding #1). Then add `.gitkeep`, retest, confirm fix works in container too.
- [ ] **Gate**: container filesystem semantics match host expectations. Worktree script `infra/worktree-add.sh` design assumes this.

#### A3.5 — ttyd + tmux + claude in a Linux container, rendered from phone (1 hour)
- [ ] Build a tiny Dockerfile in `infra/docker/chat-test/Dockerfile` based on `ubuntu:22.04`:
  ```
  FROM ubuntu:22.04
  RUN apt-get update && apt-get install -y --no-install-recommends \
        curl ca-certificates git tmux ttyd ripgrep && \
      rm -rf /var/lib/apt/lists/*
  RUN curl -fsSL https://claude.ai/install.sh | bash
  ENV PATH=/root/.local/bin:$PATH
  ```
  Commit to branch.
- [ ] Run the container with `CLAUDE_CODE_OAUTH_TOKEN` passed via env var, bind ttyd to the laptop's tailnet IP: `docker run -d --name chat-test -e CLAUDE_CODE_OAUTH_TOKEN -p 100.86.249.67:7681:7681 chat-test ttyd -p 7681 --ping-interval 30 --max-clients 2 tmux new-session -A -s main claude` — matches A4.5's flag set for consistency.
- [ ] From laptop browser: `http://100.86.249.67:7681` → terminal renders, claude TUI inside.
- [ ] **From phone over Tailscale: same URL → confirm rendering.** Test scrolling, slash commands (`/help`, `/exit`), multi-line input, ANSI colors. This is the B1 render-fidelity check.
- [ ] Disconnect (close phone browser, wait 5 min, reconnect). Verify tmux session intact.
- [ ] **Gate**: if rendering is acceptable, the ttyd path is viable for A4 / A7. If rendering is broken on mobile, surface what specifically fails — informs whether we need siteboon/claudecodeui (Phase B5) earlier.

#### A3.6 — `tmux send-keys` reliability against `claude` (½ hour)
- [ ] From a host shell into the same container (`docker exec -it chat-test bash`), use `tmux send-keys -t main "list files in current directory" Enter` to programmatically send input to the running `claude`.
- [ ] Use `tmux capture-pane -t main -p -S -50` to read the response.
- [ ] Test edge cases: prompts with special characters (quotes, dollar signs, newlines), prompts longer than terminal width, rapid successive sends.
- [ ] **Gate**: send-keys + capture-pane drives `claude` reliably enough for B1 to bet on. If it's too fragile, B1 falls back to `pexpect` / `node-pty` PTY mode.

#### A3.7 — Summary + decisions
- [ ] Commit findings to `infra/a3-validation-report.md`: what worked, what didn't, what gotchas we hit that A5/A6 need to know about.
- [ ] **Phase gate**: A3.2, A3.4, A3.5 must all pass to proceed to A4. A3.3 and A3.6 are informational (if they fail, we adjust Track B, not Track A).

### Phase A4 — Linux VM: provision + chat layer (Pass 1, no Unity) (½ day)

**Goal**: prove the browser-terminal stack (ttyd + tmux + claude) works on Linux, accessible from the phone over Tailscale. This is the deferred A2 browser-terminal validation, on the right OS. No Unity yet — we explicitly want to isolate failures in the chat layer from any Unity-on-Linux complications.

**Provider choice — free-tier first.** Validate at $0 before committing to paid Hetzner:
- **GCP $300 / 90-day credit** (https://cloud.google.com/free): real x86, generous, enough to validate the *full* stack including Unity later. Recommended.
- **Hetzner referral credit (€20)**: target architecture from day one; ~10–30 days of CCX23/CX22. Need someone's referral link (r/selfhosted / r/hetzner).
- **Oracle Always Free**: 1 GB RAM x86 forever-free (Pass-1-only, not enough for Unity). Account-stability caveats.
- Paid fallback: Hetzner CCX23 (~€29/mo) — only after free-tier validation passes.

#### A4.1 — Provision (½ hour)
- [ ] Create a small VM with the chosen provider — instance shapes per provider (all ~2 vCPU / 4–8 GB; enough for Pass 1 chat layer; Pass 2 will need resize or fresh CCX23):
  - **GCP**: `e2-standard-2` (2 vCPU / 8 GB) in `us-central1` or `europe-west1`.
  - **Hetzner**: `CX22` (2 vCPU / 4 GB, ~€4/mo) for the cheapest Pass-1 trial; or jump straight to `CCX23` (4 vCPU / 16 GB, ~€29/mo) if you want one box for the whole journey.
  - **Oracle Always Free**: `VM.Standard.E2.1.Micro` (1 OCPU / 1 GB) — tight, but it works for Pass 1.
- [ ] **Ubuntu 22.04 LTS** (not 24.04 — see §11 for the Unity bug-repro reason; Pass 1 doesn't strictly need this, but we want one OS for the whole journey).
- [ ] **Disk: stay on local NVMe (root disk).** Research §11: Hetzner attached Volumes are 10-17× slower than local NVMe for Unity workloads (IOPS-bound asset import). CCX23 ships 80 GB NVMe; CCX33 240 GB — enough for 3-4 worktrees' Library/. For GCP: PD-Balanced 200-500 GB for /home; avoid Local SSD (lost on stop).
- [ ] SSH key on creation. Disable password auth.
- [ ] Note the VM's public IP.
- [ ] **Record the provider choice** in `infra/vm-host.txt` (gitignored). A7 backup destination branches on this (Storage Box for Hetzner; B2 for GCP).

#### A4.2 — Harden + Tailscale (½ hour)
- [ ] Create non-root user `agent` with sudo. Disable root SSH.
- [ ] **ufw**: enable, allow inbound 22 on the public interface *temporarily* (needed to bootstrap Tailscale via SSH from the laptop). Enable unattended-upgrades and fail2ban.
- [ ] Install Tailscale on VM: `curl -fsSL https://tailscale.com/install.sh | sh`; `sudo tailscale up`.
- [ ] Verify SSH-over-Tailscale works: from laptop, `ssh agent@<vm-tailnet-name>` succeeds; from phone (Termius or Tailscale SSH), same.
- [ ] **Then close public 22**: `sudo ufw delete allow 22; sudo ufw allow in on tailscale0`. SSH stays available over Tailscale only. The VM has zero open ports on the public interface.

#### A4.3 — Install chat-layer base + re-validate A0 gate on Linux (½ hour)
- [ ] `apt install git tmux build-essential ttyd` — note: **NOT** `nodejs` or `python3` (research confirmed `claude` native install has no Node dependency; we add others only when a phase actually needs them).
- [ ] Install `claude` CLI via the official native installer: `curl -fsSL https://claude.ai/install.sh | bash`. Installs to `~/.local/bin/claude` with background auto-update. (Alternative: signed APT repo at https://downloads.claude.ai/claude-code/apt/stable if we want apt-tracked installs — research §11.)
- [ ] Set `CLAUDE_CODE_OAUTH_TOKEN` from `claude setup-token` (run on laptop, exported once, used here). Place in `/etc/profile.d/claude.sh`, mode 0600.
- [ ] **Port the A0 gate to Linux**: write `infra/check-a0.sh` — bash equivalent of the existing `infra/check-a0.ps1`. Same gate logic: confirms `claude auth status` returns `subscriptionType: max` + `apiProvider: firstParty`, and that `ANTHROPIC_API_KEY` is unset or empty. Commit to the branch.
- [ ] Run `infra/check-a0.sh` on the VM. **Gate**: must return exit 0. If it fails (e.g., `apiProvider: console` because the token somehow routed through API, or `ANTHROPIC_API_KEY` got set by something), stop and debug before any further work. A subscription-billed run depends on this passing.
- [ ] **B2 validation (OAuth-on-cloud-VM billing)**: after one or two `claude` invocations on the VM, check the Anthropic dashboard (claude.ai/settings) and confirm usage drew from Max quota, not API credits. Research (§11) suggests this should work, but empirical confirmation closes the loop.

#### A4.4 — Chat-layer smoke test (¼ hour)
- [ ] Clone repo to `~/work/remote-unity-agents/main` (just for a real project context to point `claude` at).
- [ ] Start a tmux session manually: `tmux new -s main -c ~/work/remote-unity-agents/main`. Inside, run `claude`. Confirm TUI starts.
- [ ] Detach (Ctrl-b d). Re-attach (`tmux attach -t main`). Confirm session intact.

#### A4.5 — ttyd in front of tmux (¼ hour)
- [ ] `ttyd -p 7681 -i tailscale0 --ping-interval 30 --max-clients 2 tmux new-session -A -s main` — binds to the Tailscale interface only, attaches/creates the tmux session if it doesn't exist (`new-session -A` is idempotent), pings every 30s to survive idle-proxy timeouts, allows 2 concurrent clients (active + reconnecting).
- [ ] **Per research (§11)**: ttyd is stateless per-connection; persistence comes from tmux. Closing the tab does NOT kill claude when tmux is the entrypoint (ttyd #89). Default reconnect timeout ~10s.
- [ ] From laptop browser: `http://<vm-tailnet-name>:7681` → terminal renders with claude TUI inside.
- [ ] From phone browser: same URL → terminal renders on mobile. **Enable Claude fullscreen mode** (`/fullscreen` slash command, per https://code.claude.com/docs/en/fullscreen) — uses xterm.js alternate screen buffer, reduces flicker on mobile.
- [ ] Type a prompt from the phone. Confirm `claude` executes it and output appears.
- [ ] Close phone browser. Walk away. Come back in 30+ minutes. Reload. Session still alive.
- [ ] **B1 validation**: also test slash commands (`/help`, `/exit`), multi-line input, ANSI colors, scrolling on iOS Safari + Android Chrome. Known issues to watch for: xterm.js Pixel 7 Pro screen-black bug (xterm.js #4279), status-line wrapping on narrow viewports. Reference precedents: `STRRL/shell-now`, `buckle42/claude-code-remote` — both ship Tailscale + ttyd + tmux + claude for iPad/phone access; this stack is known-working.

#### A4.6 — Persistence + auto-start + cgroups slices (½ hour)
- [ ] systemd user units in `infra/systemd/`:
  - `tmux-main.service` — `Type=forking`, `ExecStart=/usr/bin/tmux new-session -d -s main 'claude'`, `ExecStop=/usr/bin/tmux kill-session -t main`, `RemainAfterExit=yes`, `Slice=interactive.slice`, `Restart=on-failure`. Note: `Restart=always` would loop-fail because `tmux new -d` exits immediately after backgrounding; `Type=forking + RemainAfterExit` is the correct pattern.
  - **Alternative if the forking pattern misbehaves**: wrap in a script that does `tmux new-session -A -s main 'claude; exec bash'` (no `-d`), runs in foreground, `Type=simple`, `Restart=always`. Pick whichever survives a `systemctl restart tmux-main` test cleanly.
  - `ttyd-chat.service` — `ExecStart=/usr/bin/ttyd -p 7681 -i tailscale0 --ping-interval 30 --max-clients 2 tmux new-session -A -s main`, `Slice=interactive.slice`, `Restart=always`, `After=tmux-main.service`, `Requires=tmux-main.service`.
- [ ] **Define `interactive.slice` and `batch.slice` for resource isolation** (per research §11):
  - `interactive.slice`: `CPUWeight=1000`, `MemoryHigh=2G` — chat layer, always-on, low CPU.
  - `batch.slice`: `CPUWeight=50`, `CPUQuota=80%`, `IOWeight=50`, `MemoryHigh=` (sized per box) — Unity batchmode + GH Actions runner go here. Weights only kick in under contention, so batch gets full CPU when chat is idle.
  - Both slices in `infra/systemd/slices/`.
- [ ] `loginctl enable-linger agent` so user units survive logout.
- [ ] Reboot the VM. Confirm both services come up automatically. Confirm browser at `http://<vm-tailnet-name>:7681` from phone still works post-reboot.
- [ ] Verify cgroups: `systemctl status interactive.slice` shows the chat units; `systemd-cgls` shows the hierarchy.
- [ ] Commit unit + slice files to `infra/systemd/` on this branch.

#### A4.7 — Gate
- [ ] **Pass 1 gate**: phone browser → live `claude` over Tailscale, survives VM reboot, survives idle disconnect. **No Unity involved.** If this gate fails, the problem is in chat-layer plumbing — fix here before adding Unity complexity. If it passes, proceed to A5.

### Phase A5 — Linux VM: Unity layer (Pass 2 starts) (½ day)

**Goal**: install Unity on the VM via the GameCI Docker image, activate license once, run a smoke-test batchmode import in the container.

**Substrate decision**: we pull `unityci/editor:ubuntu-6000.3.11f1-...` and treat the container as Unity-on-Linux. We do NOT use GameCI's GitHub Actions automation (see §5 for why). License activation stays manual on the host.

#### A5.1 — Resize VM if needed (10 min)
- [ ] Pass 2 needs ≥8 GB RAM (Unity minimum 8 GB official, 16 GB realistic). If Pass 1 ran on a 4 GB box, resize now or provision a fresh CCX23 (4 vCPU / 16 GB) and re-run A4 quickly on it.

#### A5.2 — Install Docker (10 min)
- [ ] `apt install docker.io docker-compose-v2`.
- [ ] Add `agent` user to `docker` group (`usermod -aG docker agent`), re-login.
- [ ] `docker run --rm hello-world` succeeds.

#### A5.3 — Pull GameCI Unity image, one tag per Editor version in use (15 min per tag, mostly download time)
- [ ] `apt install jq` (needed for digest resolution below).
- [ ] **Precondition**: A3.1 research gate passed (conditional pass per §11) and A3.2 empirically confirmed Unity batchmode works in `unityci/editor` against at least one of our real projects.
- [ ] For each distinct Editor version pinned across our projects (read `ProjectSettings/ProjectVersion.txt` of each project — currently `6000.3.11f1` everywhere, but Gear-Engine / Card Framework / Scaffold could diverge later), identify the matching image tag at `https://hub.docker.com/r/unityci/editor/tags` (form: `ubuntu-<version>-linux-il2cpp-<N>` or `-base-<N>` if il2cpp isn't needed).
- [ ] **Pin by digest, not just tag** — once you've identified a tag, resolve its SHA256: `docker manifest inspect <tag> | jq -r '.config.digest'`. Record both the tag and the digest. Use the digest form (`unityci/editor@sha256:...`) in scripts so the load-bearing 7–10 GB image is reproducible even if upstream re-tags.
- [ ] Commit the chosen `(tag, digest)` per Editor version to `infra/docker/unity-images.txt`.
- [ ] `docker pull <tag>@<digest>` for each. Note disk usage per image (7–10 GB compressed, more uncompressed).
- [ ] Smoke per image: `docker run --rm <image-ref> unity-editor -version` → prints the editor version, exits 0.

#### A5.4 — Activate Unity Personal license (½ hour, manual) + back up `.ulf` immediately
- [ ] On VM, inside container: `docker run --rm -v ~/unity-license:/license <image-ref> unity-editor -batchmode -quit -createManualActivationFile -logFile -`. Outputs a `.alf` file into `~/unity-license/`.
- [ ] On laptop: upload `.alf` to https://license.unity3d.com/manual → download the resulting `.ulf`.
- [ ] `scp` the `.ulf` to `~/unity-license/Unity_lic.ulf` on the VM (mode 0600, gitignored).
- [ ] Verify activation works: `docker run --rm -v ~/unity-license/Unity_lic.ulf:/root/.local/share/unity3d/Unity/Unity_lic.ulf:ro <image-ref> unity-editor -batchmode -quit -logFile -` → exit 0, no license errors.
- [ ] **Back up the `.ulf` immediately, before any further work.** This file is rate-limited to re-create (Unity's manual activation page enforces a cooldown) and impossible to fully replace without going through the activation dance again. Two copies, two destinations:
  - [ ] `scp Unity_lic.ulf` back to laptop into a gitignored backup directory (`C:\Unity\backups\unity-license\Unity_lic.ulf.<vm-hostname>.<date>`).
  - [ ] (Optional, recommended) encrypted push to a B2 bucket or Hetzner Storage Box (`restic backup ~/unity-license/`).
- [ ] Document the backup paths in `infra/runbook.md` (write a stub now; A7 fills it out fully). The runbook must include: where the .ulf is, how to restore it to a new VM, what to do if it's truly lost (Unity site, fresh activation, accept the cooldown).

#### A5.5 — Parallel Import disabled via CLI flag (no code change needed)
- [ ] `infra/unity-batchmode.sh` (factored in A5.6) already passes `-desiredWorkerCount 0` on every invocation, which disables Parallel Import per-run without mutating any project file. **No per-project `EditorSettings.asset` change required.**
- [ ] Research note (§11): the exact `EditorSettings.asset` key for Parallel Import in 6000.3 is undocumented in the public Unity corpus (Unity often serializes only non-default values, so the key may not exist in your file at all). `-desiredWorkerCount 0` is the canonical workaround for the headless-Linux Parallel Import bug.
- [ ] If you ever need to disable in the Editor UI for some other reason: Edit → Project Settings → Editor → Asset Pipeline → Parallel Import. Toggle, then `git diff ProjectSettings/EditorSettings.asset` to see what Unity wrote.

#### A5.6 — Factor `unity-batchmode.sh`, then smoke test in container against a real project (½ hour)

The `docker run -v ~/unity-license/...` invocation is 200+ characters and will be called from at least three places (A5.6 smoke, A6, B1 session-manager, B2 fire-and-forget workflow). Extract it into a reusable script *first*, then use the script for the smoke test. Keeps the rest of the plan from coupling to a one-liner.

- [ ] Write `infra/unity-batchmode.sh` — a small bash wrapper that takes `<project-path>` (required), optional `<editor-version>` (default: read from `<project-path>/ProjectSettings/ProjectVersion.txt`), optional extra args (passed through to `unity-editor`). Internally resolves the image ref from `infra/docker/unity-images.txt` (digest-form, not tag-only) for that Editor version, mounts the project at `/project`, mounts the `.ulf` read-only, runs `unity-editor -batchmode -quit -nographics -projectPath /project -desiredWorkerCount 0 "$@" -logFile -`. Captures exit code, propagates.
- [ ] **Always include `-desiredWorkerCount 0`** — disables Parallel Import per-invocation (Linux headless bug workaround); cleaner than mutating `EditorSettings.asset`. See A5.5.
- [ ] Make it executable, commit to the branch.
- [ ] Clone Card Framework (smallest of our real projects) into `~/work/CardFramework/main`.
- [ ] Run via the wrapper: `~/remote-unity-agents/infra/unity-batchmode.sh ~/work/CardFramework/main`. Watch for clean exit 0, Library/ populated in `~/work/CardFramework/main/Library/`.
- [ ] **Gate**: if this fails (license, GLIBC, file permissions, anything), surface and stop — the Unity-on-Linux substrate is the variable we couldn't validate on Windows. Don't paper over it.

### Phase A6 — VM worktrees + first phone-driven batchmode (¼ day)

**Goal**: prove the full stack — phone-driven `claude` session → spawn Unity batchmode in container → see output streaming back to phone — works end-to-end on Linux.

- [ ] `git worktree add ~/work/CardFramework/slot1 <branch>` and `slot2`. Confirm independent Library/ per worktree (per A1's findings).
- [ ] Provisioning script `infra/worktree-add.sh`: creates worktree, ensures `Assets/` exists (per A1 gotcha #1 — empty Unity dirs don't survive git), seeds an empty Library/ directory, fixes mode bits if needed. Test it on a fresh worktree before relying on it.
- [ ] From phone-driven `claude` session (the persistent tmux-main session from A4.6, opened at `~/work/remote-unity-agents/main` — claude can shell out to any path): ask it to run `bash infra/unity-batchmode.sh ~/work/CardFramework/slot1` (factored in A5.6). Watch output stream back to the phone.
- [ ] **Gate**: phone → claude → unity-batchmode.sh → docker → Unity → exit 0. **This is the end-state Track A working.** Desktop can be off, work from anywhere, agent can drive Unity.

### Phase A7 — Harden as permanent service (½ day)

- [ ] `apt install restic` (used for nightly backups below).
- [ ] All services run as systemd units, `Restart=always`, log to journald, slice-scoped (per A4.6).
- [ ] (Optional) Caddy in front of ttyd for `https://chat.<domain>` with TLS via Let's Encrypt; otherwise the raw HTTP on tailnet is fine (Tailscale already encrypts). If we ever need a non-Basic-auth on ttyd (per research §11: ttyd has no native token/OAuth), put Caddy in front with `forward-auth` to Authelia/Tinyauth/oauth2-proxy.
- [ ] **Backup destination** (read `infra/vm-host.txt` from A4.1 to branch; per research §11):
  - If VM is on Hetzner: **Hetzner Storage Box BX11** (~€3.81/mo for 1 TB, free intra-Falkenstein traffic, SFTP backend works with `restic`). Best when staying on Hetzner.
  - If VM is on GCP: **Backblaze B2** ($0.18/mo for 30 GB, native restic backend, fast restore, free egress via Cloudflare Bandwidth Alliance). Best when geo-separated from primary host.
  - If VM is on Oracle: skip backups for the validation phase; revisit when promoting to a paid box.
- [ ] Nightly `restic backup ~/work/*/main/Library/ ~/unity-license/Unity_lic.ulf /etc/systemd/system /home/agent/.config/claude` (or equivalent paths). Test restore monthly against a scratch directory.
- [ ] Document the GH Actions runner deployment shape: **ephemeral / just-in-time (JIT) runner only**, one job per runner instance, then destroyed. Never autoscale persistent runners. **Never** scope to public repos. Runner unit goes in `batch.slice`. See B2 for details.
- [ ] **Write the full `infra/runbook.md`**: how to provision a replacement VM from scratch in <2 hours if this one dies. Must include: (1) provider-side VM creation, (2) Tailscale join, (3) base install (A4.2-A4.3 condensed), (4) restore .ulf from backup, (5) re-pull GameCI images (A5.3), (6) restore Library/ caches from backup, (7) reattach to existing tailnet so phone bookmarks still work. Test it once on a throwaway VM before declaring done.
- [ ] **Done with Track A.** Desktop can be off. Work from anywhere. Move to Track B planning.

---

## 8. Track B — Agent (after A is solid)

Outcome: programmatic dispatch into the same `claude` sessions Track A exposes, plus a fire-and-forget path for fresh-worktree tasks.

### Phase B0 — Decide execution split (1 hour, no code)
- [ ] Read §2.2. Decide v1 scope:
  - [ ] **B-session only** (tmux + send-keys wrapper, REST on top; webhook trigger deferred per §10). Long-term cost-optimal. ~1–2 days build.
  - [ ] **B-fire-and-forget only** (GH Actions self-hosted runner + workflow.yml). Faster to ship (~½ day) but API-billed post-June 15.
  - [ ] **Both** (start fire-and-forget for speed, add session-driven for cost + small-fix UX). Recommended.
- [ ] Decide concurrency: 2 slots (CCX33 comfy) or 4 (needs CCX43 or memory check).

### Phase B1 — VM session-manager (B-session path) (1–2 days)

**Note on ordering**: B1 is on the VM, not the laptop. The A2 reshape decided Windows is for editing + Tailscale only; the VM is the validation surface. The session-manager service runs alongside the chat-layer services from A4.

- [ ] `apt install python3 python3-pip python3-venv` (FastAPI is Python; we deliberately didn't install Python in A4.3 since no phase needed it before now).
- [ ] Tiny FastAPI service in `infra/session-manager/`, single file, in-memory session registry:
  - `POST /sessions {name, worktree}` → `tmux new-session -d -s <name> -c <worktree> claude`
  - `GET /sessions` → list with `tmux list-sessions -F`
  - `POST /sessions/{name}/input {text}` → `tmux send-keys -t <name> "<text>" Enter`
  - `GET /sessions/{name}/output?lines=N` → `tmux capture-pane -t <name> -p -S -<N>` (with ANSI strip)
  - `WS /sessions/{name}/stream` → tail capture-pane on interval
  - `DELETE /sessions/{name}` → `tmux kill-session -t <name>`
- [ ] Read patterns from `Herdr` and `claude-bridge` (reference-only, do not install).
- [ ] **Supervisor logic**: monitor `claude` inside each tmux session. On non-zero exit OR pane-silent >30s, respawn `claude` in the same tmux (preserves working dir; tmux scrollback shows history but `claude`'s conversation context resets — document this behavior visibly when respawn happens). Exponential backoff: 1s → 2s → 4s → 8s → ... → 60s max. Circuit breaker: 5 fails within 5 min → stop respawning, mark session "degraded," surface via REST + WS stream.
- [ ] Deploy as systemd user unit `session-manager.service` (bind to `tailscale0` only, port 7682 or similar; do NOT expose on the public interface).
- [ ] Test loop: from laptop over Tailscale, create a session, send a prompt, fetch output, send a follow-up — confirms the "small fix" UX works.
- [ ] **Gate**: if send-keys + capture-pane is too fragile to drive `claude` reliably, fall back to `pexpect`/`node-pty` PTY mode, or accept B-fire-and-forget only.

### Phase B2 — VM fire-and-forget (B-fire-and-forget path) (½ day)

**Note on ordering**: on the VM. Self-hosted GH Actions runner needs to spawn Unity batchmode in container, which is Linux-only. Cannot run on Windows laptop.

**Security shape (per research §11)**: ephemeral / just-in-time (JIT) runner only — one job per runner, destroyed after. Never autoscale persistent runners. Never scope to public repos. Avoid mounting `/var/run/docker.sock` blindly (= root on host); use rootless Docker or invoke `docker` via a constrained user. Use OIDC for cloud creds if any are needed (not long-lived secrets in env).

- [ ] Install `gh` on the VM: `sudo apt install gh` (or per GitHub CLI's official install). Auth with `gh auth login` using the same fine-grained PAT that the runner is registered with.
- [ ] Write `.github/workflows/agent-task.yml` with `workflow_dispatch` inputs `(branch, prompt, project)`.
- [ ] Workflow steps (uses the **persistent `~/work/<project>/main` checkout** from A6 as the parent for new worktrees — does NOT `actions/checkout` into the runner's ephemeral workspace; that would lose the warm Library/ cache):
  1. `cd ~/work/<project>/main && git fetch --all`.
  2. `infra/worktree-add.sh ~/work/<project>/slot-${{ github.run_id }} <branch>` — creates a clean worktree at a unique path, handles the empty-Assets/ gotcha.
  3. `infra/unity-batchmode.sh ~/work/<project>/slot-${{ github.run_id }}` — if the task needs Unity. Library/ written back to the slot path.
  4. `cd ~/work/<project>/slot-${{ github.run_id }} && claude -p "$PROMPT"` — claude operates inside the slot.
  5. `git commit + git push` from the slot.
  6. `gh pr create` — opens PR.
  7. (Optional) `git worktree remove ~/work/<project>/slot-${{ github.run_id }}` — cleanup, or keep around for follow-up turns. Tradeoff: keeping costs disk (5-20 GB), removing costs cold rebuild on next task.
- [ ] Install GH Actions JIT runner on the VM (`actions/runner` tarball under `~/actions-runner/`), labeled `unity-agent`, registered with `--ephemeral` flag so each run gets a fresh runner. Runner unit (`actions-runner.service`) goes in `batch.slice` (per A4.6 cgroups setup).
- [ ] Runner runs as dedicated unprivileged user `agent-runner` (NOT the same user as the chat-layer `agent`). Workspace constrained to `~/work/` via runner config.
- [ ] **Resource discipline already in place** via systemd slices (A4.6): runner inherits `CPUWeight=50`, `CPUQuota=80%`, so it can't starve chat. Additional: `nice -n 10` on Unity invocations as belt-and-suspenders.
- [ ] **Never repo-public**: this runner must be registered against the private monorepo only. Public-repo runners + arbitrary PR code = remote code execution.
- [ ] Trigger via `gh workflow run agent-task.yml -f branch=test -f prompt=... -f project=CardFramework`. Watch live logs in GitHub UI.
- [ ] Confirm PR appears and is correctly attributed.

### Phase B3 — Webhook trigger (½ day, deferred to post-v1)

**Deferred.** Track B v1 uses `gh workflow run agent-task.yml -f branch=... -f prompt=... -f project=...` from any device with `gh` installed (laptop, phone with Termius+gh, anywhere). Same outcome as `@bot` PR comments without the webhook surface.

Revisit this phase only if PR commenting becomes the dominant trigger pattern and the CLI feels awkward. If revisited:
- [ ] GH workflow: on `issue_comment.created` matching `@bot <prompt>` → trigger `agent-task.yml` (fire-and-forget) or POST to session-manager (session-driven), depending on prefix.
- [ ] Verify HMAC signature handling if we expose any direct webhook endpoint (vs. routing through GH).
- [ ] Test by commenting on a real PR.

### Phase B4 — Minimal control UI (½ day, optional)
- [ ] One-page htmx UI listing tmux sessions, with input box + log pane per session.
- [ ] Same surface area as the REST API; just a thin frontend.
- [ ] Skip if `tmux ls` + `tmux attach` from a terminal is good enough.

### Phase B5 — UX upgrade evaluation (½ day, optional, only if needed)

Trigger: A7 (or later use) surfaces real mobile-UX pain that ttyd can't fix with tuning (font size, fullscreen mode, etc.). Default is "we never run this phase" — ttyd alone is the canonical config.

If triggered:
- [ ] Survey current state: claudecodeui (siteboon/claudecodeui), warp-style web terminals, anything newer in the Claude Code ecosystem. Don't assume claudecodeui is still the right choice — re-evaluate vs alternatives at the time of need.
- [ ] If picking claudecodeui: full security review (it's "Under evaluation" in §5 — handles tokens + shells, ~11k stars, smaller maintainer base than enterprise tools). Sandbox first: separate VM user, no PAT in environment, route through Caddy with strict CSP. Run in parallel with ttyd until confidence builds.

---

## 9. Risks & landmines

- **April 2026 third-party-harness restriction** — confirmed: subscription billing only valid for Anthropic's own surfaces. Mitigation: never call Anthropic from our code; always shell out to the official `claude` binary. Re-validate quarterly.
- **June 15, 2026 `-p` billing split** — Track B-fire-and-forget becomes API-priced. Mitigation: session-driven path (B-session) stays on subscription; switch defaults to it if API cost shows up.
- **Unity Personal in headless Linux containers is unsupported (2026)** — confirmed by A3.2 discovery and follow-up research; see §11. Not just a fuzzy EULA question — modern Unity (2021.2+) uses LicensingClient daemon + hardware-fingerprint-bound XML format, and Unity removed manual `.alf`/`.ulf` activation for Personal in Aug 2023. Three paths: GameCI Puppeteer flow (brittle), Editor-on-Windows + SSH (architecture pivot), or Unity Pro ($2,200/yr). **Plan currently paused on this gate.**
- **Anthropic could harden against PTY-driving the TUI** — Path B-session relies on the interactive CLI being scriptable via tmux. If they detect/block this, fallback is API-billed dispatch. Watch policy updates.
- **Unity headless Parallel Import bug** — known. Disabled per-invocation via `-desiredWorkerCount 0` CLI flag (baked into `infra/unity-batchmode.sh`). Re-test on each Editor version bump.
- **`siteboon/claudecodeui` if adopted later (Phase B5)** — high-alert dependency: handles tokens + shells, ~11k stars but smaller maintainer base than enterprise tools. Not adopted in v1; only revisited if mobile UX in A4.5 / A7 surfaces real pain that ttyd can't fix. Adoption requires security review + sandboxed trial per §5.
- **Single VM = single point of failure** — Hetzner outage stops everything. Acceptable for solo dev; revisit when load-bearing.
- **tmux send-keys quoting** — prompts with special characters need careful escaping (or write to a file and have `claude` read it).

---

## 10. Open questions

- [ ] ⛔ **Unity licensing path for headless Linux (BLOCKING).** Personal can't run in Linux containers in 2026 (see §11 "Unity Personal in containers — blocking discovery"). Three options on the table: (a) GameCI Puppeteer flow (free, brittle, ~$0 ongoing + periodic fix-it work), (b) pivot architecture to Editor-on-Windows + SSH-from-Linux-VM (changes "desktop-off" goal), (c) buy Unity Pro ($2,200/yr ≈ $183/mo, cleanest). **Plan is paused at A3.2 until owner picks.** Until then, A3.5 / A3.6 (non-Unity ttyd+tmux+claude) can run independently if we want to keep validating the chat layer.
- [x] ~~GameCI-image-without-license-automation research~~ **Partially resolved.** Image mechanics confirmed (no ENTRYPOINT, mount path correct), but the deeper Personal-in-container issue surfaced only in A3.2. Image works; licensing is the blocker. See §11.
- [x] ~~Chat UI: ttyd vs claudecodeui?~~ **Resolved: ttyd primary; defer UX evaluation until core stack works.** Phase B5 stays as the placeholder slot for "evaluate UX upgrades (claudecodeui or others) if A7 reveals real mobile-UX pain." No pre-commitment to claudecodeui — it stays in §5 "Under evaluation" pending need + security review.
- [x] ~~Webhook trigger now or later?~~ **Resolved: defer.** Track B v1 uses `gh workflow run` from any device with gh installed. B3 becomes a "post-v1, only if commenting becomes the dominant trigger" phase.
- [x] ~~CCX23 / CCX33 / CCX43 sizing?~~ **Resolved: trigger-based.** Start CCX23 after free-tier validation passes. Resize to CCX33 if (A3.3 proves concurrent batchmode works) AND (B0 picks 2+ concurrent slots). Resize to CCX43 only if 4+ concurrent Unity jobs become the dominant workload. Default end-state remains CCX33 per §3.
- [x] ~~Library/ on root disk vs. separate Hetzner Volume?~~ **Resolved (research §11): root NVMe.** Hetzner attached Volumes are 10-17× slower for Unity's IOPS-bound asset import workload.
- [x] ~~Session-manager auto-restart `claude` on crash?~~ **Resolved: yes, with backoff.** Supervisor pattern in B1: monitor `claude` inside each tmux; on non-zero exit or pane-silent >N seconds, respawn. Exponential backoff (1s → 60s cap), circuit breaker (5 fails / 5 min → stop + surface). Caveat documented: conversation context resets on respawn (working dir preserved via tmux, chat history doesn't carry across new `claude` invocations).
- [x] ~~Watchdog Claude that monitors PR CI?~~ **Resolved: defer to post-v1.** Infrastructure (session-manager, GH runner) supports it; revisit once Track B is solid and we see real CI-failure patterns worth automating against.
- [x] ~~Unity Accelerator: needed?~~ **Resolved (research §11): no.** Cross-machine LAN cache; single-host multi-worktree gets the same benefit from Asset Database V2's built-in cache.
- [x] ~~ttyd auth strategy?~~ **Resolved: tailnet-only for v1.** Rely on tailnet membership as the only auth. Trust model documented in runbook: "tailnet members have full claude access." Caddy + forward-auth is the canonical upgrade path (per research §11) but premature for solo. Revisit only if a tailnet member needs partial access, or a non-tailnet-member needs access at all.
- [x] ~~Empirical OAuth-on-cloud-VM billing check?~~ **Resolved: tracked in A4.3 as a checkbox.** Research §11 says billing should stay on Max quota; A4.3 closes the loop by dashboard verification after first call. No standalone open question.

---

## 11. Findings & journey notes

Captures the research that shaped this plan, so future revisits don't re-derive from scratch.

### Unity + git worktrees (verified, green light)
- Unity locks per project path, not per repo. N worktrees with N independent Library/ folders work concurrently. Same pattern GameCI uses.
- Mitigations baked into the plan: `-desiredWorkerCount 0` CLI flag to disable Parallel Import on Linux headless (known "Won't Fix" bug); activate license once per machine; never symlink PackageCache across worktrees. (Unity Accelerator was considered but dropped — Asset Database V2's built-in cache covers single-host needs.)
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

### Windows-local browser-terminal validation: abandoned after two attempts (2026-05-21)

We tried twice to host the persistent browser-terminal stack on Windows so the laptop could act as an interim always-on server. Both attempts failed; we reshaped A2 to "Tailscale validation only on local; browser-terminal moves to Linux VM" rather than keep grinding on Windows-specific tooling.

**Attempt 1: `code-server` via `npm install -g code-server`.**
- Node 24: `argon2@0.28.4` fails to build (no prebuilts, requires VS Build Tools).
- Node 20 (installed via fnm to get prebuilts): `argon2@0.31.2` installs cleanly, but `postinstall.sh` (which runs `yarn` inside `vendor/` to build VS Code's own native modules: node-pty, kerberos, spdlog) fails and triggers an `EBUSY` rename on rollback (Windows Defender holding the freshly-extracted directory).
- coder/code-server's own docs say: *"We currently do not publish Windows releases. We recommend installing code-server onto Windows with `npm`."* Windows is a second-class platform for the project.

**Attempt 2: Microsoft `code serve-web`** (ships with VS Code 1.120+, accessed via `code serve-web` / `code-tunnel.exe`). Native Windows binary, zero compile, local web server only (no MS relay). Smoke-tested at `127.0.0.1:8080`:
- UI loaded; workspace name showed in title bar.
- Several built-in extensions failed to load (`File not found: ...github-authentication\dist\browser\extension.js`, same for `emmet`, `git-base`, `merge-conflict`) — `serve-web` ships missing browser-mode entry points for these. Microsoft labels `serve-web` itself "experimental."
- No top menu bar visible. Owner couldn't navigate to "Terminal: Create New Terminal" via Command Palette (F1 / Ctrl+Shift+P) in practice — keyboard focus / browser keyboard interception issues piled up.
- Conclusion: `code serve-web` may work for someone willing to spend hours configuring it, but for our purposes (mobile-driven browser terminal, "just works") it's not viable.

**Reshape decision (Q1 in plan-doc reconciliation 2026-05-21):** stop trying to make Windows host the chat layer. We're not running a Windows server in the end-state architecture anyway — only ever planned to use the laptop as a temporary host for validation. Move the browser-terminal validation entirely to the Linux VM in Phase A4. On Linux, `ttyd + tmux` is well-supported, Linux-native, and matches the production target architecture.

Three things we keep from the abandoned Windows work:
- W1-W3 are real wins — Tailscale validated on local devices is the same primitive phone↔VM will use.
- The Microsoft `code-tunnel.exe` discovery is worth knowing exists for ad-hoc cases (we just don't use it as the load-bearing architecture).
- The two failed attempts validated that Windows-native server tooling has too rough an edge to bet a plan on. Future plans should pin Linux as the server OS from the start.

### GameCI Docker image as Unity-on-Linux substrate (2026-05-21)

Decided in Q2 of the plan-doc reconciliation. GameCI is three separable pieces — we take only piece (1):
1. **`unityci/editor` Docker image** — Unity Editor pre-installed on Ubuntu, GPU-stub libs included, version-pinned by image tag. ✅ Adopted.
2. **`unity-license-activate` / `unity-license-return` GitHub Actions** — license helpers for ephemeral CI jobs. ❌ Rejected: wrong shape for our persistent host. License activation is a one-time manual step on the host (`.alf` → upload to Unity → `.ulf` → mount into container).
3. **`unity-builder` action / full CI workflow** — opinionated GitHub-Actions-shaped build pipeline. ❌ Rejected: ephemeral per-job model doesn't fit long-lived tmux sessions.

Why the image-only adoption is a big win:
- Phase A5 collapses from ~½ day of manual install debugging (Unity Hub headless, GLIBC mismatches, X11 stub libs, package conflicts) to one `docker pull`.
- Version-pinned reproducibility — `ubuntu-6000.3.11f1-...` tag means same Unity install everywhere.
- Forward-portable to any Docker host (Hetzner, GCP, AWS, OCI, your laptop).
- Easier upgrades — change the image tag, retest.
- Unity install crufts (`~/.config`, leftover Hub state) stay inside the container; host stays clean.

What changed in the plan: Phase A5 was rewritten end-to-end. License activation flow stays the same (manual, one-time). Library/ stays on host volume mounts (not inside the container) so warm caches survive container restarts.

**Status (2026-05-22)**: research-resolved. See "GameCI image standalone confirmed" entry below — `unityci/editor` has no ENTRYPOINT, license injection is opt-in via `UNITY_LICENSE` env var (handled by a hook in `/usr/bin/unity-editor.d/`), pre-mounting `.ulf` at `/root/.local/share/unity3d/Unity/Unity_lic.ulf` works without any GameCI bootstrap. A3.2 confirms empirically against our real projects.

### Hetzner + Unity: no specific precedent, but workload generalizes (2026-05-21)

Researched whether anyone has published "Unity batchmode on Hetzner Cloud." **Zero direct write-ups exist.** Hetzner shows up as a generic CI host (one HN thread mentioning AX41 bare-metal for GitHub Actions runners), but no Unity-specific case study.

**Mitigating data**: Hetzner CCX is KVM on AMD EPYC with NVMe — same hypervisor shape as AWS m5/c5 and GCP n2, where Unity batchmode is heavily documented via GameCI users. Risk profile = generic-KVM-Unity-Linux, not Hetzner-specific. Our local A1 validation already proved the worktree mechanic + concurrent batchmode on real Unity — only the hypervisor variable is new.

**Sizing reference** (from community + AWS write-ups):
- Unity official minimum: 8 GB RAM.
- Community-realistic: 32 GB RAM for non-trivial projects, 64 GB+ if baking lighting.
- AWS instances people actually use: m5.2xlarge (8 vCPU / 32 GB) baseline, c5.4xlarge (16 / 32) for heavy compute.
- Hetzner equivalent: **CCX23 (4 / 16) is the floor, CCX33 (8 / 32) matches AWS m5.2xlarge directly.**

**Known Linux-Unity gotchas** (most baked into the plan):
- **Parallel Import bug on headless Linux** — Unity issue tracker, marked "Won't Fix." Disabled per-invocation via `-desiredWorkerCount 0` CLI flag (no project-file mutation needed; the `EditorSettings.asset` key turned out to be undocumented for 6000.3 anyway). In Phase A5.5 + baked into `unity-batchmode.sh`.
- **Semaphore/assertion crash in batchmode** on certain Editor versions (2021.3.35, 2022.3.20, 2023.2.12, 2023.3.0b9) on WSL Ubuntu 22.04 and AWS EC2. Our 6000.3.11f1 isn't on the list but isn't proven safe either.
- **Ubuntu 22.04 LTS preferred over 24.04** — Unity reproduces more bugs on 24.04; 22.04 is the safer pin. Plan-doc updated everywhere.

### Stripe Minions / sandbox-provisioning landscape (2026-05-21, forward-looking)

Researched how others scale "fleet of agent dev environments" because we'll want to know the answer before our worktree-per-slot model hits its limit.

**Stripe Minions is real** — coding-agent fleet shipping >1,300 PRs/week, built on a fork of Block's open-source `goose` agent. Sits on top of their **Devbox** infrastructure: pre-warmed EC2 instances that human engineers also use. "Cattle, not pets" model: each devbox is disposable, single-task, ~6 in parallel per engineer. Warm pool gets to "usable" in ~10 seconds with monorepo pre-cloned, caches warmed.

**Critical pull-quote** for our plan: Stripe explicitly says worktrees "wouldn't scale" — they reach for warm-pool-of-full-machines instead. **Our worktree-on-one-VM plan is the pattern Stripe abandoned at scale.** Fine for solo dev (us); break point is somewhere around 4–6 concurrent active worktrees.

**Service landscape**:
| Service | Boot | Isolation | Relevance |
|---|---|---|---|
| Modal Sandbox | sub-second | gVisor | Wrong tool — no editor support, can't run Unity Editor |
| E2B | <200ms | Firecracker microVM | Same as Modal, AI-agent focused. No Unity. |
| Daytona (OSS) | ~90ms sandboxes | Docker / Kubernetes | **Closest fit if we outgrow one VM.** Self-host, handles lifecycle/DNS/ports, doesn't lock us out of Unity-in-container. |
| GitHub Codespaces | seconds–minutes | Docker on Azure VM | Human-dev-env shape. Would work but pricier than self-host. |
| Vercel Sandbox | sub-second | Firecracker | Agent runs outside; not editor workloads. |

**Common patterns the industry has converged on**:
- Firecracker microVMs or gVisor for isolation (real kernel per tenant, ~125ms boot).
- Warm pools beat cold starts (idle capacity drives cost, not compute).
- Snapshots as the persistence unit (base image + warmed-state snapshot, fork on demand).
- Agent runs *outside* the sandbox; talks to it via shell/file/git tool calls.
- Egress-restricted networking (Stripe blocks internet from Minion boxes entirely).

**Unity-specific reality** (doesn't generalize from the industry):
- **Unity license is the binding constraint, not compute.** Personal `.ulf` is user-account-scoped → per-task activate/return rate-limits and flakes. **Unity Build Server (floating licenses)** is the scale answer for game studios — but it's a separate paid product on top of Pro.
- **15 GB editor + 5–20 GB warmed Library/ per project** breaks naive ephemerality. Every serious Unity CI ends up with base image carrying a warmed Library snapshot keyed by project hash.
- **GameCI is the only public reference for ephemeral Unity runners.** No game-dev company has published a Modal/E2B-shaped Unity sandbox setup — the wrinkle really is novel.

**Realistic scale path for our case** (forward-looking, not for v1):
1. Now → ~4 worktrees: single VM (what we're building). Fine.
2. ~4–8 concurrent: add snapshot discipline — base image with Unity + warmed Library/ per project. "New worktree" becomes "VM from snapshot" when contention starts.
3. >8 concurrent or multi-user: **Daytona self-hosted** on Hetzner or a small fleet. OSS, dev-container-shaped, Unity-friendly.
4. Modal/E2B/Vercel only if a non-Unity agent workload becomes dominant cost (e.g., codegen agents that just touch schemas).
5. **Unity Build Server license** is the unlock for any true horizontal scale.

Stripe's pattern is the right north star, but Unity licensing is the binding constraint everyone else doesn't have. No off-the-shelf service solves that today.

### Off-the-shelf composition (collapses the build)
- **GitHub Actions self-hosted runner** is our fire-and-forget dispatcher (Track B-fire-and-forget half) — no custom service needed for that half. `workflow_dispatch` REST = dispatch API. Runner = executor. GH UI = log streaming (mobile-friendly, free). Enterprise trust. (The session-driven half, B1, still needs a small FastAPI service for tmux send-keys / capture-pane orchestration — that's where the custom code lives, ~150 lines.)
- **Windmill** is the backup if we ever want a non-GitHub control plane (AGPLv3, well-funded). n8n is a similar tier.
- **ttyd + tmux** covers the chat UI surface with system-tool-grade trust. `siteboon/claudecodeui` is the upgrade path if mobile UX needs more — gated on security review.

### The tmux-harness pattern (new framing)
- Inverts the SDK model: instead of frameworks spawning typed "agents," persistent interactive `claude` sessions are the agents; the harness drives them via send-input / fetch-output.
- Tools in the space (reference-only): Herdr, claude-bridge, various tmux+REST wrappers.
- Implications: one auth model for chat + automation, no cold start on follow-ups, naturally cost-optimal post–June 15, simpler mental model.

### Anthropic OAuth on cloud VM — likely safe (2026-05-22 research)

**Question**: does running the official `claude` CLI on a rented Hetzner / GCP VM authenticated via `CLAUDE_CODE_OAUTH_TOKEN` count as ToS-compliant subscription use? The April 2026 third-party-harness policy could be misread to disallow this.

**Finding**: not a gate failure. The April 2026 ban (and Feb 2026 Agent SDK + OAuth ban) targeted **third-party harnesses** — OpenClaw, OpenCode, NanoClaw, Agent SDK usage — that route subscription OAuth through *their own* binaries. The official `claude` binary on subscription is explicitly excluded. The test is *which binary*, not *where it runs*. Kersai's roundup (https://kersai.com/anthropic-killed-third-party-claude-access-heres-every-workaround-that-still-works/) confirms running on a remote server via SSH is "fully within ToS." Anthropic's own docs don't define "Anthropic's own surfaces" anywhere — that phrasing is journalist shorthand.

**Risk**: Anthropic could expand the policy. Mitigation: stay on TUI mode (not `-p`), never wrap in Agent SDK, re-validate quarterly per §9.

**June 15, 2026 narrower than feared**: only Agent SDK + `claude -p` split to a separate API-billed credit pool. Interactive `claude` TUI on subscription unchanged. Source: https://support.claude.com/en/articles/15036540.

**A4.3 still empirically validates** by checking Anthropic dashboard usage after a `claude` call on the VM.

### `claude` CLI install on Linux + token lifecycle (2026-05-22 research)

- **Install** (official, native): `curl -fsSL https://claude.ai/install.sh | bash`. Installs to `~/.local/bin/claude`, background auto-update. (Alternative apt repo at https://downloads.claude.ai/claude-code/apt/stable for tracked installs.)
- **`nodejs` NOT required** for native install. Only the npm install path (`npm install -g @anthropic-ai/claude-code`) needs Node 18+. Plan dropped `nodejs` from the apt install line.
- **System requirements**: Ubuntu 20.04+, Debian 10+, Alpine 3.19+. 4 GB+ RAM. Alpine needs extra packages (`libgcc libstdc++ ripgrep`) and `USE_BUILTIN_RIPGREP=0`.
- **`CLAUDE_CODE_OAUTH_TOKEN` lifecycle**: 1-year, generated by `claude setup-token`. Each call creates a new token; revoking one doesn't kill others.
- **Rotation**: zero-downtime — generate new token from any logged-in machine, swap env var on VM, old token keeps working until expiry or manual revoke. No coordinated rotation needed.
- **At expiry**: token rejected on next auth; CLI exits at login prompt. No automatic re-auth from VM (no browser).
- **Revoke from claude.ai/settings/claude-code dashboard.** No bulk-revoke; tokens accumulate (GitHub #59378).
- **Gotcha (GH #34198)**: `claude logout` does NOT revoke server-side. Revoked tokens may stay valid up to 4 days due to caching.

### `claude` TUI in ttyd / xterm.js / mobile browsers (2026-05-22 research)

- **Known-working stack**: `STRRL/shell-now` and `buckle42/claude-code-remote` both ship Tailscale + ttyd + tmux + claude for iPad/phone access. Not experimental; existing precedent for our exact stack.
- **Known mobile rendering issues** to brace for:
  - xterm.js #4279: Chrome on Pixel 7 Pro — screen goes black, 1-2 chars visible, duplicate-char input, green cursor. Active bug.
  - xterm.js #945: general mobile rendering tracking issue.
  - Claude Code TUI flicker — `anthropics/claude-code` #9266 (flicker with history), #9935 (high scroll rates cause UI jitter in tmux). Anthropic shipped a **differential renderer** and a **fullscreen mode** (https://code.claude.com/docs/en/fullscreen) using xterm.js alternate screen buffer to mitigate.
- **Mitigations**: enable Claude's fullscreen mode (`/fullscreen` slash command), run inside tmux (reconnects don't lose state), test in iOS Safari + Android Chrome early. iOS Safari soft-keyboard occluding status line is common; long status lines wrap badly on narrow viewports.
- **A4.5 includes**: `--ping-interval 30` (keepalive past idle proxies), `--max-clients 2` (active + reconnecting), `tmux new-session -A -s main` (idempotent attach), Claude fullscreen-mode enabled, B1 render check on iOS Safari + Android Chrome.

### Infrastructure findings — Tailscale, ttyd, disk, backups, runner security, resource isolation (2026-05-22 research)

- **Tailscale Personal (free)**: 6 users / unlimited devices / 50 tagged resources / ACLs included / Serve + Funnel available. We're nowhere near limits (3 devices + maybe 1 VM = 4 tagged resources). Cliffs: >50 servers tagged, SSO/SCIM/audit-log requirements, custom RBAC roles.
- **ttyd auth**: native only `--credential user:pass` (HTTP Basic) and `--auth-header` for reverse-proxy delegation. No token/OAuth/cookie support natively (ttyd #274 still open). Pattern when stronger auth needed: put ttyd on a unix socket, front with Caddy + forward-auth (Authelia/Tinyauth/oauth2-proxy) for TOTP/OIDC.
- **ttyd reconnection**: persistence comes from tmux (`new-session -A -s main` is the entrypoint). Closing tab doesn't kill child (ttyd #89). Useful flags: `--ping-interval 30` (WS keepalive past idle proxies), `--max-clients N` (limit concurrent), `-t disableReconnect=true` to opt out. Default reconnect ~10s.
- **GH self-hosted runner security**: **ephemeral / JIT runners** (one job per runner, then destroyed). Never autoscale persistent runners. **Never on public repos** (PR code = RCE on runner host). Mounting `/var/run/docker.sock` = root on host — prefer rootless Docker, Kaniko, or BuildKit. Dedicated unprivileged user, workspace constrained to a directory tree. OIDC for cloud creds (not long-lived secrets in env).
- **Resource isolation**: cgroups v2 via systemd slices. `interactive.slice` (chat layer) `CPUWeight=1000`, `MemoryHigh=2G`. `batch.slice` (Unity batchmode + GH runner) `CPUWeight=50`, `CPUQuota=80%`, `IOWeight=50`. Weights only kick in under contention, so batch gets full CPU when chat is idle. References: ScyllaDB, OneUptime systemd cgroups guides.
- **Disk**: Hetzner instance-local NVMe ~32k/27k random R/W IOPS; attached Volumes reported **10-17× slower**. For Unity's IOPS-bound asset import workload, **stay on local NVMe**. CCX23 (80 GB) supports 3-4 worktrees; CCX33 (240 GB) supports more. GCP equivalent: PD-Balanced 200-500 GB for /home (Local SSD lost on stop, unsuitable for persistent worktree).
- **Backups**:
  - Hetzner Storage Box BX11 ~€3.81/mo for 1 TB, free intra-Falkenstein traffic, SFTP/rclone/restic. Best when staying on Hetzner.
  - Backblaze B2 $0.18/mo for 30 GB (storage), egress $10/TB after 3×-storage free, free via Cloudflare Bandwidth Alliance. Restic-native, fast restore. Best when geo-separated.
  - Wasabi/Storj competitive but no clear win at 30 GB.

### GameCI image standalone confirmed: pre-mount `.ulf` works without GitHub Actions wrapper (2026-05-22 research)

**Answer to A3.1 / A4.0 research gate**: ✅ **conditional pass.** Standalone usage works, but the pattern is "folkloric, not blessed" — game-ci/documentation#386 is an open user request for exactly this missing doc.

**Mechanics confirmed** (from reading `images/ubuntu/editor/Dockerfile` and game-ci/docker issue #107):
- **No ENTRYPOINT / CMD** in the image. A wrapper at `/usr/bin/unity-editor` does roughly: `xvfb-run -ae /dev/stdout "$UNITY_PATH/Editor/Unity" -batchmode "$@"`. Sources hooks at `/usr/bin/unity-editor.d/*` before exec.
- **License injection is opt-in** via `UNITY_LICENSE` env var, handled by one of those `.d/` hooks. If `UNITY_LICENSE` is unset, the hook is inert — confirmed that pre-mounting a `.ulf` works as-is.
- **Expected `.ulf` path**: `/root/.local/share/unity3d/Unity/Unity_lic.ulf` (for root containers); `/home/<user>/.local/share/unity3d/Unity/Unity_lic.ulf` (non-root).
- **Filename must be exactly `Unity_lic.ulf`** — no extra dots. Unity Issue Tracker bug.
- **Closest community reference**: John Austin's "Running Unity 2020.1 in Docker" (johnaustin.io, 2020) describes the `.ulf` mount pattern. No complete `docker run` command published.

**Parallel Import disable**: the `EditorSettings.asset` YAML key for Unity 6000.3 is **NOT confirmed in the public corpus** (Unity often serializes only non-default values, so the field may not exist in the file at all). **Use the CLI arg `-desiredWorkerCount 0`** — forces zero Parallel Import workers per-invocation. Cleaner than mutating any project file; required for headless Linux to avoid the documented "Won't Fix" Parallel Import bug.

**Concurrent batchmode + shared `.ulf`**: known problematic on Personal license. Each container has its own LicensingClient daemon; `.ulf` mount is fine read-only (just signed XML), but Unity's activation server may reject concurrent activations on Personal/Plus seats. GameCI docs explicitly warn: *"For paid licenses, you need to be mindful of starting too many parallel jobs as activation will fail."* For Personal: **assume serialization required** until empirically validated (A3.3).

**Unity Accelerator**: **skip on single-host multi-worktree.** Unity's own forum guidance (post 1093288): *"With Asset Database Pipeline V2, you would have a built-in cache database in the Editor so you would not need to have Unity Accelerator running for a single developer."* Accelerator's value is cross-machine LAN cache.

**Digest pinning**: GameCI **does republish** suffixed tags occasionally (their `-0`, `-1`, `-2`, `-3` suffix scheme indicates re-rolls). Pin by digest for production. Syntax: `docker pull unityci/editor@sha256:<digest>`. Lookup: `docker buildx imagetools inspect <tag>` or `docker inspect --format='{{index .RepoDigests 0}}' <tag>` after a pull.

### Unity Personal in containers — blocking discovery (2026-05-25) ⛔

**The earlier "GameCI image standalone confirmed" entry above is correct about the IMAGE mechanics** (no ENTRYPOINT, `.ulf` mount-path is `/root/.local/share/unity3d/Unity/Unity_lic.ulf`, license injection is opt-in via `UNITY_LICENSE` env var hook). **But it missed a deeper licensing-model issue** that surfaced only when we tried to activate.

**What we found when we ran A3.2**:
1. Pulled `unityci/editor:ubuntu-6000.3.11f1-base-3.2.2`, image works, `unity-editor -version` returns 6000.3.11f1. ✅
2. Generated a `.alf` activation file inside the container (works). Tried to upload to `https://license.unity3d.com/manual` — Unity's page **only offers Plus/Pro serial entry**. No Personal option, no `.alf` upload field. Confirmed Unity removed manual Personal activation in ~Aug 2023 (Unity Discussions: "Unity no longer supports manual activation of Personal licenses" — open thread, no Unity-staff response).
3. Searched the Windows host for the legacy `Unity_lic.ulf` — doesn't exist. Modern Unity Hub writes **`UnityEntitlementLicense.xml`** (5869 bytes) to `C:\Users\<user>\AppData\Local\Unity\licenses\` instead. Different format, different path, different licensing model entirely.
4. Tried running Unity in the container with no license: failed with `[Licensing::IpcConnector] Channel LicenseClient-root doesn't exist` — modern Unity (2021.2+) uses a separate **LicensingClient daemon** that the Editor talks to over Unix IPC; no daemon running = no license.
5. The GameCI `base-3.2.2` image has `Unity.Licensing.Client` binary at `/opt/unity/Editor/Data/Resources/Licensing/Client/` (108 MB) but **no startup hook** in `/usr/bin/unity-editor.d/` (directory empty). LicensingClient isn't autostarted; we'd need to wire it ourselves.
6. Even if we wired it up: modern Unity Personal license is **hardware-fingerprint bound** (machine-id + primary MAC + hostname, per Unity issue tracker). Windows host's XML won't validate in a Linux container (Docker Desktop containers see WSL2 VM hardware, not the Windows host's).

**Net**: there is **no Unity-blessed workflow for Personal-in-Linux-container in 2026.** Unity's de-facto answer: "Personal is for desktop dev; for headless/CI, use Pro + Build Server."

**What GameCI users actually do (2026 status):**
- Adopt `game-ci/unity-license-activate` — a Puppeteer script that logs into `id.unity.com` via headless Chrome, re-generates a license per CI job, hardcodes `/etc/machine-id` in the image so the same license works across jobs. Requires `UNITY_EMAIL` + `UNITY_PASSWORD` in plaintext (TOTP fragile).
- This flow is **constantly breaking** as Unity changes the login UI — `game-ci/docker#268` (open since Nov 2025: "Access token is unavailable; failed to update").
- It's unofficial and Unity-hostile. Indies use it because Pro is $2,200/yr/seat; small teams budget periodic "fix GameCI" time.

**Three pivot options for our plan** (owner decision required):

1. **Adopt GameCI Puppeteer flow.** Plan stays free ($60/mo target). Cost: brittle, requires plaintext Unity credentials in secrets, periodic breakage to fix. Plan-doc rewrites: A3.2 / A5.4 add the Puppeteer activation step + hardcoded machine-id in container image.
2. **Pivot architecture: Editor on Windows host, claude on Linux VM, SSH bridge.** Cloud VM hosts the chat layer + agent dispatch. When `claude` needs Unity batchmode, it SSHes to the Windows desktop (or a small Windows VM) and runs Unity there natively. Personal license works fine on Windows. Cost: the "desktop off" goal weakens — the Windows machine has to be on when Unity work happens. Plan-doc rewrites: A5/A6 become "SSH-to-Windows-Unity" instead of "Linux-container-Unity."
3. **Buy Unity Pro ($2,200/yr/seat ≈ $183/mo).** Pro supports manual activation, headless Linux is officially supported, no hacks. Plan economics: $60/mo → $243/mo. Justifiable if approaching the $200K/yr Personal revenue cap anyway, or if your time is worth more than $2,200/yr of brittle GameCI babysitting.

**Decision deferred to owner** (2026-05-25). Plan paused at A3.2 until decision is made. A3.5 / A3.6 (non-Unity ttyd+tmux+claude validation) could be unblocked independently if owner wants to continue chat-layer validation while deciding.

**Sources** (load-bearing):
- [Unity Discussions — Unity no longer supports manual activation of Personal licenses](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760)
- [Unity Issue Tracker — Cannot activate license within a docker container](https://issuetracker.unity3d.com/issues/cannot-activate-license-within-a-docker-container)
- [Unity Support — Machine Identification Is Invalid For Current License](https://support.unity.com/hc/en-us/articles/360039435032)
- [game-ci/docker#268 — Access token is unavailable (open Nov 2025)](https://github.com/game-ci/docker/issues/268)
- [game-ci/unity-license-activate](https://github.com/game-ci/unity-license-activate)
- [game-ci/documentation#408 — manual Personal flow broken (open since Aug 2023)](https://github.com/game-ci/documentation/issues/408)

### Local-validate-then-VM-deploy: partially abandoned (revised 2026-05-21)
- Original premise: validate every primitive on the user's Windows machine before paying for the VM, then migrate via `rsync + systemd units + tailscale up`.
- Reality after A2 reshape: **only A0, A1, and W1-W3 (Tailscale) validated on Windows.** The chat-layer + browser-terminal piece moved to the VM after two Windows tool attempts failed (see "Windows-local browser-terminal validation: abandoned" above).
- New ordering: validate Linux primitives on the VM under free-tier credit ($0) before any paid spend. GCP $300 / 90-day credit is the recommended free-tier provider (matches paid-Hetzner x86 architecture 1:1; full Unity validation possible).
- What we keep: A0 (subscription gate) and A1 (worktree + concurrent batchmode) still ran on the laptop. Both produced gotchas that carry forward to the VM regardless of OS. The exercise wasn't wasted.
