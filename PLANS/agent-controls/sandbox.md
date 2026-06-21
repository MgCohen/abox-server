# Sandbox — the simple approach (canonical)

Status: **the build target.** Supersedes the multi-OS / provisioner / warm-start
design in [`substrate-abstraction.md`](substrate-abstraction.md) and the complex
seam in [`flow-sandbox.SPIKE.md`](flow-sandbox.SPIKE.md). The **security model** in
[`SPIKE.md`](SPIKE.md) is unchanged and carried forward whole.

Validated by the **`spikes/unity-container`** spike (PR #81 — *VIABLE*). This doc is
the design that spike proved out, collapsed to its least mechanism.

## The model (spike #81)

The agent's box is **just a Linux Docker container**: .NET SDK + Unity's *reference
DLLs*. Unity itself — install, license, editor, player builds, edit/play-mode tests —
**only ever runs on the host**, over the same bind-mounted project. The container
compile-checks C# with `dotnet build`; the host closes the loop with real Unity.

```
agent edits C# in container ──► dotnet compile-check in container (no Unity)
        │  live on host (mount)         │
        ▼                               ▼
   shared mount ◄───────────────► Unity on host: build + EditMode tests
```

## The seam

One interface, one opener. No provisioner indirection, no OS target, no router, no
`tty` flag.

```csharp
public interface ISandbox : IAsyncDisposable        // close = Dispose, guaranteed teardown
{
    Task<ExecResult> ExecAsync(string command, Action<string>? onOutput, CancellationToken ct);
}

public sealed record ExecResult(int ExitCode, string Stdout, string Stderr);

public static class DockerSandbox
{
    public static Task<ISandbox> OpenAsync(SandboxOptions options, CancellationToken ct);
}

public sealed record SandboxOptions(
    DirectoryInfo Worktree,      // mounted in — the live files
    DirectoryInfo SessionDir,    // mounted in — transcripts/hooks read off the host
    EgressPolicy  Egress,        // denylist default / allowlist variant (SPIKE.md)
    string        Image);        // a docker image tag
```

Three verbs: **open · exec · close.** `ISandbox` is an interface only so the
orchestrator's tests can fake it; opening is a plain concrete `DockerSandbox.OpenAsync`
until a second substrate actually exists.

## What runs where

The dividing line is **executes code vs holds credentials** — the two never overlap.

| In the box (via `ExecAsync`) | On the host (trusted process) |
|---|---|
| agent turns | flow orchestration / step sequencing |
| build / compile | git — branch/commit/push/merge (holds the token) |
| tests that run in-container | reading results back off the mount |
| anything that runs untrusted code | clone → stage in (no creds ever cross) |

The agent turn's PTY choreography (the subscription-billing `isatty` detail) lives
**inside the in-box agent driver**, never on the seam — if Claude's billing rules
change, the box doesn't. The mid-turn permission loop is **files on the mounted
session dir**: the in-box driver writes a request, the host resolver reads it off the
mount and writes the response back. Box → host has no callback by design.

## What the box needs

| Need | What it is | Cost |
|---|---|---|
| .NET SDK (Linux) | the base image | ~200MB, pulled once, cached |
| Unity engine reference DLLs | `UnityEngine.*Module.dll` + `UnityEditor.dll` — managed assemblies, **not** an install | ~100s of MB; **mount** for dev / **bake** for prod (EULA caveat — PLAN assumption 1) |
| the worktree mount | carries the host-primed `Library/ScriptAssemblies` + `PackageCache` | ~0 (mount, no copy) |

No Unity install in the box. No editor, no license, no `Library/` import in the box.

## Spin-up

Opening a box = image-availability + container boot; **the mount is free** (no copy).
With the image cached, expect **~1–2s cold, sub-second warm** — *expected, not yet
measured* (see Next steps). It's paid **once per flow**, amortized across every turn /
build / test, so per-operation overhead rounds to zero.

Because nothing heavy happens in the box (no Unity install, no `Library/` import),
there is **no warm-pool and no snapshot** to engineer, and **no managed-service
pressure** (Fly / e2b / Daytona). A plain .NET container is cheap enough on its own.

## Security (unchanged — see [`SPIKE.md`](SPIKE.md))

Threat model: guard against a *wandering long-running agent grabbing keys*, **not** a
malicious VM escape. On that model a container is enough:

- **No credential ever enters the box** — the host holds the token and does all git.
- **Egress** — denylist default (block host gateway, RFC1918, `169.254.169.254`
  metadata; public internet open) or allowlist variant (Anthropic only).
- **File-only seam** — the box is born with the worktree + session dir mounted; the
  host reads everything back off those mounts. No file API on `ISandbox`.
- **Guaranteed teardown** — `DisposeAsync` kills the box even on a hung turn
  (Testcontainers' Ryuk reaper would provide it; the container library is still
  provisional — see Open unknowns).

## Deliberately dropped (YAGNI for v1)

| Dropped | Why |
|---|---|
| `IProvisioner` polymorphism, `SandboxTarget`, fleet router | one OS (Linux), one host — no routing to do |
| `ImageRef` opacity | it's just a docker image tag now |
| `tty` flag on the seam | a Claude-billing detail; lives in the in-box driver |
| warm-pool, snapshot/fork (layers 2–3) | nothing heavy in the box → no slow start to hide |
| Unity-in-the-box (game-ci image, `Library/` import) | Unity stays on the host; the box shares the DLLs |
| microVM + `git bundle` seam | bind mount is enough under the threat model |
| managed SaaS (Fly / e2b / Daytona) | self-hosted Linux + cheap start — no buy trigger |

Each returns only when a *second real use* forces it (e.g. an iOS build needs a
macOS VM → then, and only then, a target + router).

## Open unknowns — what's still unvalidated

Spike #81 proved the **Unity/compile half** thoroughly, but its "agent" was a
stand-in (`docker run alpine sh run-turn.sh` — a script that *writes a file*). It
never ran a real `claude` turn. So the **agent-runtime half is untouched**, and that
is where the risk now lives — not in the container plumbing.

| Unknown | Risk | #81 |
|---|---|---|
| Real `claude` turn in-box: **subscription billing** (`isatty()` under the in-box pty, not API billing) + the TUI choreography under a **Linux** pty (≠ Windows ConPTY) | **high** | stand-in only |
| Permission handshake over the mount — bidirectional file-visibility latency for the mid-turn request/response | med | — |
| Session dir / JSONL — does `claude` write where we mount it, readable on the host? | med | — |
| Container library — does **Testcontainers** (a *test-harness* lib) hold a box open across N execs as a **runtime** pattern, with sane teardown? Else `Docker.DotNet` | med | — |
| Egress in real form (denylist/allowlist, not `--network none`) | med | — |
| DLL **bake** path (Linux host, no Unity) + the **EULA** question | low (eng) / blocker (legal) | #81 *mounted* them |
| Spin-up timing (cold / warm / amortised) | low | — |

> **Credential delivery (finding from `spikes/agent-in-box`):** the box must hold the
> **agent's own model credential** — unlike git/repo creds, which never enter the box
> (`SPIKE.md` A1/A4). The agent runs in the box, so its *model* auth lives there. Deliver
> it **brokered** — a proxy + an **ephemeral** token (the way Claude Code's own managed
> remote sessions do it) — so the box never holds a reusable key. Mounting a long-lived
> credential file is the thing to avoid.

## Next steps (build order)

1. **Spike the real agent turn in a box** — `docker run` one *real* `claude` turn over
   a mounted project. Confirm **subscription billing holds** (`isatty()` true under the
   in-box pty), the turn drives cleanly under a Linux pty, and hooks/JSONL are written
   to the mounted session dir and **read back on the host**. This is the load-bearing
   unknown #81 skipped — prove it before anything else. The container-library choice
   (**Testcontainers vs `Docker.DotNet`**, the hold-open-across-N-`ExecAsync` pattern)
   and the cold/warm/amortised **timing table** fall out of this rung as byproducts.
   *Testcontainers is the first thing to try, not a settled choice — drop to
   `Docker.DotNet` if its test-harness ergonomics fight the runtime hot path.*
2. **In-box agent driver** — extract the PTY drive choreography from
   `src/Domain/Agents/Claude/ClaudeProvider.cs` into a headless in-box program that
   spawns `claude` under a Linux pty and writes hooks/JSONL to the mounted session dir.
3. **Host-side turn over the mount** — replace `DriveAsync`'s in-process pty+pump with
   `ExecAsync(driver)` + concurrent permission resolution off the mounted session dir.
4. **Egress + flow lifecycle** — denylist/allowlist on `OpenAsync`; provision at flow
   start, `DisposeAsync` in `finally`; route turns + build/test through `ExecAsync`;
   git stays on host.

Independent tracks (not blocking 1–3): **egress** in its real form, and the **DLL
bake + EULA** question.
