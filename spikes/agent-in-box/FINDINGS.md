# Findings — a real `claude` turn inside a Linux container

Partial run, captured on a Docker daemon brought up **inside the remote sandbox**
(root + `cap_sys_admin` + writable cgroups; `dockerd` 29.3.1, overlayfs). Evidence:
[`results/`](results). Claim + acceptance: [`PLAN.md`](PLAN.md).

## Stage A — container mechanics: **all green** (zero model cost)

Log: [`results/stage-a-mechanics.log`](results/stage-a-mechanics.log).

| # | Mechanic | Result |
|---|---|---|
| M1 | bind-mount round-trip | sha256 **identical** host ↔ container |
| M2 | hold-open across N execs | 5 execs over one box; step 1's write visible in step 5 |
| M3 | `isatty` under an in-box pty | **`isatty(0)=True isatty(1)=True`** (`pty.fork`) |
| M4 | sh `stop-hook` over the mount | Stop payload written in-box, read back on host |
| M5 | guaranteed teardown | container gone after `docker kill` |
| M6 | sh `perm-hook` handshake | `req` → host `resp` → hook echoes it, across the mount |

This validates the **sh-shim ports** and the **mount + pty plumbing** the harness
rests on. Notable: **M2 means we don't strictly need Testcontainers** — plain
`docker exec` holds a box open across N execs cleanly. The library's case is now weak;
lead with `Docker.DotNet` (or the CLI) when wiring the real seam.

## Stage B — a real `claude` turn: **blocked by this environment's auth, not the design**

Log: [`results/stage-b-auth-probe.log`](results/stage-b-auth-probe.log).

- **B1 (claude runs in a Linux container): partial** — the CLI executes in-box
  (v2.1.185); a turn is blocked only at auth.
- **B2 (subscription billing path): cannot validate here.** This is a *managed remote
  session* — `claude` authenticates via an inherited file descriptor
  (`CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR=4`) + a managed proxy (`ANTHROPIC_BASE_URL`).
  fd 4 does **not** cross into a separate container (only `0–3` present), so the turn
  fails with `Authentication error`. No `ANTHROPIC_API_KEY`/`CLAUDE_API_KEY` is set, so
  it is **not** the API-billing path — there is simply no portable credential to carry in.

On a real Docker host where a dev has run `claude login`, the OAuth credential is a
mountable file (`~/.claude/.credentials.json`) and the in-box `claude` would
authenticate. **B1/B2 must be run there**, not in this managed sandbox.

## Design finding — credential delivery (new, load-bearing)

The box must hold the **agent's own model credential** — unlike git/repo creds, which
stay on the host (`SPIKE.md` A1/A4). That's an unavoidable asymmetry: the agent runs in
the box, so its *model* auth lives in the box, while its *git* auth never does.

This environment demonstrates the clean way to do it: **broker the auth** — a proxy
(`ANTHROPIC_BASE_URL`) + an **ephemeral** token, so the box never holds a reusable key.
The real product should deliver claude's subscription auth the same way (short-lived,
brokered), not by mounting a long-lived credential file.

## What's proven vs. still open

| | Status |
|---|---|
| Container seam mechanics (mount, hold-open, teardown, pty/isatty, sh hooks over mount) | ✅ green (Stage A) |
| `claude` executes in a Linux container | ✅ (Stage B, runs) |
| Turn **mechanism** (Stop hook + JSONL read-back) vs real claude | ✅ validated via `-p` (below) |
| **Standalone auth + billing path** (no managed session) | ⏳ **NOT proven here** — this env's auth is managed host-state, not portable (below) |
| Credential delivery for the in-box agent | ✅ path validated (below) — building it |

## Auth path — validated (4-agent review)

The proposed auth — replicate how Claude Code's own remote env authenticates — was
reviewed from four independent angles (mechanism, security, architecture, billing/ToS).
**All four: viable.** Confirmed against the shipped claude binary's auth-source enum:
`CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`, and `apiKeyHelper`
are first-class inputs; fd-injection is Anthropic-dogfooded (`"injected by the CCR host"`).

**Chosen mechanism (least-mechanism that's still leak-safe):**
- Source: owner's `claude setup-token` (1-yr subscription OAuth token), host-held, never
  persisted in the box. `ANTHROPIC_API_KEY` scrub stays — it selects subscription billing.
- Host → box: token via a **tmpfs file** (not a box-wide env var — that leaks into every
  gated-tool hook child; not a raw fd — fds don't cross the docker boundary).
- Box-internal: the driver **fd-injects** to its `claude` child
  (`CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`), keeping the token out of `environ`.
- `apiKeyHelper` deferred — only needed for mid-turn rotation; per-turn re-injection
  already absorbs rotation at turn boundaries.

**Three conditions (none optional):**
1. **Egress is the security boundary** — ✅ **now validated** (allowlist form, below).
2. **Single subscription owner only** — serving other users off one token is ToS resale.
3. **Rate-limit backoff** — 5-hr window + weekly caps, 429s, no API fallback.

## Egress — validated, allowlist form (condition 1)

Run on docker-in-the-sandbox. Log: [`results/egress.log`](results/egress.log). Pure
docker, no in-container iptables: the box sits on an `--internal` network (no route
out); its only egress is a host-controlled CONNECT proxy permitting **only**
`api.anthropic.com`. Ports `SPIKE.md` A3/A5 into the box. **All green:**

| # | Attempt | Required | Result |
|---|---|---|---|
| E1 | direct → cloud-metadata `169.254.169.254` | blocked | ✅ no route |
| E2 | direct → RFC1918 `10.255.255.1` | blocked | ✅ no route |
| E3 | direct → public `1.1.1.1` (bypass proxy) | blocked | ✅ no route |
| E4 | via proxy → `api.anthropic.com:443` | allowed | ✅ 200 |
| E5 | via proxy → `example.com:443` (not allowlisted) | blocked | ✅ 403 |

This is the posture that makes the fd-injected in-box token safe: there is **no exfil
channel** — the only reachable destination is Anthropic. Notes/limits:
- This is the **allowlist** form (strongest). The **denylist** default (block only
  host/RFC1918/metadata, allow the public internet) needs host-level packet filtering —
  not done here; a real-host track.
- The proxy allowlists the **exact** CONNECT host and resolves it itself (client can't
  DNS-rebind it). For full rigor, pin by resolved IP / endpoint, not just hostname.
- Real product: the proxy is a **host sidecar**; the box's `HTTPS_PROXY` points at it.
  This is also the natural place the auth broker could inject Anthropic credentials.

## Real turn — validated via `-p` (+ pty-drive findings)

A fresh isolated container can't auth (token not forwardable), but this session's
claude is authenticated, so we validated a real turn as a host subprocess (container
mechanics are proven separately in Stage A). Log: [`results/rung0-p-turn.log`](results/rung0-p-turn.log).

- ✅ `claude -p "…" --settings <stop-hook>` (no API key → **subscription**) completed a
  real turn; the **Stop hook fired** and wrote the read-back payload with
  `last_assistant_message` (exactly what `ClaudeProvider.ReadFinalMessage` parses) +
  `transcript_path`; JSONL written. The core in-box mechanism, proven against real claude.

Driving the interactive **pty TUI** (`drive-turn.py`) surfaced real Linux findings:
1. `isatty(pty)=True` under `pty.fork` — billing precondition holds.
2. The pty needs an explicit **window size** (`TIOCSWINSZ` 120×40) or the TUI never
   draws its input bar. Fixed in `drive-turn.py`.
3. **`bypassPermissions` is refused as root** ("cannot be used with root/sudo") → the
   agent must run **non-root** in the box.
4. A **fresh config runs onboarding** — theme dialog, then a login-method dialog whose
   OAuth flow can't complete headlessly → the box must ship a **pre-onboarded,
   pre-authenticated** config (what the managed env does), or use `-p`.

**Strategic (for the owner — do not overturn the oracle unilaterally):** `-p` with no
API key billed subscription, fired the Stop hook, and wrote JSONL with **no pty TUI
driving and no onboarding**. If that billing behavior holds, the in-box driver could
collapse from "port the ConPTY choreography" to `claude -p --settings <hooks>`. Tier-A
oracle A1/A2 locked the pty path on an older claude; this warrants a fresh explicit
decision against `design/behavioral-oracle.md` before adopting.

### Correction — the `-p` auth was environment-contaminated (owner's challenge)

Follow-up probing (log: [`results/auth-source-probe.log`](results/auth-source-probe.log))
showed the `-p` turn above authenticated from **this managed environment's host-state**,
not a portable credential:
- Stripping the entire chat env (`env -i`) still authed on the host → the chat **env vars**
  are not the source.
- But the same credential copied into a **clean container** gave `Not logged in` → auth
  lives in host-specific **managed-session state** (`~/.claude/session-env`, `sessions`),
  not a portable file (no `.credentials.json`, no keyring; `.claude.json` holds only
  `oauthAccount` metadata).

**Therefore:** the `-p` run validates the **mechanism** (Stop hook + JSONL) only. It does
**not** prove A.Box-standalone auth, and does **not** de-risk the "`-p` replaces the pty"
idea — that stays open. Standalone auth must come from a **portable** credential the owner
creates (`claude setup-token` → `CLAUDE_CODE_OAUTH_TOKEN`, or `claude login` →
`~/.claude/.credentials.json`), delivered into the box (the broker design, unchanged).
This managed sandbox cannot stand in for that test — B1/B2 (standalone auth + billing)
must run on a `setup-token`/`claude login` host.
