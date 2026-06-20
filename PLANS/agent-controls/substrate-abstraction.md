# Design note: sandbox substrate abstraction (provider layer)

Status: **design note**, not built. Cold-readable. **Stacks with** the two spikes
in this folder — it does not replace them:

- [`SPIKE.md`](SPIKE.md) — proves the *security boundary* (env scrub, egress, file
  seam) for one agent invocation.
- [`flow-sandbox.SPIKE.md`](flow-sandbox.SPIKE.md) — proves the *per-flow
  lifecycle, spin-up cost, and file in/out seam*.
- **This doc** — the *abstraction layer* the orchestrator binds to, so the
  substrate (Docker container / Windows container / Mac VM / managed microVM) is a
  swappable detail behind one seam, plus the **persistence** and **warm-start**
  models that seam has to support.

Read order: security boundary → lifecycle → *this* (how the orchestrator names and
swaps substrates).

---

## 1. The seam

The orchestrator's flow logic — clone → stage in → run agent turns → read out →
commit — is **substrate-agnostic**. Only the thing that *creates and runs the box*
differs per platform. Funnel that through one narrow seam:

```csharp
public interface IProvisioner
{
    Task<ISandbox> ProvisionAsync(SandboxSpec spec, CancellationToken ct);
}

public sealed record SandboxSpec(
    SandboxTarget Target,        // Linux | Windows | MacOS
    string Image,                // container image, or VM template id
    DirectoryInfo Worktree,      // staged in at provision time
    EgressPolicy Egress);        // Anthropic-only, etc.

public interface ISandbox : IAsyncDisposable
{
    Task<ExecResult> ExecAsync(string command, CancellationToken ct);
    Task<string>     ReadFileAsync(string path, CancellationToken ct);
    Task             ExportAsync(DirectoryInfo dest, CancellationToken ct);
}
```

Design rules baked into the shape:

- **`ISandbox`, not `IContainer`.** The seam's whole job is to hide *whether the
  box is a container or a VM*. Naming it `IContainer` re-leaks that — and for
  Mac/Windows it isn't a container. Name it for what it is to the orchestrator: an
  isolated box.
- **Files go in at provision time**, not via a method. The box is *born* with the
  worktree staged (mount for Docker, copy for a VM). `ISandbox`'s operations are the
  runtime ones: exec, read, export, dispose.
- **`DisposeAsync` is guaranteed anti-zombie teardown**, not best-effort — the box
  dies even if a run hung or threw (same teardown discipline the PTY path already
  honors). Orchestrator wraps the lifecycle in `finally`.
- **Async + actionable failure.** Provision can fail (no Mac node free), exec can
  fail — throw errors that say what to do, never swallow.

## 2. Persistence — long-running, not single-command

A flow runs an **unknown** number of agent turns over **one shared filesystem**.
The box must be **semi-persistent**: provision once → exec N times → close. This is
the standard "long-running sandbox" model, not the naive `run <cmd>`-and-exit one:

```
ProvisionAsync(spec)  ──▶  ExecAsync × N  (over the life of the flow)  ──▶  DisposeAsync
```

The handle holds the box open for the flow's life; the worktree and the agent's
session files persist across turns for free. This is already what the interface in
§1 encodes — one provision, many execs, one dispose.

## 3. Providers — one per substrate, container *vs* VM is forced by OS

A container shares the host kernel, so the box's OS is dictated by what can share
that kernel. This is not a preference — it's a hard constraint:

| Target | Substrate | Provider backend | Note |
|---|---|---|---|
| Linux | container | Docker / Podman on a Linux host | the v1 path; cheapest |
| Windows | container **or** VM | Docker (Windows host) / a Windows VM | Windows-host-only; big images |
| macOS | **VM only** | `tart` / Anka on Apple hardware | **no macOS container exists** — Apple licensing + kernel. iOS/mac builds force this. |

Each is a separate `IProvisioner` implementation behind the same seam. The
orchestrator picks one by `SandboxSpec.Target`, which is set by **what the flow
needs to produce** (Android/WebGL/Linux → Linux container; Windows → Windows;
iOS/macOS → Mac VM). Substrate is matched to the build target, not chosen once
globally.

## 4. The fleet router (the part that actually grows)

"Use a Mac to spawn a Mac VM" implies a **pool of host nodes** — a Linux host, a Mac
mini, a Windows box. Something has to answer *"which available node can serve
`Target=MacOS`?"* and dispatch. For v1 (Linux only, one host) this is trivial:
localhost Docker. The router is the piece that grows when Mac/Windows arrive — more
than the providers themselves. Name it now so it isn't a surprise; build it when the
second host type lands.

## 5. Warm-start — three layers (critical for Unity)

Installing Unity + modules and building Unity's `Library/` import cache is slow
(multi-GB editor; first project import is minutes). Don't pay it per flow. Three
stackable layers, each killing a different cost:

| Layer | Kills | Mechanism | Name |
|---|---|---|---|
| 1. Bake deps into the image | Unity-install time | custom image `FROM` a Unity base (`game-ci` publishes these), built once, pushed to a registry | golden image / template |
| 2. Keep N hot boxes started | box-boot + repo-clone time | a pool manager holds started, repo-cloned, idle boxes; hand one out, replenish in background | warm pool |
| 3. Snapshot a fully-initialized box | **everything**, incl. `Library/` | memory/disk snapshot taken *after* first import; fork in ms | snapshot / fork |

Unity wrinkle: layer 1 fixes the editor install, but `Library/` is **per-project**,
so it needs layer 2 or 3 (a pool with the repo pre-imported, or a post-import
snapshot). Layer 1 we always do (easy). Layer 2 is the pragmatic self-hosted
default (a pool manager you own; `tart clone` pools Mac VMs the same way). Layer 3
(snapshot-restore of a warm box) is genuinely hard to self-host — it's the strongest
argument *for* a managed service, when warm-start latency is critical.

## 6. Build vs buy

The substrate **mechanics** are solved; the **policy** is ours. Don't hand-roll the
Docker API.

**v1 — self-hosted, .NET, Linux: wrap [Testcontainers.NET].** It maps 1:1 onto
`ISandbox`: `WithResourceMapping`/`WithBindMount` (files in), `ExecAsync` (exec),
file copy-out (export), `WithNetwork` (egress), and the **Ryuk resource reaper**
gives guaranteed teardown for free — i.e. §1's anti-zombie rule, solved. Make
`DockerProvisioner` a thin adapter over it, not raw `docker` calls. (`Docker.DotNet`
is the lower-level fallback if its test ergonomics chafe.)

**Extension menu (only when a real need lands):**

| Option | Gives | Costs you |
|---|---|---|
| **e2b** | Firecracker microVMs, ~150ms boot, **templates + snapshots** (layer 3) | cloud + per-second; Python/JS SDK; Linux-only |
| **Daytona** | Docker, sub-90ms, OSS self-host, snapshots | mostly Python/JS; Linux-only |
| **Fly Machines / Sprites** | API microVMs, persistent volumes | cloud + per-second |
| **`tart` / Anka** | macOS VMs on Apple hardware | self-host Macs; the *only* macOS option |
| **Vagrant** | the multi-provider `provision→box` pattern, prior art | Ruby/CLI, heavier |

The SaaS players collide with three of our constraints at once (self-hosted,
.NET, **and** they're Linux-only so they don't touch the Mac/iOS problem that forced
this design). They're the right call *only* if we later decide to rent microVM
isolation + snapshotting for Linux-target flows.

**What no library gives us — we own regardless:** per-flow lifecycle, the
credential-never-in-the-box discipline, the egress allowlist tuned to Claude Code's
real domain footprint, git-at-the-seams, the warm-pool manager, and the fleet
router. Libraries give mechanics; we supply policy.

## 7. YAGNI posture

- Build the interface **from the real Linux implementation** — let `ISandbox` fall
  out of what `DockerProvisioner` (over Testcontainers) actually needs. Don't design
  it in the abstract for three OSes.
- **The Mac/Windows providers and the fleet router do not exist until a project
  needs them.** The seam earns its place now (the second use is known to be coming);
  the extra providers do not.
- Resist adding `ISandbox` methods for hypothetical Mac/Windows needs — every
  speculative method is a chance to get the abstraction wrong.

## 8. How this informs the spikes

- `flow-sandbox.SPIKE.md` Rung 0 becomes "wire **Testcontainers.NET** behind
  `ISandbox`," not "learn the Docker API."
- Add a warm-start probe to that spike: measure cold (install+clone) vs layer-1
  (baked image) vs layer-2 (pooled, pre-cloned) start, on a Unity image — the
  numbers decide how far up the warm-start stack we need to go.
- The `IProvisioner`/`ISandbox` seam is the integration contract the spike's
  acceptance rows lift into A.Box.

[Testcontainers.NET]: https://dotnet.testcontainers.org/
