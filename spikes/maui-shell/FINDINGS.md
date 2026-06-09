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
| **Android** (`net10.0-android`, Android System WebView) | ✅ **PASS** | Builds warning-free with **JDK 21** (the JDK-17 concern was unfounded); deployed to an x86_64 API-36 emulator (hardware GPU, WHPX). `adb screencap` shows the nested neumorphic squares rendering correctly in portrait; a `screenrecord` → ffmpeg frame extraction shows the box-shadow melt/extrude **animating live** with the depth-stagger visible (frames differ; not static). |

**Conclusion: both native-shell risks are closed.** Pure-CSS neumorphism + the
melt/extrude/depth-stagger engine renders AND animates identically in WebView2
(PC) and Android System WebView, with no per-platform code and no JS interop.
Direct evidence for the plan's "zero-JS-interop = portability requirement" claim:
the same Razor + CSS ran unchanged across web, Windows-native, and Android-native.

## Emulator setup (reusable / scalable, per industry-standard pitfalls)

AVD `morph_pixel` — Pixel 7, `system-images;android-36;google_apis;x86_64`. Set
**against the CI default** because we test *visuals + animation*, not headless logic:
- **Hardware GPU** (`hw.gpu.mode=host`, windowed) — NOT `-no-window`/SwiftShader,
  which would hide the visuals and give fake (software-rendered) animation perf.
- x86_64 image on WHPX (Hyper-V already on) → cold boot ~21s. ARM image avoided
  (slow translation = fake perf).
- 4 GB RAM. Warm-boot snapshot enabled for fast scalable re-launches.
- **Caveat that stands:** even hardware-GPU emulator ≠ real mid-range phone GPU.
  Emulator proves render/layout/animation-correctness; a physical-device
  spot-check remains the ground truth for "holds 60fps with soft shadows."

Re-run: `emulator -avd morph_pixel -gpu host` then
`dotnet build spikes/maui-shell/MauiShell.csproj -f net10.0-android -t:Run`.

## Build notes (gotchas worth keeping)

- **Repo root `Directory.Build.props` must be insulated.** It forces a singular
  `<TargetFramework>net10.0</TargetFramework>` + `TreatWarningsAsErrors` +
  `UseArtifactsOutput`, all of which fight the MAUI multi-target build (first
  symptom: `NETSDK1047 ... doesn't have a target for net10.0-windows.../win-x64`).
  Fix: a local empty `spikes/maui-shell/Directory.Build.props` stops MSBuild's
  upward search (same trick `prototype/` uses). A real MAUI project added to the
  solution later will need the same consideration.
- JDK 21 (not the officially-recommended 17) builds BOTH the Windows and Android
  targets clean — the JDK-17 concern was unfounded on this toolchain.

## Build / run

```
dotnet build spikes/maui-shell/MauiShell.csproj -f net10.0-windows10.0.19041.0
# launches: spikes/maui-shell/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MauiShell.exe
```

To add Android back: restore the `net10.0-android` (and ios/maccatalyst) lines in
`MauiShell.csproj` `<TargetFrameworks>`.
