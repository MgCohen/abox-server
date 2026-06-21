# Design note: sandbox substrate abstraction (provider layer)

> **⚠️ Superseded — read [`sandbox.md`](sandbox.md) first; that is the build target.**
> We committed to the simpler spike-#81 model: Unity stays on the host and the box
> just shares the DLLs, behind one collapsed `ISandbox` (open · exec · close). The
> **multi-OS provider/router/`SandboxTarget`/`ImageRef`/`tty`-flag/warm-start/snapshot**
> machinery below is dropped as YAGNI for v1. The **security model** (threat model,
> egress denylist/allowlist, no-creds-in-box, file-only mount seam, anti-zombie
> teardown) still holds and is carried forward into `sandbox.md`. Kept for history and
> for the day a second OS forces a router back; **not** the current design.

Status: ~~**design note**, not built~~ **superseded — see banner above.** Cold-readable.
**Stacks with** the two spikes in this folder — it does not replace them:

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
differs per platform. Funnel that through one narrow seam.

The security model these substrates enforce lives in
[`control-plane.research.md`](control-plane.research.md); the **threat model** is the
one the spikes use — *guard against a wandering long-running agent grabbing keys, not
a malicious VM escape* — which is why "a container is enough" holds for v1 and the
microVM/snapshot options stay optional.

```csharp
public interface IProvisioner
{
    Task<ISandbox> ProvisionAsync(SandboxSpec spec, CancellationToken ct);
}

public sealed record SandboxSpec(
    SandboxTarget Target,        // Linux | Windows | MacOS — selects the provider
    ImageRef Image,              // provider-owned: a Docker image OR a VM template;
                                 // only the chosen provider interprets it
    DirectoryInfo Worktree,      // bind-mounted in: the repo the flow iterates over
    DirectoryInfo SessionDir,    // bind-mounted in: the agent's session files
                                 // (~/.claude/projects/*.jsonl, hooks.jsonl) — so
                                 // transcripts and questions read back off the host
    EgressPolicy Egress);        // deny-or-allowlist (§3); default deny host+internal

public interface ISandbox : IAsyncDisposable
{
    // tty: true for an agent turn (ConPTY = correct subscription billing,
    //      Oracle A1/A2); false for build / test / git plumbing.
    // onOutput: streams stdout as it arrives, so a live chat turn can be
    //      surfaced without holding a PTY open across the boundary.
    Task<ExecResult> ExecAsync(
        string command, bool tty, Action<string>? onOutput, CancellationToken ct);
}

public sealed record ExecResult(int ExitCode, string Stdout, string Stderr);
```

Design rules baked into the shape:

- **`ISandbox`, not `IContainer`.** The seam's whole job is to hide *whether the
  box is a container or a VM*. Naming it `IContainer` re-leaks that — and for
  Mac/Windows it isn't a container. Name it for what it is to the orchestrator: an
  isolated box.
- **`ImageRef` is provider-owned.** A Docker image and a `tart` VM template are not
  the same thing; rather than smuggle both through one `string`, the orchestrator
  treats `ImageRef` as opaque and only the matching provider interprets it. The
  container-vs-VM detail stays *inside* the provider — exactly where the seam means
  to keep it.
- **Files cross only at the edges, never via a file API.** The box is *born* with the
  worktree **and** the session dir mounted in; changes and transcripts come back out
  because the orchestrator reads those same mounts off the host — live, no copy, even
  mid-run (tail a transcript while the turn runs). So `ISandbox` carries **no
  read/write/export method** — its only runtime operations are `ExecAsync` and
  `DisposeAsync`. Mount everything you need to read back and "read a file in the box"
  collapses to a host read. (A no-shared-FS VM provider handles export at the seam via
  `git bundle` — a provider concern, not an interface method, and out of scope until
  that provider lands.)
- **`tty` is a per-exec flag, because billing demands it.** An agent turn must run
  under a PTY or subscription billing breaks — piped `-p` makes `isatty()` false
  (Oracle A1/A2; structured-questions FINDINGS Issue 3). Build / test / git run as
  plain execs. Same ConPTY discipline the prototype already honors, now *inside* the
  box.
- **`ExecResult` carries exit code + stderr, so executor health is gated separately
  from the question envelope.** A broken box (bad egress, missing tool) makes the
  agent *ask for help* — indistinguishable at the output level from a real clarifying
  question (FINDINGS Issue 1 / rec #2). Gate health on `ExitCode` / `Stderr`; never
  infer it from a `<<NEEDS_INPUT>>` envelope.
- **`DisposeAsync` is guaranteed anti-zombie teardown**, not best-effort — the box
  dies even if a run hung or threw (same teardown discipline the PTY path already
  honors). Orchestrator wraps the lifecycle in `finally`.
- **Async + actionable failure.** Provision can fail (no Mac node free), exec can
  fail — throw errors that say what to do, never swallow.

## 2. Capabilities the seam must serve

The seam is **`ExecAsync` + the mount** — a turn produces a result and writes files;
the orchestrator reads files back. Every expected flow has to land on that shape, or
it forces the seam to grow. They do:

| Capability | How it lands on exec + mount |
|---|---|
| Write files (code agent) | agent writes into the worktree; orchestrator reads the mount |
| Review files / work | exec reads files; review text / envelope returns in `ExecResult` |
| Write docs / plans | a doc is a file in the worktree — same path as code, read off the mount |
| Question / answer an agent | the structured `<<NEEDS_INPUT>>` envelope returns at the output level; the answer is a resumed exec. No live loop (structured-questions FINDINGS C1/C2) |
| Planning / long chat | an **endless turn-based flow**: each message is one resumed exec; the session JSONL persists in the box so context carries free (§4). Stream output by tailing the mounted transcript — input stays turn-based |
| Git | **outside the box**, on the orchestrator, at the flow seams — the box has no remote and no creds |
| Search web | a provider/egress decision — see §3 |

The one thing the seam deliberately does **not** serve: *live mid-turn* interaction —
token-by-token streaming input, interrupting a turn, answering a TUI-modal keypress.
That needs a live PTY across the boundary, and it's deferred (flow-sandbox §3;
interaction-modes Q10 / §7). The endless turn-based flow delivers the **chat dynamic**
without it: a managed agent talking to you, one turn at a time, over a box held open
for the conversation's life.

## 3. Egress policy — deny-or-allowlist

`EgressPolicy` expresses **two shapes**, chosen per flow:

| Shape | Blocks | Allows | For |
|---|---|---|---|
| **Denylist** (default) | host gateway, RFC1918, **`169.254.0.0/16`** (cloud metadata) | the rest of the public internet | normal dev flows: web search, `npm` / `nuget` / `apt`, dep fetch, MCP |
| **Allowlist** | everything | a named set (Anthropic only) | sensitive / untrusted flows where source exfil must be network-prevented |

The denylist is consistent with the threat model — *wandering agent grabbing keys, not
deliberate exfil* — **only** with all three mandatory blocks in place. The metadata
endpoint (`169.254.169.254`) is the literal key-grab path: a cloud node's IMDS hands
out IAM creds, so blocking it is non-negotiable, not optional. With those blocked
**and** the credential-never-in-the-box discipline, there is nothing to grab.

What the denylist gives up: source-code **exfiltration** is no longer
network-prevented — you trust the agent not to deliberately ship the worktree out. The
threat model already excludes the deliberately-malicious agent, so this is in-bounds;
the allowlist shape stays available for the flows where it isn't.

**Web search** rides this: Claude's server-side `web_search` (Anthropic does the fetch)
satisfies even the allowlist; the agent fetching arbitrary URLs needs the denylist, or
routes the fetch orchestrator-side and injects the result (box → orchestrator trust is
none, so an orchestrator pull is consistent with the model).

## 4. Persistence — long-running, not single-command

A flow runs an **unknown** number of agent turns over **one shared filesystem**.
The box must be **semi-persistent**: provision once → exec N times → close. This is
the standard "long-running sandbox" model, not the naive `run <cmd>`-and-exit one:

```
ProvisionAsync(spec)  ──▶  ExecAsync × N  (over the life of the flow)  ──▶  DisposeAsync
```

The handle holds the box open for the flow's life; the worktree and the agent's
session files persist across turns for free. This is already what the interface in
§1 encodes — one provision, many execs, one dispose.

An **endless chat flow** is the same model with no declared end: the box stays open
for the conversation, each message is one resumed exec, and context carries in the
persisted session JSONL. Its one new cost is **idle** — a box holding a host slot
between messages. That is what warm-start **layer 3** (suspend / snapshot, §7) is for:
suspend the idle chat box, restore it on the next message, rather than burn a node
while nobody is typing.

## 5. Providers — one per substrate, container *vs* VM is forced by OS

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

## 6. The fleet router (the part that actually grows)

"Use a Mac to spawn a Mac VM" implies a **pool of host nodes** — a Linux host, a Mac
mini, a Windows box. Something has to answer *"which available node can serve
`Target=MacOS`?"* and dispatch. For v1 (Linux only, one host) this is trivial:
localhost Docker. The router is the piece that grows when Mac/Windows arrive — more
than the providers themselves. Name it now so it isn't a surprise; build it when the
second host type lands.

## 7. Warm-start — three layers (critical for Unity)

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
argument *for* a managed service, when warm-start latency is critical. Layer 3 also
answers the **endless-chat idle cost** (§4): suspend an idle conversation box, restore
it on the next message.

## 8. Build vs buy

The substrate **mechanics** are solved; the **policy** is ours. Don't hand-roll the
Docker API.

**v1 — self-hosted, .NET, Linux: wrap [Testcontainers.NET].** It covers the seam's
mechanics: `WithBindMount` (worktree in *and* out via the mount), `ExecAsync`
(exec), `WithNetwork` (egress), and the **Ryuk resource reaper** gives guaranteed
teardown for free — i.e. §1's anti-zombie rule, solved. Make `DockerProvisioner` a
thin adapter over it, not raw `docker` calls. **Caveat:** Testcontainers is a
*test-harness* library; §4's hold-the-box-open-across-N-execs flow is not its default
usage pattern, so confirm the long-lived-container + repeated-`ExecAsync` path in the
spike before betting on it. (`Docker.DotNet` is the lower-level fallback if its test
ergonomics chafe.)

**Extension menu (only when a real need lands):**

| Option | Gives | Costs you |
|---|---|---|
| **e2b** | Firecracker microVMs, ~150ms boot¹, **templates + snapshots** (layer 3) | cloud + per-second; Python/JS SDK; Linux-only |
| **Daytona** | Docker, sub-90ms boot¹, OSS self-host, snapshots | mostly Python/JS; Linux-only |
| **Fly Machines / Sprites** | API microVMs, persistent volumes | cloud + per-second |
| **`tart` / Anka** | macOS VMs on Apple hardware | self-host Macs; the *only* macOS option |
| **Vagrant** | the multi-provider `provision→box` pattern, prior art | Ruby/CLI, heavier |

¹ Vendor-claimed boot times; treat as marketing until the `flow-sandbox` spike
measures real numbers.

The SaaS players collide with three of our constraints at once (self-hosted,
.NET, **and** they're Linux-only so they don't touch the Mac/iOS problem that forced
this design). They're the right call *only* if we later decide to rent microVM
isolation + snapshotting for Linux-target flows.

**What no library gives us — we own regardless:** per-flow lifecycle, the
credential-never-in-the-box discipline, the egress policy tuned to Claude Code's
real domain footprint, git-at-the-seams, the warm-pool manager, and the fleet
router. Libraries give mechanics; we supply policy.

## 9. YAGNI posture

- Build the interface **from the real Linux implementation** — let `ISandbox` fall
  out of what `DockerProvisioner` (over Testcontainers) actually needs. Don't design
  it in the abstract for three OSes.
- **The Mac/Windows providers and the fleet router do not exist until a project
  needs them.** The seam earns its place now (the second use is known to be coming);
  the extra providers do not.
- Resist adding `ISandbox` methods for hypothetical Mac/Windows needs — every
  speculative method is a chance to get the abstraction wrong.

## 10. How this informs the spikes

These decisions are now folded into `flow-sandbox.SPIKE.md`; recorded here as the
rationale behind them:

- Rung 0 wires **Testcontainers.NET** behind `ISandbox`, not raw `docker`.
- The egress rung proves the **denylist** form (§3) — host / internal / metadata
  blocked, public internet reachable — and the **allowlist** variant, not just
  `--network none`.
- The box mounts the **session dir** as well as the worktree (§1), so question
  detection and transcript-tail read off the host.
- The agent turn runs under a **TTY** (`ExecAsync(tty: true)`), preserving the
  subscription billing path inside the box.
- A warm-start probe measures cold (install+clone) vs layer-1 (baked image) vs
  layer-2 (pooled, pre-cloned) start on a Unity image — the numbers decide how far up
  the stack (§7) we need to go.
- The `IProvisioner`/`ISandbox` seam is the integration contract the spike's
  acceptance rows lift into A.Box.

[Testcontainers.NET]: https://dotnet.testcontainers.org/
