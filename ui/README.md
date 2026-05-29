# RemoteAgents UI

Mobile / web / desktop front-end for the C# orchestrator. Sibling to
[`../remote-agents-dotnet/`](../remote-agents-dotnet/) — the UI iterates
independently from the library and pulls it in by `ProjectReference`.

See [`../PLANS/csharp-orchestrator-ui.md`](../PLANS/csharp-orchestrator-ui.md) for the design,
phasing, and decisions.

## Projects

| Project | Role |
|---|---|
| `RemoteAgents.Host` | ASP.NET service exposing REST + SignalR over the orchestrator library |
| `RemoteAgents.UI.Components` | Shared Razor class library used by both web and MAUI shells |
| `RemoteAgents.UI.Web` | Blazor WebAssembly app, served by Host |
| `RemoteAgents.UI.Maui` | MAUI Blazor Hybrid shell for Windows + Android (deferred until C5) |

The library is referenced via `..\..\remote-agents-dotnet\src\RemoteAgents\RemoteAgents.csproj`.
`RemoteAgents.UI.slnx` bundles the four UI projects with the library + tests
as references (Go-To-Definition works across the boundary).

## Dev loop

```pwsh
# from the ui/ folder
dotnet build RemoteAgents.UI.slnx
dotnet run --project RemoteAgents.Host --launch-profile http
# Host listens on http://localhost:5062
```

REST endpoints (full list in `RemoteAgents.Host/RemoteAgents.Host.http`):

- `GET  /health`
- `GET  /projects` — from `<repo>/projects.json`
- `GET  /flows`    — `cli/flows/*.cs` minus `smoke-*`
- `POST /runs`     — spawns the flow, returns `RunSummary` (202)
- `GET  /runs` / `GET /runs/{id}` / `POST /runs/{id}/cancel`
- `POST /runs/{id}/respond` — scaffolded (answer-back routing pending
  library v2; see [`../PLANS/interaction-modes.md`](../PLANS/interaction-modes.md) Q10)

SignalR streaming hub: `/hub/runs`, method `Stream(runId)`
returning `ChannelReader<AgentEvent>`.

## Always-on (Windows service + Tailscale)

Phase C3 of the plan. Scripts under `scripts/`:

| Script | What |
|---|---|
| `configure-power.ps1` | AC sleep disabled, network adapter stays awake. Run once. |
| `install-host-service.ps1` | Detects Tailscale IP, publishes Host, registers as nssm-managed Windows service binding to `<tailnet-ip>:5050`. Re-runnable; replaces any prior install. |

Prereqs (one-time):

```pwsh
choco install nssm                  # the service manager
# Tailscale installed + signed in (already done — see infra plan W1-W3)
# .NET 10 SDK installed
```

Install (Administrator PowerShell):

```pwsh
.\scripts\configure-power.ps1
.\scripts\install-host-service.ps1
```

Verify from the laptop:

```pwsh
curl http://<laptop-tailnet-ip>:5050/health
```

Verify from the phone (already on the tailnet per infra plan W1-W3):
open Chrome / Safari to `http://<laptop-tailnet-ip>:5050/health`.

### Tailscale ACL

Add to your tailnet's `tailscale-acl.json` so the phone can reach the
chat-layer port (5050) but not SSH or the session-manager:

```json
{
  "acls": [
    { "action": "accept", "src": ["tag:laptop"], "dst": ["tag:unity-vm:22,5050,7681,7682"] },
    { "action": "accept", "src": ["tag:mobile"], "dst": ["tag:unity-vm:5050,7681"] }
  ]
}
```

## Smoke checklist

After install:

- [ ] `curl http://localhost:5050/health` returns `{"ok":true,...}`
- [ ] `curl http://localhost:5050/projects` lists projects.json entries
- [ ] `curl http://localhost:5050/flows` lists non-smoke flows
- [ ] `curl -X POST http://localhost:5050/hub/runs/negotiate?negotiateVersion=1`
      returns a `connectionToken`
- [ ] Phone browser hits `http://<tailnet-ip>:5050/` and renders the Blazor app (post-C4)
- [ ] Reboot the laptop → service auto-starts → phone still reaches it
