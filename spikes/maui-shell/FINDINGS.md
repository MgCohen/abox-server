# MAUI Blazor Hybrid shell spike — FINDINGS

Throwaway spike (2026-06-09). Goal: prove the validated pure-CSS melt/extrude +
neumorphism + depth-stagger renders inside the **native shells** (MAUI Blazor
Hybrid / `BlazorWebView`), not just desktop Chrome — i.e. that the same Razor/CSS
we'd ship to web survives unchanged in a real PC/mobile app.

## What it is

- `dotnet new maui-blazor` trimmed to a single page (`Components/Pages/Home.razor`).
- Three nested neumorphic squares (`l0`>`l1`>`l2`), each a `morph-item` with an
  inline `--depth`. Engine CSS in `wwwroot/morph.css`.
- Orchestration = the validated phase state machine in C#, **zero JS interop**:
  `idle → exiting (melt, innermost-first) → entering (extrude, outermost-first) → idle`,
  phase-wait computed from max depth, single `StateHasChanged` per phase.
- Auto-plays once on first render; `Replay` button replays.

## Result

| Target | Status | Evidence |
|---|---|---|
| **Windows native** (`net10.0-windows`, WebView2) | ✅ **PASS** | Builds warning-free `win-x64`; native window renders the nested neumorphic squares with correct soft raised shadows; melt→extrude auto-play completed and settled in the extruded idle state. Screenshot confirmed by owner. |
| Android (`net10.0-android`) | ⏸ not yet run | Tooling present (JDK 21, Android SDK platforms 36/36.1, build-tools 37); needs a device or emulator + target re-added to `.csproj`. |

**Conclusion: the PC-native-shell risk is closed.** Pure-CSS neumorphism + the
melt/extrude/depth-stagger engine renders identically in WebView2 to Chrome, with
no per-platform code and no JS interop. This is direct evidence for the
plan's "zero-JS-interop = portability requirement" claim.

## Build notes (gotchas worth keeping)

- **Repo root `Directory.Build.props` must be insulated.** It forces a singular
  `<TargetFramework>net10.0</TargetFramework>` + `TreatWarningsAsErrors` +
  `UseArtifactsOutput`, all of which fight the MAUI multi-target build (first
  symptom: `NETSDK1047 ... doesn't have a target for net10.0-windows.../win-x64`).
  Fix: a local empty `spikes/maui-shell/Directory.Build.props` stops MSBuild's
  upward search (same trick `prototype/` uses). A real MAUI project added to the
  solution later will need the same consideration.
- JDK 21 (not the officially-recommended 17) is installed — fine for the Windows
  target; re-verify when the Android build runs.

## Build / run

```
dotnet build spikes/maui-shell/MauiShell.csproj -f net10.0-windows10.0.19041.0
# launches: spikes/maui-shell/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MauiShell.exe
```

To add Android back: restore the `net10.0-android` (and ios/maccatalyst) lines in
`MauiShell.csproj` `<TargetFrameworks>`.
