# Spike: a real `claude` turn inside a Linux container

Claim + acceptance: [`PLAN.md`](PLAN.md). This is the **starter harness** — it must
run on a **Docker host with the `claude` CLI installed and logged in** (this is *not*
runnable in the CI/remote sandbox, which has no Docker daemon).

## What it proves

The unknown #81 skipped: a real `claude` turn survives a Linux container with
**subscription billing** (`isatty()` under an in-box pty), the **TUI choreography**
under a Linux pty, and **hooks + JSONL crossing the mounted session dir**.

## Prerequisites

- Docker (`docker info` shows `OSType=linux`).
- A `claude` install method baked into [`image/Dockerfile`](image/Dockerfile) — left
  as an explicit step so the spike records exactly how the box gets the CLI. Confirm
  the current channel on your host.
- A logged-in subscription credential reachable by the in-box `claude` (the credential
  delivery into the box is the thing B2 scrutinizes — start by mounting the host's
  `~/.claude` auth read-only and confirm the session is a *subscription* session).
- A throwaway project dir to mount (any folder with a file to edit).

## Run

**Rung 0 — real turn, bypass mode (B1, B2, B3, B5)**
```sh
host/run-spike.sh /path/to/throwaway-project "Add a line to NOTES.md and stop." bypass
```
Watch for `[drive] isatty(pty)=True` (B2) and `[drive] Stop hook fired` (B3), then the
read-back section: the final message (`stop-signal.json`) and the JSONL under the
mounted `HOME/.claude/projects` (B5). Logs land in `results/rung0.log`.

**Rung 1 — gated permission over the mount (B4)**
Run with `default` mode and a prompt that triggers a `Bash`/`Write` tool. While it
runs, a host watcher writes the approval:
```sh
host/run-spike.sh /path/to/throwaway-project "Run 'date' with the Bash tool, then stop." default &
# in another shell, approve the request that appears on the mounted perm dir:
#   echo '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow"}}' \
#     > <session>/perm/resp-<id>.json
```

**Rung 2 — teardown mid-turn (B6)**: start a turn, `docker kill` the container, assert
no orphaned process and the mount is intact.

## Files

| File | Role |
|---|---|
| `in-box/drive-turn.py` | Linux pty driver — faithful port of `ClaudeProvider`/`ClaudeProtocol` |
| `in-box/stop-hook.sh` | sh port of the Stop shim → writes `RA_STOP_SIGNAL` on the mount |
| `in-box/perm-hook.sh` | sh port of the PreToolUse shim → `req`→poll `resp`→timeout-deny |
| `image/Dockerfile` | .NET SDK + python + claude; no Unity install |
| `host/run-spike.sh` | open box with mounts, run one turn, read back off the mount |
| `results/` | captured evidence (populated on a Docker host) |

## Note on faithfulness

The pty markers/keys in `drive-turn.py` are lifted verbatim from
`src/Domain/Agents/Claude/ClaudeProtocol.cs`. If the real CLI renders differently under
a Linux pty, tuning them **is** the spike's finding — capture the delta in `results/`.
