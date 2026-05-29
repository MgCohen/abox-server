# RemoteAgents.UI.Maui — install + run

MAUI Blazor Hybrid shell wrapping `UI.Components` for Windows + Android
(+ iOS when a Mac is available).

## What's shipped on this branch

| Target | Status |
|---|---|
| `net10.0-windows10.0.19041.0` | ✅ builds clean, runnable via `dotnet run -f net10.0-windows10.0.19041.0` |
| `net10.0-android` | ⏸ project is wired; build needs JDK + Android SDK (see below) |
| `net10.0-ios` / `net10.0-maccatalyst` | gated on `IsOSPlatform('osx')` — won't try unless on a Mac |

The Razor pages, API client, SignalR client, CSS all come from
`UI.Components` via ProjectReference — the MAUI shell is just a
`BlazorWebView` host. Same `Home` / `RunView` / `RunHistory` you see in
the web shell.

## Configure the Host address

Open [`MauiProgram.cs`](RemoteAgents.UI.Maui/MauiProgram.cs) and set
`DefaultHostBaseAddress` before building for your phone:

```csharp
public const string DefaultHostBaseAddress = "http://100.86.249.67:5050/";
//                                            ^ your laptop's Tailscale IPv4
```

The dev default (`http://localhost:5062/`) only works when the MAUI app
runs on the same machine as the Host.

## Run on Windows

```pwsh
cd remote-agents-dotnet
dotnet run --project ui/RemoteAgents.UI.Maui -f net10.0-windows10.0.19041.0
```

A native window opens with the same UI as the browser shell.

## Run on Android — prereqs

The `dotnet workload install maui` already installed the Android
build target, but you still need:

1. **A JDK 17+** (not just a JRE — the build needs the `jar` tool).
   Easiest: install Microsoft OpenJDK (`winget install Microsoft.OpenJDK.17`).
   Set `JAVA_HOME` to its install dir.
2. **The Android SDK** (platform-tools, build-tools 34+, platform 34).
   - Easiest path: install **Android Studio**, let it manage the SDK,
     set `ANDROID_HOME` to `%LOCALAPPDATA%\Android\Sdk`.
   - Lighter path: install `cmdline-tools` standalone, then:
     ```pwsh
     sdkmanager "platform-tools" "platforms;android-34" "build-tools;34.0.0"
     ```

Then:

```pwsh
dotnet build ui/RemoteAgents.UI.Maui -f net10.0-android -c Release
# APK lands under artifacts/bin/RemoteAgents.UI.Maui/release_net10.0-android/
adb install <apk-path>
```

The first Android build pulls down extra packages (Android workload
support libs, R8, etc.) and takes 5–10 min. Subsequent builds are
seconds.

## Run on iOS / macOS

Blocked on Mac availability (Apple toolchain is the constraint, not
MAUI). The TFMs are conditionally enabled when MSBuild sees
`IsOSPlatform('osx')`.

## Where to look in the code

- [`MauiProgram.cs`](RemoteAgents.UI.Maui/MauiProgram.cs) — DI + HostBaseAddress
- [`Components/Routes.razor`](RemoteAgents.UI.Maui/Components/Routes.razor) — router with `AdditionalAssemblies` for UI.Components
- [`Components/_Imports.razor`](RemoteAgents.UI.Maui/Components/_Imports.razor) — namespace imports
- [`MainPage.xaml`](RemoteAgents.UI.Maui/MainPage.xaml) — BlazorWebView host
- [`wwwroot/index.html`](RemoteAgents.UI.Maui/wwwroot/index.html) — loads `_content/RemoteAgents.UI.Components/pages.css`
