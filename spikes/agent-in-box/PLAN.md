# Spike: a real `claude` turn inside a Linux container

Status: **spike plan + starter harness, not yet run.** Cold-readable, standalone.
The executable counterpart must run on a **Docker host with `claude` installed and
logged in** — this is the validation [`PLANS/agent-controls/sandbox.md`](../../PLANS/agent-controls/sandbox.md)
calls Step 1, and the high-risk unknown #81 skipped.

## Why this spike

`spikes/unity-container` (#81) proved the **Unity/compile half** — but its "agent" was
a stand-in (`docker run alpine sh run-turn.sh`, a script that *writes a file*). It
never ran a real `claude` turn. So the **agent-runtime half is unvalidated**, and the
billing path is load-bearing. This spike runs the real CLI in a Linux box and answers:

> Does a real `claude` turn run inside a Linux container — keeping **subscription
> billing** (`isatty()` true under the in-box pty, not the API-key path), driving the
> **TUI choreography** under a Linux pty (≠ Windows ConPTY), with **hooks + JSONL
> crossing the mounted session dir**?

## What it ports (and the deltas to confirm)

The harness here is a faithful Linux translation of the host implementation:

| Host today (`src/Domain/Agents/Claude/`) | In the box (this spike) | Delta to confirm |
|---|---|---|
| Windows ConPTY via `Porta.Pty` | Linux pty (`in-box/drive-turn.py`) | TUI markers/keys render the same under a Linux pty |
| launched through `cmd.exe` | `claude` launched directly | no cmd.exe; flags per `ClaudeProtocol.BuildArgs` |
| `pwsh` Stop/Perm hook shims (`ClaudeHooks`) | **sh** shims (`in-box/*-hook.sh`) | sh ports behave identically |
| `RA_STOP_SIGNAL` / `RA_PERM_DIR` in host temp | both under the **mounted session dir** | mount carries the req/resp + signal round-trip |
| JSONL in `~/.claude/projects/` on host | `HOME` set to the mounted session dir | `claude` writes JSONL where we can read it off the mount |
| subscription-key scrub (`EnvScrub`) | same keys scrubbed before launch | scrub doesn't break the subscription path |

## Acceptance — what "green" means

| # | Claim | Pass condition |
|---|---|---|
| B1 | A **real** `claude` turn runs in a Linux container over the mounted project | the CLI completes a turn in-box and the edit is live on the host mount |
| B2 | **Subscription billing holds** | `isatty()` true under the in-box pty (Oracle A1/A2); the turn is **not** on the API-key path (keys scrubbed, session is a subscription session) |
| B3 | The **TUI choreography** works under a Linux pty | trust / bypass dialogs dismissed, input-bar marker detected, prompt submitted, **Stop hook fires** |
| B4 | The **permission loop crosses the mount** | a gated tool drops `req-*.json` on the mounted perm dir; the host writes `resp-*.json`; the tool proceeds |
| B5 | **JSONL reads back on the host** | the per-session transcript written in-box is byte-faithful when read off the mount |
| B6 | **Guaranteed teardown** | the container dies on dispose even mid-turn (anti-zombie, Oracle A10) |

**Pass:** every row demonstrated and captured in `results/` — not asserted.

## Rungs (each ≈ a day)

### Rung 0 — real turn, bypass mode (B1, B2, B3, B5)
`docker run` the box over a mounted throwaway project; `in-box/drive-turn.py` launches
`claude` under a Linux pty in `bypassPermissions`, dismisses startup dialogs, submits a
one-line prompt, waits for the Stop signal. Assert: `isatty()` true, final message +
JSONL readable on the host mount. The cheapest proof of the billing + pty unknown.

### Rung 1 — gated permission over the mount (B4)
Re-run in `default` mode with the sh `PreToolUse` shim; the prompt asks for a `Bash`
tool use. Assert `req-*.json` appears on the **mounted** perm dir; a host script writes
`resp-*.json`; the tool proceeds. Proves the mid-turn handshake survives the bind mount.

### Rung 2 — teardown mid-turn (B6)
Start a turn, kill the container while it runs; assert no orphaned `claude`/pty process
and the mount is intact.

### Rung 3 — container library + timing (byproduct, not the point)
Wrap Rung 0 behind the `ISandbox` seam over **Testcontainers.NET**; prove the
hold-open-across-N-`ExecAsync` runtime pattern and emit a cold/warm/amortised timing
table. **If Testcontainers' test-harness ergonomics fight the hot path, fall back to
`Docker.DotNet`** — this rung is where that call gets made, not before.

## Layout

```
agent-in-box/
  image/Dockerfile        .NET SDK + claude CLI + python (pty) + the sh hook shims
  in-box/drive-turn.py    Linux pty driver — faithful port of ClaudeProvider choreography
  in-box/stop-hook.sh     sh port of ClaudeHooks' Stop shim (writes RA_STOP_SIGNAL)
  in-box/perm-hook.sh     sh port of the PreToolUse shim (req → poll resp → timeout-deny)
  host/run-spike.sh       docker run with worktree + session mounts; capture + read-back
  results/                captured logs (the evidence — populated on a Docker host)
  PLAN.md                 this file
  README.md               prerequisites + how to run each rung
```

## Out of scope

Egress policy (independent track), the DLL bake/EULA question, and the full A.Box
Flow/Step wiring. This spike validates **one** thing: a real `claude` turn survives a
Linux container with billing + pty + hooks/JSONL over the mount.

## Definition of done

- B1–B6 observed with captured `results/`, on a Docker+`claude` host.
- A one-paragraph verdict: **does the agent runtime survive the box** — with the
  confirmed Linux-port deltas (sh shims, Linux pty, `HOME`/session-dir location) and
  whether subscription billing held. The rows lift into A.Box as the agent-runtime
  acceptance criteria, joining #81's compile criteria and `SPIKE.md`'s separation matrix.
