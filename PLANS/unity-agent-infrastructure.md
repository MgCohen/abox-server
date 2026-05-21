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

## Current state (last update 2026-05-20)

**Branch**: `phase-a/local-validation` (commits ahead of `main`: A0 → A1 → A2 rewrite). No PR open yet.

**Resume here**: Phase A2 step **W7** — validate `claude` in the browser-rendered VS Code terminal. W1–W6 status: Tailscale joined (laptop `matheus` 100.86.249.67 / phone `matheuss-z-fold6` 100.98.201.119, bidirectional via DERP relay, no direct UDP — harmless for HTTP). **W4 pivoted**: code-server install failed on Windows (postinstall.sh vendor-yarn step incompatible with Windows-native; Node 24 missing prebuilt argon2 + Node 20 hit EBUSY on rename during the inner VS Code build). Switched to **Microsoft `code serve-web`** (built into VS Code 1.120, local web server, no MS relay, full VS Code UI). Smoke-tested at `http://127.0.0.1:8080` — UI loads, workspace opens, but terminal access is the open question (owner reports no top menu bar and Ctrl+` not opening a terminal panel). Research in flight; W7 = "can we open a terminal in the web UI and run claude in it?" See §11 for pivot detail.

| Phase | Status |
|---|---|
| A0 — Local prerequisites | ✅ subscription confirmed (Max, firstParty), `infra/check-a0.ps1` codifies the gate |
| A1 — Worktree + Unity batch-mode concurrency | ✅ validated on vanilla Unity 6000.3.11f1 (parallel imports exit 0; shared `LicensingClient` confirmed); Scaffold cold-build issue spawned as a separate task |
| A2 — Windows-local Track A (code-server + Tailscale + `claude` + Unity, phone-accessible) | 🟡 **W1–W10 not started** |
| A3 | Merged into A2 (one Windows-native stack covers persistence + browser access) |
| A4–A7 — Hetzner VM | ⏸ Gated on A2 demo passing end-to-end and owner approval of cloud spend |
| Track B (agent dispatch) | ⏸ Comes after Track A is solid |

**Load-bearing constraints to re-load on resume**:
- **No Linux / WSL on the local Windows machine.** Track A is validated on Windows-native binaries (code-server, Tailscale, `claude.exe`). The VM later gets a Linux equivalent of the same architecture.
- **Multi-Unity-project host.** `C:\Unity\` holds Card Framework, Scaffold, Gear-Engine, and growing. VM layout mirrors this as `~/work/<project>/{main,slotN}/`.
- **Side task open**: Scaffold's `com.scaffold.schemas` is committed at `Assets/Packages/` AND declared as a git URL in `manifest.json` → 50+ GUID conflicts on cold build. A spawned task addresses this independently; it doesn't block this branch but must be resolved before Scaffold can be deployed to the VM.
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
              │  ~/work/<project>/main   ~/work/<project>/slot1  │
              │  (one tree per Unity project × N worktrees;       │
              │   each worktree gets its own Library/)            │
              └────────────────────┬──────────────────────┘
                                   ▼
              ┌───────────────────────────────────────────┐
              │  Unity -batchmode + Unity Accelerator     │
              └───────────────────────────────────────────┘
```

**Project scope**: the VM hosts **multiple distinct Unity projects** (e.g. `Card Framework`, `Scaffold`, …), each living under `~/work/<project>/` with its own `main` worktree plus N agent slot worktrees. Unity Hub is installed once; each project's pinned Editor version (from `ProjectSettings/ProjectVersion.txt`) is installed alongside the others. License activation happens once for the machine, not per project.

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
| Browser-accessible terminal/IDE | Surface that renders `claude` TUI in a browser | See note below: Windows uses `code serve-web` (Microsoft), VM uses `ttyd` or `code-server` (Linux) |
| GitHub Actions self-hosted runner | Dispatch via `workflow_dispatch` / webhooks | Enterprise |
| GitHub PAT (fine-grained) | Runner auth + dispatcher PR pushes | Enterprise |

### Optional
| Tool | Purpose | Trust tier |
|---|---|---|
| Windmill (self-host) | Alt control plane if we outgrow GH Actions | Enterprise-grade OSS |
| Caddy | TLS + reverse proxy | Standard OSS |
| Domain + Cloudflare DNS | Nicer URLs | Standard |
| Unity Accelerator | Shared asset import cache | Enterprise (Unity official) |

**Browser-terminal stack split (Windows-local vs. VM)** — code-server doesn't install cleanly on Windows-native (postinstall vendor-yarn step assumes Linux/macOS; Node 24 / Node 20 both fail). For Phase A2 (Windows-local Track A) we use **Microsoft's `code serve-web`** instead — it ships with VS Code 1.120+, runs a local web server (no Microsoft relay, distinct from `code tunnel`), exposes full VS Code UI + integrated terminal, has native Windows binary, and built-in 3-hour terminal reconnect grace (`[reconnection-grace-time] 10800000ms`). Microsoft Corp trust tier (parity with GitHub already in this table). For the VM (Phases A4+) we stay on the original `ttyd + tmux` stack — Linux-native, simpler, the source-of-truth target. The two surfaces don't need to match; Track A on the laptop is throwaway validation, the VM is what we keep.

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

### Phase A2 — Windows-local persistent `claude` in a browser, accessible from phone (code-server + Tailscale)

**Rewrite of original A2 + A3.** Owner constraint: no Linux / WSL on the local machine. Track A is validated end-to-end on the Windows host first; the VM (A4+) becomes "move this stack to a box that's always on," not "introduce new architecture." Track B's `tmux send-keys` pattern (§2.2) is Linux-specific and stays a VM concern — Track A doesn't need it.

**Architecture (Windows-native, all binaries pre-approved per §5):**
```
Phone browser ── Tailscale (encrypted, no public ports) ──► Laptop
                                                              │
                                                              ▼
                                          code-server (Windows Service)
                                                              │  persistent integrated terminal
                                                              ▼
                                          claude TUI (Windows .exe)
                                                              │
                                                              ▼
                                          Unity project worktree → Unity batch-mode
```

#### W1 — Tailscale on the laptop (10 min, owner action)
- [x] Sign up for Tailscale Personal (free) at `https://login.tailscale.com/start`. (Account: `tecocohen@gmail.com`.)
- [x] Install the Windows client from `https://tailscale.com/download/windows`. Run, sign in.
- [x] Laptop joined tailnet as `matheus` (100.86.249.67).

#### W2 — Tailscale on the phone (5 min, owner action)
- [x] Tailscale installed on phone (Android). Joined tailnet as `matheuss-z-fold6` (100.98.201.119). Both devices visible in admin console.

#### W3 — Phone-to-laptop reachability check (2 min)
- [x] Validated via `tailscale ping 100.98.201.119` from the laptop: 5/5 pongs returned, ~400–1500ms via DERP(fra) relay. Bidirectional tailnet routing confirmed.
- [x] **Note**: traffic is going via DERP relay, not direct UDP — likely CGNAT on home/mobile side. Harmless for HTTP (code-server :8080 will still work, just with relay latency added).
- [x] **Gate passed**: proceed to W4.

#### W4 — Verify `code serve-web` (5 min) [DONE]
- [x] `code serve-web --help` runs (VS Code 1.120 + `code-tunnel.exe` ship with this command natively, no install needed).
- [x] Smoke-tested at `127.0.0.1:8080` with `--without-connection-token --accept-server-license-terms --default-folder C:\Unity`. First boot downloads the Server payload (~22s) and starts the extension host on a named pipe. HTTP probe returns 200; UI loads in browser.
- [x] Log artifacts kept at `infra/logs/code-serve-web-smoke.log` for reference (non-fatal "File not found" warnings for `github-authentication`, `emmet`, `git-base`, `merge-conflict` browser-mode extensions — they don't ship with serve-web; harmless for our use).

#### W5 — Configure for our use (10 min)
- [ ] Decide host binding strategy:
  - **Safer**: `--host 127.0.0.1` + a Tailscale subnet route / Tailscale Serve proxy. Doesn't expose the port on the LAN at all.
  - **Simpler** (default plan): `--host 0.0.0.0 --port 8080` with `--connection-token <strong-secret>`, then a Windows Defender Firewall rule limiting inbound 8080 to the Tailscale interface only.
- [ ] Generate a connection token: `[Guid]::NewGuid().ToString()` (or stronger) → save to `%LOCALAPPDATA%\code-serve-web\token` (gitignored).
- [ ] Confirm Windows Defender Firewall behavior: rule for TCP 8080 inbound, scoped to the Tailscale interface (profile = Private; the Tailscale adapter advertises Private when connected). The Public profile must NOT have a rule for 8080.
- [ ] Set default workspace to `C:\Unity` (already smoke-tested with this).

#### W6 — Run `code serve-web` as a Windows Service via `nssm` (15 min)
- [ ] Install `nssm` — `winget install NSSM.NSSM`.
- [ ] Wrap the binary directly: `nssm install code-serve-web "C:\Users\mtgco\AppData\Local\Programs\Microsoft VS Code\code-tunnel.exe"`. Arguments: `serve-web --host 0.0.0.0 --port 8080 --accept-server-license-terms --connection-token-file %LOCALAPPDATA%\code-serve-web\token --default-folder C:\Unity --server-data-dir %LOCALAPPDATA%\code-serve-web\server-data`. (Use `code-tunnel.exe` directly, NOT `code.cmd` — nssm + cmd wrappers double-fork on Windows and don't track the child process for restart.)
- [ ] Set startup = Automatic, working dir = `C:\Unity`, exit action = Restart, log redirect = `C:\Unity\remote-unity-agents\infra\logs\code-serve-web.{out,err}.log` (rotated weekly via nssm log rotation).
- [ ] Start the service. Verify it survives a logoff/login cycle (close all RDP/console sessions, reconnect, port 8080 still serving).
- [ ] Commit `infra/install-code-serve-web-service.ps1` codifying the token gen + nssm install + firewall rule. Do not commit the token file itself.

#### W7 — `claude` in the browser-rendered VS Code terminal (5 min) [PENDING]
- [ ] **Resolved blocker from initial test**: `code serve-web` web UI hides the menu bar by default and some browsers hijack `Ctrl+Shift+P`. Canonical fixes (per [microsoft/vscode#95418](https://github.com/microsoft/vscode/issues/95418), [#205013](https://github.com/microsoft/vscode/issues/205013), [#219042](https://github.com/microsoft/vscode/issues/219042)): (a) press **F1** instead of Ctrl+Shift+P for the Command Palette, (b) click inside the empty editor area first so it has keyboard focus, (c) optional permanent fix — set `"window.menuBarVisibility": "classic"` in `%APPDATA%\Code\User\settings.json` and reload to restore the full menu bar. The terminal IS supported in serve-web (extension host runs a real shell) — no CLI flag needed.
- [ ] Open terminal via F1 → "Terminal: Create New Terminal". `cd` into a Unity project (e.g., `C:\Unity\Scaffold`), run `claude` interactively, confirm TUI renders, type a small read-only prompt, confirm response.
- [ ] **Gate** (revised): if F1 + click-to-focus still doesn't surface the terminal, suspect the connection token is missing (we ran `--without-connection-token` for smoke test — production needs the token). Re-test before escalating.

#### W8 — Persistence validation (10 min)
- [ ] In the same `claude` session, ask a question that requires it to remember context for the next message.
- [ ] Close the browser tab entirely. Wait 2 minutes.
- [ ] Reopen `http://127.0.0.1:8080` from the laptop. The terminal session should still be there — VS Code Server's `[reconnection-grace-time]` defaults to 10800s = 3 hours, which means terminal processes are held alive for that long after disconnect.
- [ ] Repeat with a longer gap (close browser, lock laptop, come back 10 min later). Confirm session still alive.
- [ ] **Gate**: if persistence is shorter than the documented grace-time, surface — we may need to pass `--reconnection-grace-time-ms` explicitly to the server (the CLI flag exists per VS Code source).

#### W9 — Phone demo (the moment of truth) (10 min)
- [ ] From phone over Tailscale: navigate to `http://100.86.249.67:8080` (or the tailnet hostname). Enter connection token. Confirm `code serve-web` UI loads on mobile.
- [ ] Attach to the existing terminal session from W7/W8 (VS Code remembers the last-open terminals on reconnect within the grace window). The TUI should render — confirm the same conversation is visible.
- [ ] Type a prompt from the phone. Confirm `claude` executes it and output appears.
- [ ] Close phone browser. Walk away. Come back in 10 min. Reload. Session still alive. **This is Track A working on Windows.**

#### W10 — Unity loop closure (10 min)
- [ ] From the phone-driven `claude` session, ask it to run a Unity batch-mode command (e.g., the wrapper at `infra/run-unity-import.ps1`) against a worktree. Watch it execute on the laptop, output streaming back to the phone. Confirms the full Track A + A1 + Unity stack works end-to-end on Windows-only.
- [ ] **Done with Windows-local Track A.** Decision point on whether to proceed to A4 (VM) for desktop-off / always-on operation, or stay on the laptop.

### Phase A3 — (merged into A2)

A3's original split (local web terminal as a separate phase from local tmux) made sense for the ttyd + tmux Linux stack. With code-server on Windows, both concerns collapse into one phase (W4–W10 above). Track A is fully covered by the new A2.

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
- [ ] `claude auth status` in a worktree → JSON confirms `subscriptionType: "max"`. Re-run `infra/check-a0.ps1` to re-validate the full A0 gate on the VM.

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

### Browser-terminal pivot: code-server → `code serve-web` (Windows-local only)
- Original plan: `code-server` on Windows via `npm install -g code-server`. Found in W4 (2026-05-21) that this fails on Windows:
  - Node 24: `argon2@0.28.4` fails to build (no prebuilts, requires VS Build Tools).
  - Node 20 (installed via fnm to get prebuilts): `argon2@0.31.2` installs cleanly, but `postinstall.sh` (which runs `yarn` inside `vendor/` to build VS Code's own native modules: node-pty, kerberos, spdlog) fails and triggers an `EBUSY` rename on rollback (Windows Defender holding the freshly-extracted directory).
  - coder/code-server's own docs state: "We currently do not publish Windows releases. We recommend installing code-server onto Windows with `npm`." — Windows is a second-class platform for the project.
- Considered: VS Build Tools install (~6 GB, no guarantee), Microsoft `code tunnel` (rejected: relays through MS public infra, violates §2 "no public ports" principle), ttyd + custom session shim (no tmux equivalent on Windows, would need ~80 lines of conpty wrapper).
- Chose: **Microsoft `code serve-web`** (ships with VS Code 1.120+, accessed via `code serve-web` or the `code-tunnel.exe` binary). Local web server only (no MS relay), full VS Code UI, integrated terminal, 3-hour reconnect grace by default. Native Windows binary, zero compile.
- Smoke-tested 2026-05-21: UI loads at `127.0.0.1:8080`. **Open question at time of write: how to actually invoke the integrated terminal from the web UI** — owner reported no top menu bar visible and Ctrl+\` not opening a terminal. Resolving in W7.
- Scope of this pivot: **Windows-local only**. VM (Phases A4+) stays on `ttyd + tmux` per the original plan; that's the Linux-native stack we keep long-term. The two halves don't need to match — Windows-local is throwaway validation, the VM is the production target.

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
