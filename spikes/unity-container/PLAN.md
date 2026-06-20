# Spike: run an agent in a container mounted on a Unity project

Status: **spike plan**, not built. Cold-readable and standalone — everything you
need to run it is in this folder.

## The question

Can an AI coding agent run **inside a Docker container** that has a Unity project
**bind-mounted**, edit the project's C# in place, and **compile-check it with
`dotnet build` — without Unity installed in the container** — while the
authoritative Unity work (edit-mode tests, builds) runs **on the host** over the
same shared mount?

If yes, the agent's box is *just a Linux Docker container* for the whole edit →
compile-check loop, and Unity (with its install + licensing + platform needs) only
ever runs on the host. That would collapse a lot of complexity.

## The bet

A Unity project is, for the agent's purposes, a **C# project**. The game code
references `UnityEngine` / `UnityEditor`, but it can be **compiled without opening
the editor** — you only need the engine's *reference assemblies*, not a running,
licensed Unity. So:

| Loop | Runs where | Needs |
|---|---|---|
| Edit + **compile-check** (`dotnet build`) | **container** | .NET SDK + Unity *reference DLLs* (no editor) |
| Edit-mode / play-mode tests | **host**, signaled over the mount | running Unity (headless ok) |
| Platform build | **host** | running Unity + platform SDK |

The container and the host share **one directory** — the project — so an edit made
in the box is instantly visible to the host, and a host test run sees exactly what
the agent wrote.

## What the bet rests on (validate, don't assume)

1. **Compiling needs the engine DLLs, not the editor.** `using UnityEngine;`
   resolves against `UnityEngine.*.dll` / `UnityEditor.dll`, which ship with a
   Unity install. The container needs *those DLLs* baked in — but **not** an
   activated, running editor. Compiling against assemblies is not the licensed act;
   running the editor/player is. (Confirm the EULA stance for baking the DLLs into a
   **private** image for your own builds.)
2. **A faithful compile needs a host-primed project.** Unity generates the
   `.csproj`/`.sln` and resolves packages into `Library/PackageCache/`. Those are
   produced by Unity, not by `dotnet`. So the host **primes once** (import + resolve
   + regenerate project files), and the container builds against that primed state on
   the mount. Re-prime only when packages change.
3. **`dotnet build` ≈ Unity's compile, not `=`.** Unity uses its own compiler
   settings (scripting-define symbols like `UNITY_2022_3`, response files, an exact
   reference closure). A plain `dotnet build` catches the large majority of
   type/syntax errors — a good **fast gate** — but green-in-container is not a
   guarantee Unity will accept it. Characterize the gap; don't pretend it's zero.

## Acceptance — what "green" means

| # | Claim | Pass condition |
|---|---|---|
| U1 | Agent runs in the box over the mounted project | an agent CLI executes a turn inside the container and writes a file into the mounted project |
| U2 | Edits are live on the host | the file the agent wrote in-container appears **byte-identical** on the host with no copy step |
| U3 | C# compiles without Unity installed | `dotnet build` resolves `UnityEngine`/`UnityEditor` and builds the project; **no Unity editor present** in the container |
| U4 | The compile-check has teeth | inject a real type error → build **fails**; revert → build **passes** |
| U5 | Fidelity gap is characterized | test a Unity-define-dependent / Unity-specific construct; **document** where `dotnet build` diverges from Unity's own compile (known limits, not surprises) |
| U6 | The host closes the loop | after an in-container edit, the host runs Unity edit-mode tests (`Unity -batchmode -runTests`) over the **same mount** and sees the edit |

**Pass:** every row observed and captured as logged output in `results/`
(demonstrated, not asserted).

## Setup

**Container image** (`image/`):
- Linux base with the **.NET SDK**.
- Unity **reference assemblies** copied in (engine modules + `UnityEditor.dll`),
  from a licensed install. No editor, no activation.
- An **agent CLI** (whichever you're validating) installed.

**The project** (`project/`):
- A **small real Unity project** — enough C# that references `UnityEngine` and has
  at least one asmdef and one package dependency, plus one edit-mode test.

**Host prime** (`host/prime`):
- Run Unity once headless to import, resolve packages into `Library/PackageCache/`,
  and **regenerate** the `.csproj`/`.sln`. This populates the mount so the container
  can build.

## Rungs (each standalone, each ≈ a day)

### Rung 0 — agent in a box over the mount (U1, U2)
Mount the (primed) project into a plain container; run an agent turn that edits a
C# file; confirm the edit is live on the host. Cheapest — proves the
agent-in-container-over-mount shape end to end. No compile yet.

### Rung 1 — compile-check without Unity (U3, U4)
In the same container, `dotnet build` the project against the baked-in reference
DLLs. Then inject a type error and confirm the build fails; revert and confirm it
passes. Proves the fast gate works with no editor present.

### Rung 2 — fidelity gap (U5)
Probe where `dotnet build` and Unity's own compile disagree: a `#if UNITY_*`
branch, an analyzer/define-gated construct, a package-only type. Write down the
limits in `results/` so the gate's blind spots are known, not discovered later.

### Rung 3 — host closes the loop (U6)
After an in-container edit, run `Unity -batchmode -nographics -runTests
-testPlatform EditMode` **on the host** over the same mounted dir; confirm it picks
up the agent's change and reports results. Proves the split (compile in box, Unity
on host) actually closes.

## Suggested layout (when built)

```
unity-container/
  image/        # Dockerfile: .NET SDK + agent CLI + Unity reference DLLs
  project/      # a small real Unity project (asmdef + a package + one edit-mode test)
  host/         # prime script (import/resolve/regen csproj) + the host-side Unity test run
  agent/        # the in-container agent task + the sample edit it makes
  results/      # captured build logs, fidelity notes, the loop-closed proof
  README.md     # this claim + how to run each rung
```

## Out of scope

Network egress rules, security hardening, multi-OS providers, orchestrator wiring.
This spike validates **one** thing: agent-in-a-mounted-container + compile-without-
Unity + Unity-on-the-host. Nothing else.

## Definition of done

- U1–U6 observed, with captured `results/`.
- A one-paragraph verdict: **viable or not** for "agent in Docker over a mounted
  Unity project, compile in the box, Unity on the host" — including the **fidelity
  limits** found in Rung 2, and what the host prime actually had to do.
