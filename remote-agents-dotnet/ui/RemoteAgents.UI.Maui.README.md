# RemoteAgents.UI.Maui — install instructions (C5)

This MAUI Blazor Hybrid shell wraps the same `UI.Components` Razor library
the Web shell uses, targeting Windows + Android (+ iOS when a Mac is
available).

It is not in the repo yet because the MAUI workload didn't install in the
session that built C0–C4 + C6 — the elevation prompt was dismissed. Run
the four steps below from an **Administrator PowerShell** to land C5.

## 1. Install the MAUI workload

```pwsh
dotnet workload install maui
```

This downloads ~1 GB and takes 5–15 minutes. UAC will prompt — accept it.
If it still fails, the most common causes:

- Disk space: needs ~3 GB free on C:.
- Visual Studio installer locking: close any open VS instances.
- Stale prior partial install: `dotnet workload clean` then retry.

Confirm afterwards:

```pwsh
dotnet workload list
```

You should see `maui` in the list with its packs.

## 2. (For Android) install the Android SDK packs

```pwsh
dotnet workload install android
# or, bundled with maui (already done by step 1 in 10.0.200+)
```

You'll also need the **Android SDK** (Platform-Tools, Build-Tools 34+,
Platform 34) installed somewhere `dotnet` can find. Two paths:

- **Easiest**: install **Android Studio**; let it manage the SDK; set
  `ANDROID_HOME` to its sdk dir (typically `C:\Users\<you>\AppData\Local\Android\Sdk`).
- **Lighter**: install `sdkmanager` standalone from
  https://developer.android.com/tools/sdkmanager and run
  `sdkmanager "platform-tools" "platforms;android-34" "build-tools;34.0.0"`.

## 3. Scaffold the project

From repo root:

```pwsh
cd remote-agents-dotnet
dotnet new maui-blazor -n RemoteAgents.UI.Maui -o ui/RemoteAgents.UI.Maui --framework net10.0
dotnet add ui/RemoteAgents.UI.Maui/RemoteAgents.UI.Maui.csproj reference ui/RemoteAgents.UI.Components/RemoteAgents.UI.Components.csproj
```

Add it to the second solution file:

```
# RemoteAgents.UI.slnx
<Folder Name="/ui/">
  ...existing entries...
  <Project Path="ui/RemoteAgents.UI.Maui/RemoteAgents.UI.Maui.csproj" />
</Folder>
```

## 4. Wire it to UI.Components

Replace `ui/RemoteAgents.UI.Maui/MauiProgram.cs`'s service registration
with the same shape `UI.Web` uses:

```csharp
builder.Services.AddMauiBlazorWebView();
builder.Services.AddScoped(_ => new HttpClient {
    BaseAddress = new Uri(builder.Configuration["HostBaseAddress"]
                          ?? "http://<laptop-tailnet-ip>:5050/")
});
builder.Services.AddScoped<HostApiClient>();
```

Replace `Pages/Main.razor`'s `<Router>` with the same AdditionalAssemblies
form as `UI.Web/App.razor`:

```razor
@using RemoteAgents.UI.Components.Pages
<Router AppAssembly="@typeof(MauiProgram).Assembly"
        AdditionalAssemblies="new[] { typeof(Home).Assembly }">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)"/>
    </Found>
</Router>
```

The `HostBaseAddress` config key wants the Tailscale IP of the laptop
running the Host service, so the phone can reach it over the tailnet.

## 5. Build + run

Windows:

```pwsh
dotnet build ui/RemoteAgents.UI.Maui -f net10.0-windows10.0.19041.0
dotnet run --project ui/RemoteAgents.UI.Maui -f net10.0-windows10.0.19041.0
```

Android (sideload to phone via USB debugging):

```pwsh
dotnet build ui/RemoteAgents.UI.Maui -f net10.0-android -c Release
# .apk lands in artifacts/bin/RemoteAgents.UI.Maui/release/...
# adb install <apk-path>
```

## Notes

- The Razor pages, API client, SignalR client, and CSS are already
  shipped under `ui/RemoteAgents.UI.Components/` — MAUI just needs to
  host them in a `BlazorWebView`.
- Once Android is signed-on-tailnet, the same `http://<laptop-tailnet-ip>:5050`
  URL the Web shell uses works for MAUI too.
- iOS path is unchanged but blocked on Mac availability — Apple toolchain
  is the constraint, not MAUI.
