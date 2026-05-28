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

## Current state (last update 2026-05-25 — post-audit)

**Branch**: `phase-a/local-validation` (commits ahead of `main`: A0 → A1 → A2 rewrite → A2 reshape → A5 Hub-pivot → 2026-05-25 risk-register sweep). No PR open yet.

**Resume here**: Phase **A4** — provision Hetzner CCX23, then in A5 install Unity Hub + Editor natively on the VM (temporary xrdp for the one-time Hub sign-in / Personal activation, then torn down). A3.0 (Docker Desktop) ✅. **A3 narrowed**: Unity-in-container path (A3.1-A3.4) deleted after the 2026-05-25 licensing discovery — Personal can't run in Linux containers. A3.5 / A3.6 (ttyd+tmux+claude in container) kept as optional pre-flight if we want extra confidence on mobile rendering before VM spend. Full new Unity strategy in §11 "Unity Personal on Linux VM — canonical 2026 path."

**2026-05-25 audit**: full risk-register sweep of remaining unchecked steps (A3.5 → B5) against current 2026 reality. Result: no architecture changes; one load-bearing claim (Hetzner "MAC pinning") corrected to "MAC stability via server-resource binding"; A5 install path swapped from AppImage → apt repo; A5.4 activation simplified (Personal auto-activates on Unity ID signin); new A5 sub-step added for headless install of future Editor versions; xrdp setup hardened against `xrdp #3479` blue-screen failure mode; Tailscale ACLs added to A4.2; `-W` flag added to ttyd; A7 2-seat exhaustion path moved to self-service; `~/.claude` unbounded-growth caveat added to A7 backup scope; B2 supply-chain hardening (SHA-pinned actions) added. Full audit notes in §11 "2026-05-25 risk-register sweep."

| Phase | Status |
|---|---|
| A0 — Local prerequisites | ✅ subscription confirmed (Max, firstParty), `infra/check-a0.ps1` codifies the gate |
| A1 — Worktree + Unity batch-mode concurrency | ✅ validated on vanilla Unity 6000.3.11f1 (parallel imports exit 0; shared `LicensingClient` confirmed); Scaffold cold-build issue spawned as a separate task |
| A2 — Local Tailscale validation (reshaped) | ✅ W1–W3 done: laptop + phone joined tailnet, bidirectional `tailscale ping` confirmed. Browser-terminal validation moved to A3 / A4 (Linux primitives) after two Windows-native install attempts failed (code-server, code serve-web). See §11 for the journey. |
| A3 — Local Docker validation (narrowed) | A3.0 ✅. A3.1-A3.4 deleted (Unity-in-container is dead). A3.5/A3.6 (ttyd+tmux+claude in container, no Unity) kept as optional chat-layer pre-flight. |
| A4 — Linux VM: chat layer (Pass 1) | ⏸ Provision Hetzner CCX23 (referral credit if possible), install ttyd + tmux + claude, phone access via Tailscale. **No Unity yet.** Validates the chat layer on a non-personal-device host. |
| A5 — Linux VM: Unity layer (Pass 2 start) | ⏸ Native Hub + Editor install via temporary xrdp session (one-time, for Personal activation GUI flow), then xrdp torn down. Hetzner snapshot taken before teardown = A7 restore point. |
| A6 — VM worktrees + first batchmode in container | ⏸ |
| A7 — Permanent service (systemd) | ⏸ |
| Track B (agent dispatch) | ⏸ Comes after Track A is solid |

**Load-bearing constraints to re-load on resume**:
- **No Linux / WSL on the local Windows machine.** A2 reshape acknowledged this: browser-terminal validation belongs on Linux. Local Windows is for editing + Tailscale client only.
- **Multi-Unity-project host.** `C:\Unity\` holds Card Framework, Scaffold, Gear-Engine, and growing. VM layout mirrors this as `~/work/<project>/{main,slotN}/`.
- **Two-pass validation discipline.** Pass 1 (A4) validates the chat layer with no Unity. Pass 2 (A5–A6) adds Unity. The split isolates failures.
- **Unity Hub + Editor native on the Linux VM, NOT containerized.** Modern Unity Personal (2026) cannot be activated inside a Docker container (license is hardware-fingerprint bound, manual `.alf`/`.ulf` flow removed in Aug 2023). We treat the Linux VM as "a personal Linux machine I have" — Hub installed natively (via Unity's apt repo, not AppImage), Personal activated via temporary xrdp GUI session, Editor installed via Hub, license bound to the VM's MAC address. GameCI image considered but rejected — see §11 "Unity Personal in containers — blocking discovery" and "Unity Personal on Linux VM — canonical 2026 path."
- **Hetzner MAC stability via server-resource binding** (corrected 2026-05-25). Hetzner does **not** offer an explicit "MAC pinning" feature — but the MAC is implicitly stable for the life of the **server resource (server ID)**: survives reboot, stop/start, host migration, and `hcloud server rebuild --image <snap> <server-id>` (the in-place snapshot restore). MAC changes ONLY if the server resource is deleted and re-created. Required for A7 disaster recovery: **never delete the server resource**; protect it with billing alerts + account safeguards, not just snapshots.
- **2-seat Personal limit.** Laptop + VM = 2/2 seats consumed. Must "Return License" via Hub before destroying / re-provisioning the VM, or the seat is gone until Unity Support manually frees it.
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

What still validates locally (post-2026-05-25 pivot — A3 narrowed): claude TUI in ttyd on mobile, tmux send-keys reliability — A3.5 + A3.6, both optional pre-flight. Most of the original A3 (Unity-in-container) is moot now that Unity runs natively on the VM.

What requires the cloud VM (A4+): chat-layer end-to-end test, **Unity Hub install + Personal activation** (binds to the VM's hardware), Editor install, headless batchmode, real systemd-on-Ubuntu.

Migration to VM is **not** `rsync from laptop` — it's a fresh Ubuntu 22.04 install per A4.1-A4.3, then Hub-driven Unity install per A5. Reusable across hosts: our `infra/` scripts. Reusable on the *same* VM via Hetzner snapshot (A5.6): the activated license + installed Editor. Reusable to a *new* VM: nothing — must re-activate Personal (burns a seat).

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
              │  Unity -batchmode (Editor installed        │
              │  natively via Hub on the VM;               │
              │  Personal license, MAC-pinned)             │
              └───────────────────────────────────────────┘
```

**Project scope**: the VM hosts **multiple distinct Unity projects** (e.g. `Card Framework`, `Scaffold`, …), each living under `~/work/<project>/` with its own `main` worktree plus N agent slot worktrees. **Unity Hub is installed natively on the VM** (one-time, via temporary xrdp GUI for the sign-in dance — see A5). Each project's pinned Editor version (from `ProjectSettings/ProjectVersion.txt`) is installed via Hub, alongside the others. Personal license activates once for the machine, bound to the VM's MAC address; shared across all installed Editor versions and all worktrees.

**Hardware (default end-state, Hetzner path)**: one Hetzner CCX33 (8 vCPU / 32 GB / 240 GB NVMe), Ubuntu 22.04 LTS, behind Tailscale. (CCX23 / 4 vCPU / 16 GB / **160 GB NVMe** is the validation-budget floor; resize/upgrade to CCX33 once Pass 2 passes.) **GCP path**: `n2-standard-4` (4 vCPU / 16 GB) equivalent — pricier per spec but the $300 / 90-day credit covers the whole validation window. Decision made at A4.1.

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
| `ttyd` (canonical repo: `tsl0922/ttyd`) | Browser-accessible terminal on the VM. **Note**: last release 1.7.7 (March 2024); ~25 months without a tagged release as of May 2026 — maintenance limbo, not abandoned. All required flags (`-W`, `-i`, `--ping-interval`, `--max-clients`) still work. | Standard OSS — small, well-known, Linux-native |
| **Unity Hub** (AppImage, Linux) | Manages Editor installs + Personal license activation on the VM | Enterprise (Unity official) |
| **Unity Editor** (installed by Hub, per pinned version) | The Editor itself; runs natively in batchmode | Enterprise (Unity official) |
| `xrdp` + `xfce4` (TEMPORARY, A5 only) | Brief GUI session for Hub's one-time Personal activation flow; uninstalled after | Standard OSS |
| Docker (CE on the VM, optional) | Runtime for the chat-layer test container (A3.5) and B2 fire-and-forget GH runner if we use container isolation. **Not** used for Unity. | Enterprise |
| GitHub Actions self-hosted runner | Dispatch via `workflow_dispatch` (gh CLI from any device); webhooks deferred to post-v1 per §10. **Free on private repos for individual accounts** (a proposed self-hosted runner fee was floated in 2026 then **postponed** after community pushback — re-check before B2 implementation). | Enterprise |
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

**Why native Hub install on the Linux VM (not the GameCI Docker image)** — original plan adopted `unityci/editor` as the Unity substrate; A3.2 discovered (2026-05-25) that Personal can't be activated in a container in 2026 (Unity removed manual `.alf`→`.ulf` flow for Personal in Aug 2023; modern Personal is XML format + LicensingClient daemon, hardware-fingerprint bound to MAC + machine-id + hostname). Three options were on the table: GameCI's Puppeteer flow (brittle), Editor-on-Windows + SSH bridge (architecture pivot), or Unity Pro ($2,200/yr). We picked a fourth: **install Hub + Editor natively on the Linux VM as if it were a personal Linux desktop** — uses a temporary xrdp session for the one-time Hub sign-in / Personal activation, then tears down. This works because the activation happens *on* the VM, so the license binds to the VM's own hardware fingerprint (MAC-pinned via Hetzner so it survives snapshot restore). Trade-offs: lose container portability and version pinning by image SHA; gain a free, Unity-blessed licensing path with no Puppeteer hacks. Full rationale + sources in §11 "Unity Personal on Linux VM — canonical 2026 path."

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
| Hetzner CCX33 (Hetzner path) | **€48.49 (~$53)** | Post-April 2026 pricing. Can start on CCX23 (**€24.49**) and scale. Free for ~30 days on Hetzner referral credit (€20 signup credit + €10 to referrer after €10 spend). |
| Hetzner snapshot (post-A5 restore point) | ~€0.012/GB/mo, ~€0.50–1/mo for a CCX23 snapshot | One-time snapshot taken after A5 (Hub + Editor + Personal license activated). A7 disaster recovery = `hcloud server rebuild --image <snap-id> <server-id>` against the **same server resource** (preserves MAC + server ID, license stays valid). Different from "Hetzner Cloud Backups" (the 20% recurring option) — snapshots are explicit, one-shot, cheap, survive instance state loss BUT NOT server-resource deletion. |
| Hetzner Storage Box BX11 (backups) | **€3.20** | 1 TB SFTP target, restic-compatible, free intra-Falkenstein egress. Updated 2026 pricing (down from €3.81). For ongoing Library/ + `~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml` + `~/.claude/` data backups. Complementary to the post-A5 snapshot (snapshot = "fresh-VM reset point", Storage Box = "rolling data backups"). |
| GCP n2-standard equivalent (GCP path) | ~$100 | If we end up on GCP instead of Hetzner. Free for 90 days on $300 credit. |
| Backblaze B2 (backups, GCP path) | ~$0.18 | 30 GB nightly. Replaces Storage Box if VM is on GCP. |
| Tailscale | $0 | Personal plan (6 users / unlimited devices / 50 tagged resources). |
| Claude Max | (already paid) | — |
| Unity Personal | $0 | Free if revenue cap qualifies. |
| GitHub | $0 | Free tier covers self-hosted runners on private repos. |
| Domain | ~$1/mo | Optional. |
| **Total new spend (Hetzner path)** | **~€52/mo (~$57)** | CCX33 (€48.49) + Storage Box (€3.20) + optional domain (~€1). Post-April 2026 prices. CCX23-only floor for validation: ~€28/mo. |
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

### Phase A3 — Local Docker validation (narrowed scope after A3.2 discovery)

**History**: A3 was originally designed as a Linux-primitive pre-flight on the Windows laptop via Docker Desktop, validating: GameCI image + our real projects (A3.2), concurrent batchmode (A3.3), worktree+container mounts (A3.4), ttyd+tmux+claude rendering on mobile (A3.5), tmux send-keys reliability (A3.6). A3.2 ran into a hard licensing block (Unity Personal can't activate in Linux containers in 2026 — see §11 "Unity Personal in containers — blocking discovery") and the architecture pivoted to native Hub-on-VM install (see §11 "Unity Personal on Linux VM — canonical 2026 path").

**Current scope**:
- **A3.0 ✅ kept** (Docker Desktop install — still useful for A3.5/A3.6 and future B2 fire-and-forget container isolation).
- **A3.1 deleted** (GameCI research gate — answered, then mooted by the pivot).
- **A3.2, A3.3, A3.4 deleted** (Unity-in-container is not our substrate anymore).
- **A3.5, A3.6 kept as OPTIONAL pre-flight** — ttyd+tmux+claude rendering question is still real and the chat-layer container test on the laptop can de-risk A4.5 before we commit to a VM. Skip if you'd rather go straight to A4.

#### A3.0 — Install Docker Desktop ✅ (2026-05-22)
- [x] Installed Docker Desktop for Windows (v29.4.3) with WSL2 backend.
- [x] `docker run --rm hello-world` exit 0 — image pulled, container ran.
- [x] Host mount confirmed: `docker run --rm -v C:\Unity:/host alpine ls /host` lists all C:\Unity\* projects.
- [x] Versions captured at `infra/docker-desktop-version.txt`: Docker 29.4.3, WSL 2.7.3.0, kernel 6.6.114.1-1, Windows 26200.8457.
- **Gotcha**: WSL was in `REGDB_E_CLASSNOTREG` state pre-install. Fix was `wsl --install --no-distribution` from elevated PowerShell + reboot. Documented in the version log.

#### A3.1 — GameCI image research gate (deleted; superseded by pivot)
Replaced by the native-Hub-on-VM path. See §11 "Unity Personal on Linux VM — canonical 2026 path."

#### A3.2 / A3.3 / A3.4 — Unity-in-container validations (deleted; superseded by pivot)
The image is irrelevant to the chosen architecture. The `unityci/editor:ubuntu-6000.3.11f1-base-3.2.2` image (digest `sha256:ef80dca0…8021`) pulled in the 2026-05-25 attempt remains in local Docker cache; harmless, can be removed with `docker rmi` if disk pressure. `infra/docker/unity-images.txt` is kept on-branch as historical record (forward usefulness if we ever pivot back to containerized CI).

The `Unity_v6000.3.11f1.alf` file at `C:\Unity\unity-license\` is useless (manual `.alf`→`.ulf` flow is dead). Can be deleted.

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
- [ ] Run the container with `CLAUDE_CODE_OAUTH_TOKEN` passed via env var, bind ttyd to the laptop's tailnet IP: `docker run -d --name chat-test -e CLAUDE_CODE_OAUTH_TOKEN -p 100.86.249.67:7681:7681 chat-test`. Image's default CMD runs `ttyd -p 7681 --ping-interval 30 --max-clients 2 tmux new-session -A -s main claude`. (Ubuntu 22.04 apt ttyd is 1.6.3 — writable by default, no `-W` flag needed; verified 2026-05-25.)
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
  - **Hetzner**: `CX22` (2 vCPU / 4 GB, ~€4/mo) for the cheapest Pass-1 trial; or jump straight to `CCX23` (4 vCPU / 16 GB / **160 GB NVMe**, **€24.49/mo** post-April 2026) if you want one box for the whole journey.
  - **Oracle Always Free**: `VM.Standard.E2.1.Micro` (1 OCPU / 1 GB) — tight, but it works for Pass 1.
- [ ] **Ubuntu 22.04 LTS** (Unity 6.4 officially supports both 22.04 and 24.04 per current docs — 22.04 pin is a preference based on historical bug-repro density on 24.04, not a hard requirement; GLIBC 2.35 on 22.04 meets the ≥2.34 requirement for Editor 6000.3+).
- [ ] **Disk: stay on local NVMe (root disk).** Research §11: Hetzner attached Volumes are ~5-7× slower than local NVMe on 4k random IOPS (2021 benchmark; no comprehensive 2026 re-bench published — treat as a lower bound). For Unity's IOPS-bound asset import workload, the gap matters. CCX23 ships 160 GB NVMe; CCX33 240 GB — enough for 3-4 worktrees' Library/ plus the unbounded `~/.claude/` cache (see A7 note). For GCP: PD-Balanced 200-500 GB for /home; avoid Local SSD (lost on stop).
- [ ] SSH key on creation. Disable password auth.
- [ ] Note the VM's public IP.
- [ ] **Record the provider choice + server resource ID + primary MAC** in `infra/vm-host.txt` (gitignored). A7 backup destination branches on this (Storage Box for Hetzner; B2 for GCP). The server-ID is load-bearing for DR (see next bullet).
- [ ] **MAC stability — corrected approach (2026-05-25)**. Hetzner does **NOT** offer an explicit "MAC pinning" feature in the Cloud Console (we previously believed otherwise). The MAC is instead **implicitly stable for the life of the server resource**: it survives reboot, stop/start, host migration, and `hcloud server rebuild --image <snap> <server-id>` (in-place snapshot restore). MAC changes only if the **server resource itself is deleted and re-created**. **Implications**:
  - The Hetzner side requires no configuration step — MAC stability comes free as long as the server isn't deleted.
  - DR plan in A7 must use `hcloud server rebuild` (not "create new from snapshot") to preserve MAC.
  - **Protect the server resource itself**: enable billing alerts at the Hetzner account level so a missed payment can't auto-delete the resource; consider locking the Cloud project against accidental deletion via the Console.
  - Record both the **server resource ID** and the **primary MAC** in `infra/vm-host.txt` — the MAC is for license-troubleshooting reference; the server-ID is for the `rebuild` command.

#### A4.2 — Harden + Tailscale (½ hour)
- [ ] Create non-root user `agent` with sudo. Disable root SSH.
- [ ] **ufw**: enable, allow inbound 22 on the public interface *temporarily* (needed to bootstrap Tailscale via SSH from the laptop). Enable unattended-upgrades and fail2ban.
- [ ] Install Tailscale on VM: `curl -fsSL https://tailscale.com/install.sh | sh`; `sudo tailscale up`.
- [ ] Verify SSH-over-Tailscale works: from laptop, `ssh agent@<vm-tailnet-name>` succeeds; from phone (Termius or Tailscale SSH), same.
- [ ] **Then close public 22**: `sudo ufw delete allow 22; sudo ufw allow in on tailscale0`. SSH stays available over Tailscale only. The VM has zero open ports on the public interface.
- [ ] **Tailscale ACLs — port-level lockdown** (added 2026-05-25). Defense-in-depth on top of `-i tailscale0` interface binding: if a tailnet device is compromised, the blast radius shouldn't include SSH. Tag each device in the admin console:
  - VM: `tag:unity-vm`
  - Laptop: `tag:laptop`
  - Phone: `tag:mobile`
  
  Then write `infra/tailscale-acl.json` (apply via admin console → Access Controls):
  ```json
  {
    "tagOwners": {
      "tag:unity-vm": ["autogroup:admin"],
      "tag:laptop":   ["autogroup:admin"],
      "tag:mobile":   ["autogroup:admin"]
    },
    "acls": [
      { "action": "accept", "src": ["tag:laptop"], "dst": ["tag:unity-vm:22,7681,7682"] },
      { "action": "accept", "src": ["tag:mobile"], "dst": ["tag:unity-vm:7681"] }
    ]
  }
  ```
  Phone can only reach the chat-layer port (7681); SSH (22) + session-manager (7682, added in B1) restricted to laptop. Free on Personal plan. Verify with `tailscale ping --tsmp` from each device that only permitted ports succeed.

#### A4.3 — Install chat-layer base + re-validate A0 gate on Linux (½ hour)
- [ ] `apt install git tmux build-essential ttyd` — note: **NOT** `nodejs` or `python3` (research confirmed `claude` native install has no Node dependency; we add others only when a phase actually needs them).
- [ ] Install `claude` CLI via the official native installer: `curl -fsSL https://claude.ai/install.sh | bash`. Installs to `~/.local/bin/claude` with background auto-update. (Alternative: signed APT repo at https://downloads.claude.ai/claude-code/apt/stable if we want apt-tracked installs — research §11.)
- [ ] Set `CLAUDE_CODE_OAUTH_TOKEN` from `claude setup-token` (run on **laptop**, not VM — see warning below), exported once, used here. Place in `/etc/profile.d/claude.sh`, mode 0600.
- [ ] **⚠ Do NOT run `claude login` or interactive OAuth flow from the VM.** Cloudflare JS-challenges the OAuth login from Hetzner / cloud-provider IPs (`anthropics/claude-code` #21678, closed "not planned"). The token-export workflow above is the documented bypass: generate the token on a non-datacenter IP (your laptop), then export it on the VM. No browser ever opens on the VM. Confirmed working approach for 2026 cloud-VM `claude` usage.
- [ ] **Port the A0 gate to Linux**: write `infra/check-a0.sh` — bash equivalent of the existing `infra/check-a0.ps1`. Same gate logic: confirms `claude auth status` returns `subscriptionType: max` + `apiProvider: firstParty`, and that `ANTHROPIC_API_KEY` is unset or empty. Commit to the branch.
- [ ] Run `infra/check-a0.sh` on the VM. **Gate**: must return exit 0. If it fails (e.g., `apiProvider: console` because the token somehow routed through API, or `ANTHROPIC_API_KEY` got set by something), stop and debug before any further work. A subscription-billed run depends on this passing.
- [ ] **B2 validation (OAuth-on-cloud-VM billing)**: after one or two `claude` invocations on the VM, check the Anthropic dashboard (claude.ai/settings) and confirm usage drew from Max quota, not API credits. Research (§11) suggests this should work, but empirical confirmation closes the loop.

#### A4.4 — Chat-layer smoke test (¼ hour)
- [ ] Clone repo to `~/work/remote-unity-agents/main` (just for a real project context to point `claude` at).
- [ ] Start a tmux session manually: `tmux new -s main -c ~/work/remote-unity-agents/main`. Inside, run `claude`. Confirm TUI starts.
- [ ] Detach (Ctrl-b d). Re-attach (`tmux attach -t main`). Confirm session intact.

#### A4.5 — ttyd in front of tmux (¼ hour)
- [ ] `ttyd -p 7681 -i tailscale0 --ping-interval 30 --max-clients 2 tmux new-session -A -s main` — binds to the Tailscale interface only, attaches/creates the tmux session if it doesn't exist (`new-session -A` is idempotent), pings every 30s to survive idle-proxy timeouts, allows 2 concurrent clients (active + reconnecting). **Note on `-W` flag**: ttyd 1.7.4+ flipped to read-only-by-default and requires `-W` for input. **But Ubuntu 22.04 apt ships ttyd 1.6.3** (4 years old, writable by default, `--writable`/`-W` flag doesn't exist yet). Empirically confirmed in A3.5 container build. If you ever upgrade ttyd (snap, GitHub release binary, source build to 1.7.7), add `-W`. With apt's 1.6.3, no flag needed.
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

### Phase A5 — Linux VM: native Hub + Editor + Personal activation (Pass 2 starts) (½ day)

**Goal**: install Unity Hub + Editor natively on the VM, activate Personal via temporary xrdp GUI, install pinned Editor version(s), validate headless batchmode against a real project, take a Hetzner snapshot, tear down the GUI.

**Substrate decision (2026-05-25 pivot)**: native install via Hub, NOT via the GameCI Docker image. Unity Personal in 2026 can't be activated in a Linux container — license is XML format bound to MAC + machine-id, and the manual `.alf`→`.ulf` flow for Personal was removed in Aug 2023. By activating Hub on the VM directly (using a temporary xrdp session for the GUI), the license binds to the VM's own hardware, which is MAC-pinned at the provider level (A4.1). See §11 "Unity Personal in containers — blocking discovery" and "Unity Personal on Linux VM — canonical 2026 path" for the full investigation.

**Seat economics warning**: Personal licenses have a 2-seat-per-Unity-ID limit. Activating on the VM consumes seat 2 (laptop is seat 1). Before destroying or re-provisioning the VM, **MUST "Return License" via Hub first** — otherwise the seat is gone until Unity Support manually frees it.

#### A5.1 — Resize VM if needed (10 min)
- [ ] Pass 2 needs ≥8 GB RAM (Unity minimum 8 GB official, 16 GB realistic). If Pass 1 ran on a 4 GB box, resize now or provision a fresh CCX23 (4 vCPU / 16 GB / 160 GB NVMe) and re-run A4 on it. **If you re-provision** (new server resource = new MAC + new server-ID): re-record both in `infra/vm-host.txt` before A5.2, and confirm billing-alert / account-safeguard setup per A4.1. (In-place resize of an existing CCX22 → CCX23 via `hcloud server change-type` preserves server-ID + MAC, so it's preferred when feasible.)

#### A5.2 — Install xrdp + lightweight desktop (TEMPORARY, ½ hour)
- [ ] `apt install xfce4 xfce4-goodies xrdp`.
- [ ] **`sudo adduser xrdp ssl-cert`** — the `xrdp` service user (not the agent user) needs ssl-cert group membership to read the snakeoil TLS cert; without this, mstsc connects but the X session never starts.
- [ ] **`echo "xfce4-session" > /home/agent/.xsession && chown agent:agent /home/agent/.xsession`** — required to avoid the well-documented blue/black screen hang on Ubuntu 22.04 (`neutrinolabs/xrdp #3479`). Without this, xrdp doesn't know what session to launch and stalls after auth.
- [ ] **TLS hardening** in `/etc/xrdp/xrdp.ini` (matches Windows 11 mstsc defaults): under `[Globals]` ensure `security_layer=negotiate`, `crypt_level=high`, `ssl_protocols=TLSv1.2,TLSv1.3`. Restart xrdp after edit.
- [ ] `systemctl enable --now xrdp`.
- [ ] **Firewall**: `sudo ufw allow in on tailscale0 to any port 3389`. Never expose 3389 on the public interface — RDP brute-forced from the public internet within hours.
- [ ] Confirm xrdp running: `systemctl status xrdp`.
- [ ] **⚠ Connection-time discipline**: at the xrdp login screen pick **Xorg** (NOT Xvnc — Xvnc has rendering issues with Hub). **Do NOT have a local console login on the VM** simultaneously with the RDP session — concurrent local + RDP logins on Ubuntu xrdp cause session corruption.
- [ ] **Note**: this is *temporary infrastructure* — uninstalled in A5.6 once Hub + license are set up. Additional Editor versions later don't need xrdp re-installed (use `unityhub --headless install` per A5.7).

#### A5.3 — RDP from laptop, install Unity Hub via apt repo (½ hour)
- [ ] From Windows laptop: open built-in `mstsc.exe` (Remote Desktop). Host = `<vm-tailnet-name>:3389` or `<vm-tailnet-ip>:3389`. User = `agent`. On first connect, click through the self-signed cert warning (snakeoil cert). Should land in an xfce desktop.
- [ ] **Install Unity Hub via Unity's official apt repo** (canonical path in 2026 — preferred over AppImage; gives auto-updates, no libfuse dance, no manual file management):
  ```bash
  sudo install -d /etc/apt/keyrings
  curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg
  echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list
  sudo apt update && sudo apt install unityhub
  ```
- [ ] **If install hangs**: recent Hub release notes call out an AppArmor profile-loading stall on Ubuntu 22.04. Reboot and retry; usually clears on second attempt.
- [ ] Launch Hub from xfce app menu (or `unityhub` from terminal in the xrdp session).
- [ ] Click "Sign in" → opens browser to id.unity.com → log in with your Unity account. **Personal license auto-activates** on signin if your account holds no paid license (no separate "Add license" click needed — Unity Hub UI changed; the old "Get a free Personal license" button is gone). Revenue cap acceptance is EULA-only.

#### A5.4 — Confirm Personal active + install Editor 6000.3.11f1 (½ hour)
- [ ] **Personal already activated** in A5.3 (auto on Unity ID signin). Verify in Hub: **Preferences → Licenses** should show one Personal seat active for this machine, with a **"Return license"** button visible (same UI as Pro on Linux — confirms self-service release works for the 2-seat exhaustion scenario in A7).
- [ ] Hub has written `UnityEntitlementLicense.xml` to **`~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`** (confirmed path, 2026) and started the `Unity.Licensing.Client` daemon. License is now bound to this VM's MAC + machine-id + hostname.
- [ ] In Hub: **Installs** → **Install Editor** → search for `6000.3.11f1` → install with default modules (or add Linux Build Support if you want to ship Linux Player builds; otherwise base is fine for asset import / script compile). Download is ~5 GB.
- [ ] **Verify headless invocation works** (still inside xrdp, but using a terminal): `~/Unity/Hub/Editor/6000.3.11f1/Editor/Unity -batchmode -quit -nographics -logFile -` → exit 0, no license errors.
- [ ] **Back up the license XML immediately** (cooldown applies to re-activation):
  - [ ] `scp ~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml` back to laptop into `C:\Unity\backups\unity-license\UnityEntitlementLicense.xml.<vm-hostname>.<date>`.
  - [ ] Note: this XML is hardware-fingerprint bound. It won't work on a different machine. The backup is for restoring to the *same* VM via `hcloud server rebuild` (see A7) — primarily insurance against accidental local deletion of the file.

#### A5.5 — Disable Parallel Import via CLI flag (no code change needed)
- [ ] Same as before: `infra/unity-batchmode.sh` (factored in A5.7) passes `-desiredWorkerCount 0` on every invocation. No per-project `EditorSettings.asset` mutation needed. See §11 for why this is the canonical workaround.

#### A5.6 — Take Hetzner snapshot, tear down xrdp + xfce (¼ hour)
- [ ] **Take a Hetzner snapshot of the VM right now** (Hetzner Cloud Console → instance → snapshots → "Take snapshot," label as `post-A5-unity-activated`). This is the A7 disaster-recovery restore point. Includes Hub, Editor, activated license, and (briefly) the xfce/xrdp install.
- [ ] **Important**: this snapshot must restore to the **same VM ID** to preserve MAC. Restoring to a new VM = new MAC = invalid license. Document this loud in `infra/runbook.md`.
- [ ] Tear down GUI: `apt purge xfce4* xrdp` + `apt autoremove`. Closes the temporary GUI surface, frees ~1 GB disk.
- [ ] `sudo ufw delete allow in on tailscale0 to any port 3389`. Port 3389 closed.
- [ ] Verify Editor still runs headlessly: `~/Unity/Hub/Editor/6000.3.11f1/Editor/Unity -batchmode -quit -nographics -logFile -` → exit 0. Hub is unused from here on but stays installed (useful if we ever need to install another Editor version or Return License).

#### A5.7 — Factor `unity-batchmode.sh`, smoke test against Card Framework (½ hour)

This wrapper script is called from A6, B1, and B2 — extract it once.

- [ ] Write `infra/unity-batchmode.sh`: takes `<project-path>` (required), optional `<editor-version>` (default: read from `<project-path>/ProjectSettings/ProjectVersion.txt`), optional extra args. Resolves Editor binary path via `~/Unity/Hub/Editor/<version>/Editor/Unity`, runs `Unity -batchmode -quit -nographics -projectPath <path> -desiredWorkerCount 0 "$@" -logFile -`. Captures exit code, propagates. (No Docker — pure native invocation.)
- [ ] **Headless install for additional Editor versions** (2026 unlock — no second xrdp dance required): `unityhub -- --headless install --version <X.Y.Zf1> --changeset <hash> [--module <m>]`. Works over plain SSH after the first GUI activation; documented at https://docs.unity.com/en-us/hub/hub-cli. Find the changeset hash on https://unity.com/releases/editor/whats-new/<version>. The wrapper script auto-resolves Editor binary path; only the install step changes per version.
- [ ] Make executable, commit.
- [ ] Clone Card Framework into `~/work/CardFramework/main`.
- [ ] Run: `bash infra/unity-batchmode.sh ~/work/CardFramework/main`. Watch for clean exit 0, Library/ populated.
- [ ] **Gate**: if this fails (license activation lost, GLIBC mismatch, file perms, anything), surface and stop. The Hub-native-install thesis depends on this passing.

### Phase A6 — VM worktrees + first phone-driven batchmode (¼ day)

**Goal**: prove the full stack — phone-driven `claude` session → spawn Unity batchmode natively on the VM → see output streaming back to phone — works end-to-end on Linux.

- [ ] `git worktree add ~/work/CardFramework/slot1 <branch>` and `slot2`. Confirm independent Library/ per worktree (per A1's findings).
- [ ] Provisioning script `infra/worktree-add.sh`: creates worktree, ensures `Assets/` exists (per A1 gotcha #1 — empty Unity dirs don't survive git), seeds an empty Library/ directory, fixes mode bits if needed. Test it on a fresh worktree before relying on it.
- [ ] From phone-driven `claude` session (the persistent tmux-main session from A4.6, opened at `~/work/remote-unity-agents/main` — claude can shell out to any path): ask it to run `bash infra/unity-batchmode.sh ~/work/CardFramework/slot1` (factored in A5.7). Watch output stream back to the phone.
- [ ] **Gate**: phone → claude → unity-batchmode.sh → Unity Editor → exit 0. **This is the end-state Track A working.** Desktop can be off, work from anywhere, agent can drive Unity.

### Phase A7 — Harden as permanent service (½ day)

- [ ] `apt install restic` (used for nightly backups below).
- [ ] All services run as systemd units, `Restart=always`, log to journald, slice-scoped (per A4.6).
- [ ] (Optional) Caddy in front of ttyd for `https://chat.<domain>` with TLS via Let's Encrypt; otherwise the raw HTTP on tailnet is fine (Tailscale already encrypts). If we ever need a non-Basic-auth on ttyd (per research §11: ttyd has no native token/OAuth), put Caddy in front with `forward-auth` to Authelia/Tinyauth/oauth2-proxy.
- [ ] **Backup destination** (read `infra/vm-host.txt` from A4.1 to branch; per research §11):
  - If VM is on Hetzner: **Hetzner Storage Box BX11** (~€3.81/mo for 1 TB, free intra-Falkenstein traffic, SFTP backend works with `restic`). Best when staying on Hetzner.
  - If VM is on GCP: **Backblaze B2** ($0.18/mo for 30 GB, native restic backend, fast restore, free egress via Cloudflare Bandwidth Alliance). Best when geo-separated from primary host.
  - If VM is on Oracle: skip backups for the validation phase; revisit when promoting to a paid box.
- [ ] Nightly `restic backup ~/work/*/main/Library/ ~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml /etc/systemd/system /home/agent/.claude/ /home/agent/.config/claude` (or equivalent paths). Test restore monthly against a scratch directory. Note: the Unity license XML is bound to this VM's MAC + machine-id — backup is for restore to the *same* VM (via `hcloud server rebuild` against the same server-ID), not for cloning to a new one.
- [ ] **⚠ `~/.claude/` grows unbounded** (anthropics/claude-code #24207 — users report 3.2 GB after 2 months, some 200-472 GB). Add to nightly: `find ~/.claude/projects/*/sessions -mtime +30 -delete` (or similar retention). Watch CCX23 160 GB disk: a 4-worktree Scaffold install (~12 GB Library each = 48 GB) + Unity 5 GB + `~/.claude` (variable, plan 20 GB ceiling) + OS leaves ~80 GB headroom. Promote to CCX33 (240 GB) before it gets tight.
- [ ] Document the GH Actions runner deployment shape: **ephemeral / just-in-time (JIT) runner only**, one job per runner instance, then destroyed. Never autoscale persistent runners. **Never** scope to public repos. Runner unit goes in `batch.slice`. See B2 for details.
- [ ] **Write the full `infra/runbook.md`** — disaster recovery now branches on failure mode:
  - **VM disk corruption / config drift, server resource intact**: `hcloud server rebuild --image <snapshot-id-from-A5.6> <server-id>` against the same server. MAC + server-ID preserved → license stays valid. Then restore Library/ + `~/.claude/` caches from restic backup. Estimated <30 min if snapshot is recent. **This is the command, not "restore snapshot" in the Console — `rebuild` is the in-place operation; create-from-snapshot would mint a new server with a new MAC.**
  - **Server resource deleted / Hetzner account locked / need to migrate to a new VM**: provision fresh VM (new server-ID, new MAC), re-do A4 + A5 (including re-activating Personal license — consumes a seat from the dead VM). Then restore Library/ + `~/.claude/` caches. Estimated 2-3 hours. **Account-level safeguard to prevent this**: enable Hetzner billing alerts; consider Cloud project locks; don't manually delete the server.
  - **Personal license seat exhausted (both seats taken by laptop + dead VM)**: log in to **id.unity.com → Account → Active Devices** and self-release the dead VM's seat (no support ticket needed). Alternatively, "Return License" via Hub on the active VM. Only if both seats are inaccessible (e.g., both VMs destroyed without Return-License) do you need to contact Unity Support.
  - Test the `hcloud server rebuild` path once on a throwaway VM before declaring done. Use `hcloud snapshot create --description test` + `hcloud server rebuild --image <id> <server>` + verify Unity license still validates post-rebuild.
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

**Note on ordering**: on the VM. Self-hosted GH Actions runner needs to spawn Unity batchmode against the natively-installed Editor (per A5), which lives on the VM. Cannot run on Windows laptop.

**Security shape (per research §11)**: ephemeral / just-in-time (JIT) runner only — one job per runner, destroyed after. Never autoscale persistent runners. Never scope to public repos. Avoid mounting `/var/run/docker.sock` blindly (= root on host); use rootless Docker or invoke `docker` via a constrained user. Use OIDC for cloud creds if any are needed (not long-lived secrets in env).

**⚠ Supply-chain hygiene (added 2026-05-25)**: pin all third-party GitHub Actions by **commit SHA, not by tag**. 2025-2026 supply-chain attacks against the Actions ecosystem have been frequent and severe:
- **CVE-2025-30066** (`tj-actions/changed-files`): 23,000+ repos compromised via retroactive tag mutation.
- **Nov 2025 Shai-Hulud worm**: installed rogue runners on compromised hosts.
- **March 2026 `trivy-action` force-push**: 75/76 version tags compromised.

Pattern: `uses: org/action@<40-char-sha> # v1.2.3` (SHA is load-bearing; the comment is for readability). Apply to **every** non-`actions/`-namespace action in `agent-task.yml`.

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
- **Unity Personal licensing fragility on the VM** (managed via the canonical 2026 path — native Hub + server-resource-stable MAC, see §11). Real risks under this model:
  - **2-seat exhaustion**: dead-VM seat usually self-releasable via id.unity.com Active Devices (2026-05-25 audit confirmed). Only Unity Support if both seats inaccessible. Mitigation: runbook checklist in A7; consider automating Return-License as a pre-shutdown systemd ExecStop hook.
  - **Server-resource deletion**: only failure mode that breaks the license. Mitigation: Hetzner billing alerts so account-level issues can't auto-delete; Cloud project lock; runbook checklist (A7) treats the server-ID as load-bearing.
  - **Hub UI changes**: Unity could change the Personal activation flow in Hub at any time (they killed manual `.alf` upload in Aug 2023; in 2026 they removed the explicit "Get a free Personal license" button — Personal now auto-activates on signin). Mitigation: low impact — re-activation is once per VM lifetime, manual GUI step, not automated.
  - **ttyd maintenance limbo**: last release 1.7.7 (March 2024); ~25 months without a tagged release as of May 2026. Functional but stagnant. Mitigation: monitor for replacement; siteboon/claudecodeui (B5) is the upgrade path if rendering issues mount.
- **Anthropic could harden against PTY-driving the TUI** — Path B-session relies on the interactive CLI being scriptable via tmux. If they detect/block this, fallback is API-billed dispatch. Watch policy updates.
- **Unity headless Parallel Import bug** — known. Disabled per-invocation via `-desiredWorkerCount 0` CLI flag (baked into `infra/unity-batchmode.sh`). Re-test on each Editor version bump.
- **`siteboon/claudecodeui` if adopted later (Phase B5)** — high-alert dependency: handles tokens + shells, ~11k stars but smaller maintainer base than enterprise tools. Not adopted in v1; only revisited if mobile UX in A4.5 / A7 surfaces real pain that ttyd can't fix. Adoption requires security review + sandboxed trial per §5.
- **Single VM = single point of failure** — Hetzner outage stops everything. Acceptable for solo dev; revisit when load-bearing.
- **tmux send-keys quoting** — prompts with special characters need careful escaping (or write to a file and have `claude` read it).

---

## 10. Open questions

- [x] ~~Unity licensing path for headless Linux~~ **Resolved (2026-05-25 pivot): native Hub install on the VM via temporary xrdp.** Personal can't run in Linux containers (see §11 "Unity Personal in containers — blocking discovery"), so we treat the Linux VM as "a personal Linux machine I have" — Hub installed natively, Personal activated via brief xrdp session, license MAC-pinned, snapshot taken for A7 restore point. Full canonical workflow in §11 "Unity Personal on Linux VM — canonical 2026 path." Plan-doc A3-A7 rewritten accordingly.
- [x] ~~GameCI-image-without-license-automation research~~ **Resolved.** Image works standalone for paid licenses; for Personal it doesn't (the LicensingClient daemon + hardware-fingerprint binding don't permit it). We adopted native Hub install instead. GameCI image kept as a "considered, rejected" reference in §11.
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

### GameCI Docker image as Unity-on-Linux substrate (2026-05-21) — ⚠ SUPERSEDED by 2026-05-25 pivot

**Note**: this entry documents a decision that was *later reversed* when A3.2 discovered the Personal-in-container licensing block. The actual Unity substrate we adopted is native Hub install on the VM — see "Unity Personal on Linux VM — canonical 2026 path" further below. The text below describes the rejected approach for historical context only.

Decided in Q2 of the 2026-05-21 plan-doc reconciliation. GameCI is three separable pieces — we take only piece (1):
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

**Decision made (2026-05-25)**: ⬇ see next §11 entry "Unity Personal on Linux VM — canonical 2026 path." None of the three options above were picked — owner suggested a fourth: treat the Linux VM as "a personal Linux machine I have" — install Hub natively on the VM, activate Personal via temporary GUI session, license binds to VM's own hardware fingerprint. Research-validated; plan rewritten accordingly.

**Sources** (load-bearing):
- [Unity Discussions — Unity no longer supports manual activation of Personal licenses](https://discussions.unity.com/t/unity-no-longer-supports-manual-activation-of-personal-licenses/926760)
- [Unity Issue Tracker — Cannot activate license within a docker container](https://issuetracker.unity3d.com/issues/cannot-activate-license-within-a-docker-container)
- [Unity Support — Machine Identification Is Invalid For Current License](https://support.unity.com/hc/en-us/articles/360039435032)
- [game-ci/docker#268 — Access token is unavailable (open Nov 2025)](https://github.com/game-ci/docker/issues/268)
- [game-ci/unity-license-activate](https://github.com/game-ci/unity-license-activate)
- [game-ci/documentation#408 — manual Personal flow broken (open since Aug 2023)](https://github.com/game-ci/documentation/issues/408)

### 2026-05-25 risk-register sweep — all remaining steps audited against 2026 reality

After the §11 "Unity Personal on Linux VM" pivot was committed, ran a full audit of every unchecked step (A3.5 → B5) against current 2026 sources to surface unknowns before VM provisioning. Four parallel research agents covered: Hetzner infra (MAC + snapshots + pricing), Unity Hub + xrdp current state, Anthropic billing + CLI policy, Tailscale + ttyd + GH runner state.

**Net result: no architecture changes. One load-bearing claim corrected, several improvements adopted, several confirmations.**

**🔴 Corrections (claims were wrong as written)**:
- **"Hetzner MAC pinning" feature does NOT exist.** Reworded across A4.1, §2.4, §9, and the canonical-2026-path entry below. Reality: MAC is **implicitly stable for the life of the server resource (server ID)** — survives reboot, stop/start, host migration, and `hcloud server rebuild --image <snap> <server-id>` (in-place snapshot restore). Only deletion of the server resource itself changes the MAC. Mitigation moves from "configure pinning" → "protect the server resource itself" (billing alerts, account safeguards).
- **Hetzner snapshot restore is `hcloud server rebuild`, not generic "restore"** — A7 runbook now specifies the command. `rebuild` overwrites disk in place, preserves server-ID + MAC. The alternative (create-from-image) mints a new server with a new MAC, invalidating the license.
- **CCX23 disk is 160 GB NVMe, not 80 GB** — fixed in §3 + A4.1.
- **Post-April 2026 pricing**: CCX23 €24.49, CCX33 €48.49, Storage Box BX11 €3.20. §6 updated.
- **Volume vs NVMe IOPS gap is ~5-7× (2021 bench), not 10-17×** — softened in A4.1 disk note. Direction (stay on local NVMe) unchanged.

**🟡 Improvements adopted (better way to do the same thing in 2026)**:
- **A5.3 Hub install: AppImage → apt repo.** Unity's docs now canonicalize the deb repo at `https://hub.unity3d.com/linux/repos/deb stable main`. Auto-updates via apt, no libfuse2 dance, no manual file management. AppImage path still exists but is no longer recommended.
- **A5.2 xrdp hardening**: added `sudo adduser xrdp ssl-cert` (the xrdp service user, not the agent user), `~/.xsession` with `xfce4-session` (avoids `neutrinolabs/xrdp #3479` blue-screen hang), TLS hardening in `xrdp.ini`, and explicit "pick Xorg not Xvnc, no concurrent local console login" connection-time discipline.
- **A5.4 Personal activation simplified**: in 2026 Hub, Personal **auto-activates on Unity ID signin** (no "Add license → Get a free Personal license" click — that button was removed). Revenue cap is EULA-only.
- **A5.7 headless install for future Editor versions**: `unityhub -- --headless install --version <X.Y.Zf1> --changeset <hash>` works over plain SSH after the first GUI activation. **Eliminates the recurring xrdp risk** — only the FIRST license activation needs GUI; all subsequent Editor installs are SSH-only.
- **A4.5 / A3.5 ttyd flag**: added mandatory `-W`. Since ttyd 1.7.4 (March 2024) the terminal is read-only by default; without `-W` the page renders but keystrokes are silently ignored.
- **A7 2-seat exhaustion path**: moved to self-service via id.unity.com → Active Devices (no Unity Support ticket needed unless both seats inaccessible).
- **A4.2 Tailscale ACLs**: layered on top of `-i tailscale0` interface binding — phone restricted to port 7681 only, SSH (22) + session-manager (7682) restricted to laptop. Free on Personal, ~20 lines of JSON, defense-in-depth.
- **A4.3 Cloudflare-from-Hetzner warning**: `claude login` from a Hetzner IP gets JS-challenged by Cloudflare (`anthropics/claude-code` #21678, closed not-planned). Plan already does the right thing (token generated on laptop, exported on VM) but now explicitly calls out "do NOT run `claude login` on the VM."
- **A7 backup scope**: added `~/.claude/` to restic paths + retention pruning. GitHub #24207 reports unbounded growth (3.2 GB / 2 months typical; 200-472 GB in extreme cases). Disk-sizing implication for CCX23.
- **B2 supply-chain hardening**: pin all third-party Actions by commit SHA, not tag. References CVE-2025-30066 (tj-actions, 23K repos), Nov 2025 Shai-Hulud worm, March 2026 trivy-action force-push.

**🟢 Confirmations (claims that DID hold up)**:
- **June 15, 2026 Anthropic billing split**: interactive TUI on Max **unchanged**. `tmux send-keys` driving the TUI is unaffected (split is by binary mode, not transport). Only `claude -p` moves to API-billed Agent SDK credit pool. Max 5x gets ~$100/mo Agent SDK credit, Max 20x ~$200/mo.
- **`claude` install + auth lifecycle**: `claude.ai/install.sh`, APT repo, `claude setup-token` (1-year OAuth), `claude auth status` JSON schema (`subscriptionType: "max"`, `apiProvider: "firstParty"`) all unchanged.
- **`tmux send-keys` driving TUI**: no ToS prohibition, no anti-automation. Anthropic ships official docs examples of piped/scripted usage. Ecosystem of tmux-driver tooling (tmai, NTM, tui-use, amux, hermes-agent) active and unblocked.
- **Ubuntu 22.04 vs 24.04**: Unity 6.4 officially supports both. 22.04 pin is preference (GLIBC 2.35 meets ≥2.34 requirement), not requirement.
- **Tailscale Personal**: 6 users / unlimited devices / 50 tagged resources / Funnel / SSH / ACLs all free, unchanged.
- **GH JIT runners (`--ephemeral`)**: setup unchanged. Free on private repos for individual accounts (proposed fee postponed).
- **restic + Storage Box / B2**: all stable. B2 still ~$0.18/mo for 30 GB with Cloudflare Bandwidth Alliance free egress.
- **xterm.js mobile (Pixel 7 Pro #4279)**: issue is closed but no documented fix commit found — treat as unverified. Claude Code `/tui fullscreen` (v2.1.89+) is now real and documented but explicitly hedges on mobile compatibility. Mitigation: test on actual device in A4.5; fallback path is siteboon/claudecodeui (B5).

**🎉 Net effect**: the Hub-native-on-VM pivot turns out **even cleaner** than written. Apt-repo install is simpler than AppImage; headless install for future Editors removes a major recurring risk; self-service seat release removes the Unity Support dependency; MAC stability comes free with server-resource binding.

**Sources** (load-bearing, post-audit):
- [Unity Hub Linux install — official docs](https://docs.unity.com/en-us/hub/install-hub-linux) (apt repo path)
- [Unity Hub CLI reference (`--headless install`)](https://docs.unity.com/en-us/hub/hub-cli)
- [Unity Support — "License is already active on two devices" (self-release)](https://support.unity.com/hc/en-us/articles/39943726903060)
- [neutrinolabs/xrdp #3479 — Xorg blue screen on Ubuntu 22.04](https://github.com/neutrinolabs/xrdp/issues/3479)
- [Hetzner Docs — Backups/Snapshots](https://docs.hetzner.com/cloud/servers/backups-snapshots/overview/)
- [Hetzner Cloud API — `server rebuild`](https://docs.hetzner.cloud/)
- [Hetzner price adjustment April 2026](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/)
- [Anthropic — Claude Code Billing Changes June 15, 2026 (article 15036540)](https://support.claude.com/en/articles/15036540)
- [anthropics/claude-code #21678 — Cloudflare blocks OAuth from Hetzner](https://github.com/anthropics/claude-code/issues/21678)
- [anthropics/claude-code #24207 — `~/.claude` unbounded growth](https://github.com/anthropics/claude-code/issues/24207)
- [Tailscale Personal plan limits](https://tailscale.com/docs/account/manage-plans/free-plans-discounts)
- [Tailscale ACL docs](https://tailscale.com/docs/features/access-control/acls)
- [tsl0922/ttyd releases (1.7.7 = March 2024, last)](https://github.com/tsl0922/ttyd/releases)
- [CVE-2025-30066 (tj-actions/changed-files)](https://github.com/advisories/ghsa-mrrh-fwg8-r2c3)
- [Wiz — Hardening GitHub Actions (SHA pinning)](https://www.wiz.io/blog/github-actions-security-guide)

### Unity Personal on Linux VM — canonical 2026 path (2026-05-25)

After the "Personal can't run in containers" discovery above, owner suggested: "treat the Linux VM as a personal machine I have." Follow-up research confirmed this is the cleanest working path in 2026; no better alternative exists.

**Research verdict** (cross-checked against Unity docs 6000.3/6000.4, GameCI 2024-2026, Unity Discussions 2023-2026):
- No CLI activation path for Personal (Unity docs explicit: "command-line procedures don't apply to Unity Personal").
- No remote activation (Hub on machine A can't license machine B).
- No mature community installer; the Hub-via-temporary-GUI dance is what indies actually do.
- Two refinements over a naive VNC approach: **use xrdp** (built-in Windows RDP client, smoother than any VNC client), and **rely on Hetzner's implicit MAC stability** (MAC is bound to the server resource ID for its lifetime — survives reboot, stop/start, host migration, `hcloud server rebuild`). Earlier draft of this entry said "pin the MAC at provider level" — that's wrong; no such feature exists in Hetzner Cloud Console. See 2026-05-25 risk-register sweep entry above for the corrected approach.

**Workflow** (codified in plan-doc A4.1 + A5):
1. Provision Hetzner CCX23 (160 GB NVMe), Ubuntu 22.04. **Record the server resource ID + primary MAC** in `infra/vm-host.txt` — MAC is implicitly stable for the life of the server resource (no config needed; just don't delete the server). Enable billing alerts at the Hetzner account level.
2. Install `xfce4 + xrdp` (TEMPORARY) with the hardening from A5.2 (`adduser xrdp ssl-cert`, `~/.xsession`, TLS pin), open port 3389 on `tailscale0` only.
3. RDP from Windows laptop (built-in `mstsc.exe`) over Tailscale. Pick **Xorg** at login (not Xvnc). Don't be logged in locally.
4. **Install Unity Hub via apt repo** (`https://hub.unity3d.com/linux/repos/deb stable main`), sign in to Unity account — **Personal auto-activates on signin** (no separate "Add license" click; the old button was removed). Hub writes `UnityEntitlementLicense.xml` to `~/.config/unity3d/Unity/licenses/`, starts `Unity.Licensing.Client` daemon, license binds to this VM's MAC + machine-id + hostname.
5. Install Editor 6000.3.11f1 via Hub (~5 GB).
6. **Take a Hetzner snapshot now** — this is the A7 disaster-recovery restore point. Recover with `hcloud server rebuild --image <snap-id> <server-id>`.
7. `apt purge xfce4* xrdp` + close port 3389. VM is back to headless. **Future Editor versions install headlessly** via `unityhub -- --headless install --version <X.Y.Zf1> --changeset <hash>` — no need to re-spin xrdp.
8. From here on: `~/Unity/Hub/Editor/<version>/Editor/Unity -batchmode -quit -nographics -projectPath <path> -desiredWorkerCount 0 -logFile -` works headlessly. LicensingClient daemon is auto-started by Editor on each invocation.

**Constraints to live with**:
- **2-seat Personal limit** per Unity ID (laptop + VM = 2/2). Before destroying/re-provisioning the VM, **MUST "Return License" via Hub** or the seat is gone until Unity Support manually frees it. Document in runbook (A7).
- **MAC must stay stable.** Hetzner's MAC is implicitly bound to the server resource ID; survives reboot, stop/start, host migration. Only deletion of the server resource itself changes MAC. **Protect the server resource** with billing alerts + account safeguards (A4.1).
- **Snapshot restore semantics** (2026-05-25 corrected):
  - **In-place rebuild**: `hcloud server rebuild --image <snap-id> <server-id>` → server-ID + MAC preserved → license still valid. Fast disaster recovery (<30 min). This is the A7 default.
  - **Create new server from snapshot** → new server-ID → new MAC → license invalid → must re-activate (burns a seat). Use only if the server resource is unrecoverable.
- **Editor version upgrades**: install additional versions via Hub (one-time RDP-back-in: re-install xfce+xrdp briefly, install new Editor, tear down). Or `~/Unity/Hub/UnityHub.AppImage --headless install --version <new>` if it works in 2026 (untested, would simplify).

**Trade-offs vs the original GameCI-container plan**:
- Lose: image-based version pinning, container isolation, one-line install.
- Gain: free Personal licensing that actually works; no Puppeteer scripts; no $2,200/yr Pro; Unity-blessed activation path.

**Sources** (load-bearing):
- [Unity Manual — Manage license through CLI (6000.4)](https://docs.unity3d.com/6000.4/Documentation/Manual/ManagingYourUnityLicense.html) — confirms Personal has no CLI activation.
- [Unity Hub install on Linux](https://docs.unity.com/en-us/hub/install-hub-linux)
- [Unity Discussions — License Activation Issue on Linux Server Headless (Feb 2025)](https://discussions.unity.com/t/unity-license-activation-issue-on-linux-server-headless-mode/1603609)
- [Silicon Orchid — Unity on Hyper-V VM (MAC fingerprint issue)](https://blogs.siliconorchid.com/post/coding-inspiration/unity-on-hyperv/)
- [Hetzner Cloud docs — primary NIC MAC pinning](https://docs.hetzner.cloud/) (configurable via Cloud Console)

### Local-validate-then-VM-deploy: partially abandoned (revised 2026-05-21)
- Original premise: validate every primitive on the user's Windows machine before paying for the VM, then migrate via `rsync + systemd units + tailscale up`.
- Reality after A2 reshape: **only A0, A1, and W1-W3 (Tailscale) validated on Windows.** The chat-layer + browser-terminal piece moved to the VM after two Windows tool attempts failed (see "Windows-local browser-terminal validation: abandoned" above).
- New ordering: validate Linux primitives on the VM under free-tier credit ($0) before any paid spend. GCP $300 / 90-day credit is the recommended free-tier provider (matches paid-Hetzner x86 architecture 1:1; full Unity validation possible).
- What we keep: A0 (subscription gate) and A1 (worktree + concurrent batchmode) still ran on the laptop. Both produced gotchas that carry forward to the VM regardless of OS. The exercise wasn't wasted.
