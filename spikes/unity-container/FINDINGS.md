# Findings — agent in a container over a mounted Unity project

Cold-readable verdict for the spike posed in [`PLAN.md`](PLAN.md). Evidence and
captured logs: [`results/SUMMARY.md`](results/SUMMARY.md) + [`results/`](results).
Built and observed on a real project (`C:\Unity\random-game`, Unity 2022.3.50f1),
Windows 11 host, Docker Desktop (Linux engine, WSL2), .NET SDK 10 container.

## Verdict: **VIABLE**

An AI agent can run in a Linux Docker container with a Unity project bind-mounted,
edit its C# in place, and `dotnet build` compile-check it **with no Unity in the
container** — while Unity on the host compiles the same edit and runs its edit-mode
suite green over the one shared mount. The split (compile in the box, Unity on the
host) holds end to end. Every claim below was demonstrated, not asserted.

| # | Claim | Result |
|---|---|---|
| U1 | Agent runs in the box over the mounted project | ✅ |
| U2 | Edits live on host, byte-identical, no copy | ✅ |
| U3 | C# compiles with **no Unity in the container** | ✅ |
| U4 | The compile-check has teeth (real error → fail) | ✅ |
| U5 | Fidelity gap characterized | ✅ |
| U6 | Host closes the loop (Unity EditMode 90/90) | ✅ |
| U7 | Cross-asmdef project-code edit needs **no prime** | ✅ |

## The flow that works

```
agent edits C# in container ──► dotnet compile-check in container (no Unity)
        │  live on host (U2)            │  builds (U3) · catches errors (U4) · no-prime cross-asmdef (U7)
        ▼                               ▼
   shared mount ◄───────────────► Unity on host: build + EditMode tests (U6: 90/90)
```

## Three load-bearing facts

1. **The box can't compile alone — the reference closure is three parts.** Engine
   modules + .NET BCL facades come from the box; the **project's own resolved
   closure** (`Library/ScriptAssemblies`, `Library/PackageCache`, incl.
   `UnityEngine.UI`) comes from the **host prime, on the mount**. The box supplies
   the engine; the project's closure rides the mount.

2. **Unity's generated `.csproj` is not portable.** It is non-SDK, targets
   `v4.7.1`, and every `HintPath` is a Windows absolute path. It will not
   `dotnet build` on Linux as-is. Two ways through: reconstruct an SDK-style project
   (Rung 1), or **walk Unity's `<ProjectReference>` graph** and compile the
   project-source closure from source while translating only the binary HintPaths
   to container paths (Rung 4 — the better path; it also lifts Unity's exact
   `DefineConstants` for free).

3. **The refresh boundary is source-on-the-mount vs Unity-produced binary** — not
   "Unity asmdef vs project asmdef":

   | Change | What refreshes | Cost |
   |---|---|---|
   | Edit within an asmdef | nothing | — |
   | **Public-API edit across project asmdefs** | nothing (recompiled from source) | — |
   | **Package** added/changed | host **prime** (mounted `Library/`) | seconds, no image rebuild |
   | **Unity version** change | **rebuild image** (baked engine DLLs) | image build |

   The container **image** rebuilds only on a Unity-version change; packages refresh
   through the mount via prime; project code — including cross-asmdef edits — needs
   neither, *provided the build resolves the source closure* (Rung 4 does).

## Fidelity limits — trust as a fast gate, not Unity parity

green-in-box is **not** a guarantee Unity accepts the code:

- **Scripting-define divergence** (demonstrated): code behind a define the gate
  doesn't set is silently skipped → false green. Rung 4 mitigates by lifting Unity's
  exact define set from the primed csproj.
- **Stale `ScriptAssemblies`** (the single-asmdef gate): cross-assembly API edits
  compile against stale sibling DLLs. Rung 4's source-closure build removes this for
  project code.
- **BCL surface**: net471 via `Microsoft.NETFramework.ReferenceAssemblies`, not
  Unity's own netstandard2.1 facades — a small surface-area divergence.
- **New asmdef** needs one prime to appear as a `.csproj` node.

## What this validates for the build

The agent's box is *just a Linux Docker container* for the entire edit →
compile-check loop. Unity (install + licensing + platform builds + edit/play-mode
tests) only ever runs on the host, over the same mount. The remaining work is
engineering, not validation: harden the `<ProjectReference>`-graph resolver, and
bake the engine DLLs into a private image (the PLAN's portable box — note the EULA
question in PLAN assumption 1).

## How to reproduce

See [`README.md`](README.md) — each rung is a single `docker run`. Scripts:
`agent/run-turn.sh` (U1/U2), `image/build/build-asm.sh` (U3/U4/U5),
`host/run-edit-tests.ps1` (U6), `image/build/resolve-closure.cs` +
`build-closure.sh` (U7).
