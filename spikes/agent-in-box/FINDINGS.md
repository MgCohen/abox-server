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
| A real subscription turn completes in-box + its billing path | ⏳ run on a `claude login`'d Docker host |
| Credential delivery for the in-box agent | 🔎 finding: broker it (proxy + ephemeral token) |
