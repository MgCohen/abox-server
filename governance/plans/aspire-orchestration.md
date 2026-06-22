# Aspire for Infra + Dev Orchestration — Setup Guide & Eval

Status: evaluation + setup plan (not yet adopted). Companion to the architecture
docs in [`PLANS/rebuild/`](rebuild). Scope: how .NET Aspire would slot into
**abox-server** (this repo) and **abox-client** (the separate Blazor WASM UI
repo), what changes where, how to run both together locally, and whether it is
usable in the current cloud session.

> **One-line verdict.** Aspire is a good fit for *one layer* — provisioning,
> wiring, and observing backing infra + dev processes — and **not** a replacement
> for our own ports (`IRepository<T>`, `IProvider`, …). Adopt it **with the first
> real backing service**, not before. It runs in this cloud session: a project-only
> graph builds/runs out of the box, and a Postgres container provisions too once a
> daemon is bootstrapped (two lines, §5) — the only dev-only catches are
> ephemerality and the headless dashboard.

---

## 1. Mental model — where Aspire sits

Two layers, complementary, **not** competing:

```
   Domain port    (OUR seam — the pluggability)     IRepository<Project>
        │   same "theory" as IProvider / IAgentFactory — we own this
        ▼
   Adapter        (concrete impl)        JsonRepository  →  PostgresRepository
        │   uses the vendor client directly
        ▼
   Vendor client  (Aspire integration registers it)  NpgsqlDataSource
        │   Aspire wires config + health + telemetry
        ▼
   Resource       (Aspire AppHost provisions/runs)   postgres container / managed PG
```

Aspire never touches the top two layers. Its contribution is exactly **three
verbs** on the bottom two:

- **Provision** — run the actual Postgres/Redis/container (dev) or model it for deploy.
- **Inject** — resolve a resource *name* to a live connection string / URL into the
  consuming process, no hand-authored secrets or ports.
- **Instrument** — one line adds a health check + OpenTelemetry traces/metrics, surfaced
  in the dashboard.

If Aspire were stripped out, every adapter and port is unchanged; we'd just be
back to hand-writing connection strings, health checks, telemetry, and
`docker run`.

---

## 2. What changes on the **server** (this repo)

### 2.1 New: a shared resource-name + storage-selector (Infrastructure)

The single binding contract is a resource *name*; make it a constant, not a magic
string, so AppHost and Host agree:

```csharp
namespace ABox.Infrastructure.Storage;

public static class AboxResources
{
    public const string Db = "abox";   // == Aspire resource name == connection-string name
}

public enum StorageProvider { Json, Postgres }
public sealed record StorageOptions { public StorageProvider Provider { get; init; } = StorageProvider.Json; }
```

### 2.2 New: a second adapter behind the existing port (Infrastructure/Storage)

`PostgresRepository<T> : IRepository<T>` — mirrors `JsonRepository<T>` 1:1
(throw-on-dup / throw-on-missing), reuses `WireJson.Options`, stores each entity
as a JSONB row so it stays generic. Nothing above `IRepository<T>` changes;
`IProjectRepository`/`ProjectRepository` and every endpoint are untouched. Table
DDL is a migration / startup task (the one thing the file adapter did lazily that
a DB shouldn't). Driver is **Npgsql** (standard lib, not Aspire).

### 2.3 Changed: composition reads one flag (Host/Composition.cs)

```csharp
var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new();

switch (storage.Provider)
{
    case StorageProvider.Postgres:
        builder.AddNpgsqlDataSource(AboxResources.Db);                             // Aspire integration
        services.AddSingleton(typeof(IRepository<>), typeof(PostgresRepository<>));
        break;
    case StorageProvider.Json:                                                     // current default
        services.AddSingleton(StorageRoot.Default);
        services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
        break;
}
```

`Storage:Provider` is the **single knob**: it selects the adapter *and* gates
whether the Aspire integration (`AddNpgsqlDataSource`) runs. With it unset, the
Host behaves exactly as today — file-backed, zero infra.

### 2.4 New: the AppHost project (`src/AppHost/ABox.AppHost.csproj`)

The composition root for *resources*. It is additive and ASP.NET-free; the pure
engine never references it.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="<pin-current>" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsAspireHost>true</IsAspireHost>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="<pin-current>" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" Version="<pin-current>" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Host\ABox.Host.csproj" IsAspireProjectResource="true" />
  </ItemGroup>
</Project>
```

```csharp
// src/AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("pg").AddDatabase(AboxResources.Db);

var server = builder.AddProject<Projects.ABox_Host>("server")
                    .WithReference(db)
                    .WithEnvironment("Storage__Provider", nameof(StorageProvider.Postgres))
                    .WithEndpoint("https", e => e.Port = 7443);   // stable URL the WASM client targets

// Client lives in a SEPARATE repo → reference by path (no generated Projects.* type).
builder.AddProject("client", "../../abox-client/src/ABox.Web/ABox.Web.csproj")
       .WaitFor(server);

builder.Build().Run();
```

### 2.5 Optional: `ABox.ServiceDefaults`

Only when a **second process** exists (e.g. a future executor/Unity worker).
Adds `AddServiceDefaults()` → OpenTelemetry + health endpoints. Note it also
pulls service-discovery + HTTP-resilience we don't currently use, so until then
prefer adding the few OTel packages directly over taking the whole bundle. Honors
the existing minimal-observability stance (rebuild D2) until multi-process flips it.

> **Note — `AboxResources.Db` visibility.** The AppHost must see the constant. It
> already project-references `ABox.Host`, which references `ABox.Infrastructure`,
> so the constant is in scope. Keep it in Infrastructure; don't duplicate the
> string into the AppHost.

---

## 3. What changes on the **client** (abox-client repo)

**No UI code changes.** The only orchestration touch-points:

- **API base URL.** Standalone Blazor WASM reads its API URL from
  `wwwroot/appsettings.Development.json` (fetched by the browser at startup).
  Point it at the server's pinned dev endpoint:
  ```json
  { "ApiBaseUrl": "https://localhost:7443" }
  ```
- **Nothing else.** The client is added to the AppHost graph by *path* from the
  server repo (§2.4); the client repo itself needs no AppHost and no Aspire
  packages for this loop.

> **WASM boundary (the real limit).** `WithReference(server)` injects a URL as an
> **env var into a process**. Standalone WASM has no server-side runtime — calls
> happen in the **browser**, which never sees that env var. So we *pin* the server
> port and set it once in the client's served config, rather than relying on
> automatic injection. Aspire fully orchestrates *turning both on*; it only
> partially automates *wiring the URL* because of where WASM runs.

---

## 4. Running both together (local dev loop)

1. Clone the two repos side-by-side:
   ```
   ./abox-server
   ./abox-client
   ```
   (the `../../abox-client/...` path in §2.4 assumes this layout).
2. From the server repo: `dotnet run --project src/AppHost`.
3. Aspire brings up, in order: Postgres (container) → server (waits for DB) →
   client (waits for server). One Ctrl-C tears all of it down.
4. The Aspire **dashboard** opens with both processes' logs, console, restart
   controls, health, and request traces.

This is purely a **dev/test** convenience. Production is unchanged and
intentionally decoupled: client deploys to the device / static hosting, server to
its own box over Tailscale — no AppHost in the prod path.

---

## 5. Does it work in **this cloud session**? (live probe — 2026-06-20)

Probed the running container directly:

| Capability | Result | Detail |
|---|---|---|
| .NET 10 SDK | ✅ | `10.0.109`, RID `ubuntu.24.04-x64` |
| NuGet reachable | ✅ | `api.nuget.org` HTTP 200; `Aspire.Hosting.AppHost` resolvable |
| Aspire CLI / templates / workload | ❌ installed | not needed — modern Aspire is the `Aspire.AppHost.Sdk` + NuGet packages; hand-author the csproj (§2.4) |
| **Docker daemon** | ⚠️ **not running by default, but bootstrappable** | the socket is absent at session start, but the session can start its own daemon (see below) — `docker run hello-world` and `docker pull postgres:17-alpine` both succeed once it's up |

**Bringing up a daemon (verified 2026-06-20).** The session is `root` with
passwordless `sudo`; `dockerd`/`containerd` are preinstalled; user namespaces +
`/dev/fuse` + `overlay` are present; Docker Hub is reachable. The only missing
piece is cgroup delegation, which one mount fixes. Two lines bootstrap it:

```bash
sudo mount -t cgroup2 none /sys/fs/cgroup    # delegate cgroup v2 (starts as tmpfs, no controllers)
sudo dockerd >/tmp/dockerd.log 2>&1 &        # → Docker 29.3.1 on /var/run/docker.sock
```

Verified after bootstrap: daemon up (overlayfs, cgroup v2), `hello-world` runs,
`postgres:17-alpine` pulls clean. So `AddPostgres`/`AddContainer` **do work in a
web session** once the daemon is started.

**What this means for Aspire here:**

| Aspire feature | This session | Why |
|---|---|---|
| Build/run the AppHost (it's just a .NET exe) | ✅ | NuGet reachable; no daemon needed |
| Orchestrate **project/executable** resources (server + client) | ✅ | `AddProject`/`AddExecutable` spawn processes, not containers |
| Point at an **external/managed** Postgres via connection string | ✅ | `AddConnectionString(...)` — no local provisioning |
| **Provision a local Postgres/Redis/container** (`AddPostgres`, `AddContainer`) | ✅ **after bootstrap** | needs a container runtime; not running by default, but the two-line bootstrap above brings up a working daemon |
| Dashboard *process* launches | ⚠️ | starts, but viewing it needs a browser / port-forward — impractical headless |
| Full "F5 → everything up + browser" inner loop | ⚠️ | the *processes + containers* run; only the dashboard's browser view is awkward headless |

**Verdict for the cloud session.** Usable for **scaffolding and compile/run
verification** out of the box, and — once the daemon is bootstrapped — for the
**full container-provisioning loop** (`AddPostgres` + server + client) too. Two
caveats keep it dev-only: it's **ephemeral** (daemon, pulled images, and any
Postgres volume die with the session — no durable data, re-bootstrap each
session), and the **dashboard's browser view** is impractical headless (the
orchestration itself still runs). To make web sessions docker-ready
automatically, run the two-line bootstrap from a **SessionStart hook** (see the
`session-start-hook` skill) rather than by hand. Where durable data matters,
prefer an **external/managed Postgres via `AddConnectionString`** over the
ephemeral local container.

---

## 6. Adoption sequencing

1. **Now (optional, here):** land `PostgresRepository<T>` + the `Storage:Provider`
   switch + `AboxResources` behind the unchanged port, with **Json still the
   default** so nothing breaks. Build-verifiable in this session.
2. **First backing service:** add `src/AppHost`, provision Postgres, flip
   `Storage:Provider=Postgres` via the AppHost. Real `dotnet run` loop validated
   on any docker-capable host — including a web session once the §5 daemon
   bootstrap has run.
3. **Second process (executor/Unity worker):** add `ABox.ServiceDefaults` (OTel +
   health), revisit the minimal-observability stance, wire the worker into the
   graph + dashboard.
4. **Client integration loop:** add the client by path, pin the server port, set
   the client's served `ApiBaseUrl`.

---

## 7. Caveats to keep visible

- **Aspire ≠ our pluggability.** Ports (`IRepository<T>`, `IProvider`) stay ours;
  Aspire wires the box *under* the adapter. Don't wrap vendor clients in bespoke
  interfaces — that's reinventing the wheel.
- **WASM URL wiring is manual** (§3) — pin the port, set served config.
- **Cross-repo reference is by path** (string), not the type-safe `Projects.*`
  form, because the client is a separate repo.
- **Non-Azure deploy is less turnkey.** AppHost emits a manifest; our Hetzner +
  Tailscale target would consume it via compose/our own deploy, not one-click azd.
  Aspire's win here is the **dev/test** loop, not prod.
</content>
</invoke>
