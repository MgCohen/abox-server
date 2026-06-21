# Results — agent in a container over a mounted Unity project

Environment: Windows 11 host, Docker Desktop (Linux engine, WSL2), .NET SDK 10
container (`mcr.microsoft.com/dotnet/sdk:10.0`), Unity **2022.3.50f1** installed
on host. Project under test: `C:\Unity\random-game` (real project, already
host-primed: `Library/ScriptAssemblies` + `Library/PackageCache` + generated
`.csproj` present). Mounted **read-write**; the agent only ever creates one new
file (`Assets/Scripts/Bootstrap/Runtime/AgentSpike.cs`); no existing file modified.

## Acceptance

| # | Claim | Result | Evidence |
|---|---|---|---|
| U1 | Agent runs in the box over the mounted project | **PASS** | `rung0.log` — container process writes `AgentSpike.cs` into `/project` |
| U2 | Edits are live on the host, byte-identical, no copy | **PASS** | `rung0.log` — in-container sha256 == host sha256 (`2e0d31…4d2f`) |
| U3 | C# compiles without Unity installed | **PASS** | `rung1.log` — `CardMatch.Audio` → `.dll`, **no editor in container**, resolves `UnityEngine` + `UnityEngine.UI` |
| U4 | The compile-check has teeth | **PASS** | `rung1-u4.log` — valid=0, inject `CS0029`→FAILED(1), revert=0 |
| U5 | Fidelity gap characterized | **PASS** | `rung2.log` + notes below — define-divergence false-green demonstrated |
| U6 | Host closes the loop (Unity edit-mode tests over same mount) | **PASS** | `rung3-unity.log` + `rung3-editmode-results.xml` — Unity imports `AgentSpike.cs`, compiles it, runs EditMode: **90/90 passed** |
| U7 | Cross-asmdef project-code edit needs **no prime** (source-closure build) | **PASS** | `rung4a.log` + `rung4b.log` — see Rung 4 below |

## Rung 4 — source-closure build & the "no prime for project code" claim (U7)

Rung 1's gate compiled **one** asmdef and resolved its siblings as prebuilt
`Library/ScriptAssemblies/*.dll` — fast, but a cross-asmdef public-API edit is then
invisible until the host re-primes (fidelity gap #2). Rung 4 removes that limit for
project code with a **graph-walking resolver** (`image/build/resolve-closure.cs`):

- Walks Unity's primed `.csproj` **`<ProjectReference>`** edges to find the
  project-source closure (e.g. `CardMatch.CardMatch` → `CardMatch.Utility` →
  `External.DOTween`), compiling **all of them from source** in one `dotnet build`.
- Collects only the `<Reference>` HintPaths (engine + packages), translating Windows
  paths to container paths (`…/Editor` → `/unity-editor`, project-relative →
  `/project`), dropping BCL facades (replaced by `ReferenceAssemblies`) and the
  closure's own stale `ScriptAssemblies`.
- Lifts Unity's **exact `DefineConstants`** from the primed csproj — closing
  fidelity gap #1 (no more hand-picked define subset).

**Demonstration (`rung4b.log`):** a new public API added to `CardMatch.Utility`
and consumed from `CardMatch.CardMatch`:

| Build mode | Result | Meaning |
|---|---|---|
| source-closure (Utility from source) | **PASS** | cross-asmdef project-code edit compiles with **no prime** |
| stale `ScriptAssemblies` (primed `Utility.dll`) | **FAIL** (`SpikeApi` not found) | the prebuilt-sibling path *would* need a prime |
| source-closure after evolving the API signature | **PASS** | iterate freely across the asmdef boundary, still no prime |

### Refresh taxonomy (corrected)

The real boundary is **source-on-the-mount vs Unity-produced binary**, not
"Unity asmdef vs project asmdef":

| Change | What refreshes | Cost |
|---|---|---|
| Edit within one asmdef | nothing | — |
| Public-API edit **across project-source asmdefs** | nothing (recompiled from source) | — |
| **Package** added/updated | host **prime** (refreshes mounted `Library/PackageCache`) | seconds–minutes, no image rebuild |
| **Unity version** change | **rebuild image** (baked engine DLLs) | image build |

So the container **image** only rebuilds on a Unity-version change; packages refresh
through the mounted `Library/` via prime; and **project code — including
cross-asmdef edits — needs neither**, as long as the build resolves the source
closure rather than leaning on `ScriptAssemblies`.

### Rung 4 limits / notes

- The resolver trusts the primed `.csproj` graph; a **newly added asmdef** (not yet
  primed into a `.csproj`) needs one prime to appear as a node.
- BCL surface is net471 via `ReferenceAssemblies`, not Unity's own netstandard2.1
  facades — a 4th, smaller fidelity dimension (rare API-surface differences).
- Closure compiles can pull a lot of source (e.g. `External.DOTween`); fine as a
  gate, but larger than a single-asmdef compile.

## Verdict

**Viable.** An AI agent can run in a Linux Docker container with a Unity project
bind-mounted, edit its C# in place (live on the host, byte-identical, no copy), and
`dotnet build` compile-checks it with **no Unity in the container** — while Unity on
the host compiles the same edit and runs its edit-mode suite green over the one
shared mount. The split (compile in the box, Unity on the host) holds end to end.

**What the host prime had to provide:** the container's compile is only possible
because Unity (on the host) had already produced `Library/ScriptAssemblies/*.dll`
(incl. `UnityEngine.UI`) and `Library/PackageCache/` and resolved the package set.
The box supplies the engine reference DLLs + .NET Framework facades; the *project's*
reference closure comes from the primed `Library/`. Re-prime when packages or
cross-assembly public APIs change.

**Fidelity limits (trust as a fast gate, not as Unity parity):** scripting-define
divergence silently skips gated code (demonstrated); the gate compiles against
*stale* sibling assemblies between primes; Unity's own `.csproj` is not portable to
`dotnet build` (Windows HintPaths + non-SDK net471), so the gate reconstructs an
SDK-style project. See gaps 1–3 above.

## How the compile-check is wired (the load-bearing detail)

`dotnet build` cannot consume Unity's own generated `.csproj`: it is non-SDK,
targets `v4.7.1`, and every `<HintPath>` is a **Windows absolute path**
(`C:\Program Files\Unity\...`). Instead the gate uses a reconstructed SDK-style
project (`image/build/unity-asm.csproj`) that compiles one asmdef's sources
against the **three-part reference closure** Unity actually used:

| Closure part | Source | In the box |
|---|---|---|
| Engine modules (`UnityEngine.*Module.dll`) | `…/Editor/Data/Managed/UnityEngine` | baked/mounted into image |
| .NET Framework facades (mscorlib, System.*) | Unity `…/Data/NetStandard` shims | replaced by `Microsoft.NETFramework.ReferenceAssemblies` (net471) NuGet |
| Project asmdefs + packages (`UnityEngine.UI`, `Newtonsoft`, sibling `CardMatch.*`) | **`/project/Library/ScriptAssemblies`, `Library/PackageCache`** | **on the mount, from host prime** |

This generalizes: `CardMatch.Bootstrap` (8 sibling-asmdef deps) compiles by
resolving the siblings from `Library/ScriptAssemblies`.

## Fidelity gaps (U5) — the gate's known blind spots

1. **Scripting-define divergence (demonstrated).** The gate hardcodes a subset of
   Unity's ~200 `#define`s. Code behind a define Unity sets but the gate omits is
   **silently skipped** → false green. Shown: a `CS0029` hidden behind
   `#if UNITY_STANDALONE_WIN` compiled clean under the gate's defaults, and only
   FAILED once `UNITY_STANDALONE_WIN` was added. A faithful gate must mirror the
   exact define set for the target platform/editor context.
2. **Stale `ScriptAssemblies` (structural).** Sibling assemblies are resolved from
   the **last Unity compile**. Editing asmdef A's public API and rebuilding asmdef
   B compiles B against the *old* A.dll → green-in-box, red-in-Unity until the host
   re-primes. The gate is fast precisely because it does not recompile the whole
   graph; that is also its limit.
3. **Unity's own `.csproj` is not portable.** Windows `HintPath`s + non-SDK net471
   mean the project files Unity emits cannot be fed to `dotnet build` on Linux
   as-is. The gate reconstructs an SDK-style project rather than consuming them.

**Net:** `dotnet build` in the box is a real, fast type/syntax gate that catches
the large majority of errors with no Unity present — but green-in-box is **not** a
guarantee Unity accepts the code. Trust it as a fast pre-check, not as Unity parity.
