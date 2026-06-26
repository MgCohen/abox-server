# Spike build: agent in a container over a mounted Unity project

Executable counterpart to [`PLAN.md`](PLAN.md). Validates: **agent edits a Unity
project inside a Linux Docker container, `dotnet build` compile-checks it with no
Unity installed, and Unity on the host closes the loop over the same mount.**

Verdict and evidence: [`results/SUMMARY.md`](results/SUMMARY.md). **U1–U5 demonstrated**
on `C:\Unity\random-game` (Unity 2022.3.50f1).

## Layout

```
image/build/   unity-asm.csproj   reconstructed SDK-style build project (the compile gate)
               build-asm.sh        compiles one asmdef vs the 3-part reference closure
agent/         AgentSpike.cs       the file the "agent turn" writes into the project
               run-turn.sh         in-container agent-turn stand-in (Rung 0)
host/          run-edit-tests.ps1  host-side Unity edit-mode test run (Rung 3)
results/       *.log + SUMMARY.md   captured proof
```

## Prerequisites

- Docker Desktop running (Linux engine). `docker info` shows `OSType=linux`.
- Unity **2022.3.50f1** installed (for the reference DLLs and the host prime/tests).
- A host-primed Unity project (`Library/ScriptAssemblies` present). `random-game` is.

The reference DLLs are **mounted** from the host install in these scripts (fast
iteration). Baking them into the image — the PLAN's "private image" — is a
`COPY` of `…/Editor/Data/Managed/UnityEngine` + `UnityEditor.dll`; not committed
here (EULA: see PLAN assumption 1).

## Run the rungs (PowerShell, from this folder)

Set once:
```powershell
$MANAGED = "C:\Program Files\Unity\Hub\Editor\2022.3.50f1\Editor\Data\Managed"
$PROJECT = "C:\Unity\random-game"
```

**Rung 0 — agent in a box over the mount (U1, U2)**
```powershell
docker run --rm -v "${PROJECT}:/project" -v "${PWD}\agent:/agent:ro" `
  alpine sh /agent/run-turn.sh
# then confirm the file exists on the host at $PROJECT\Assets\Scripts\Bootstrap\Runtime\AgentSpike.cs
```

**Rung 1 — compile-check without Unity (U3, U4)**
```powershell
docker run --rm -v "${PROJECT}:/project" -v "${MANAGED}:/unity-refs:ro" -v "${PWD}\image\build:/build:ro" `
  mcr.microsoft.com/dotnet/sdk:10.0 `
  sh /build/build-asm.sh CardMatch.Bootstrap '/project/Assets/Scripts/Bootstrap/Runtime/**/*.cs'
# U4: edit AgentSpike.cs to introduce a type error -> build fails; revert -> passes.
```

**Rung 2 — fidelity gap (U5)**
```powershell
# hide an error behind #if UNITY_STANDALONE_WIN, then:
docker run --rm -e EXTRA_DEFINES="" ...                 # passes (branch skipped) = false green
docker run --rm -e EXTRA_DEFINES="UNITY_STANDALONE_WIN" # fails (error surfaces)
```

**Rung 3 — host closes the loop (U6)**
```powershell
.\host\run-edit-tests.ps1   # runs Unity -batchmode -runTests over the same mount; needs a Unity license
```

**Rung 4 — source-closure build, no prime for project code (U7)**
```powershell
# resolver walks the primed .csproj ProjectReference graph, compiles the whole
# project-source closure from source; engine+packages are the only binaries.
docker run --rm -v "${PROJECT}:/project" -v "${MANAGED}\..\..:/unity-editor:ro" -v "${PWD}\image\build:/build:ro" `
  mcr.microsoft.com/dotnet/sdk:10.0 sh /build/build-closure.sh CardMatch.CardMatch
# (mount the install's Editor dir as /unity-editor). Edit a public API in a
# dependency asmdef + a consumer in the dependent -> still builds, no prime.
```

## Cleanup

The spike writes exactly one file into the project: `AgentSpike.cs` (+ a `.meta`
if Unity runs in Rung 3). Remove both to return the project to baseline.
