# Contract publishing — per-feature `Api`/`Contract` split + one shared NuGet package

**Status:** server side **built & green** 2026-06-26 (Phases 0–5; ratified by
[ADR 0014](../design/adr/0014-contract-leaf-split-api-and-contract.md)). Client cutover (Phase 6)
+ the publish dry-run remain — they live in the client repo. Read cold — no prior context needed.

**Goal.** Share the server's public-facing wire DTOs with the separate **client** repo
(all-.NET, Blazor WASM) from a **single source of truth**, with publishing/fetching driven
by automation, while keeping the server's internal assembly structure free to grow
unbounded.

**Companion (client side):** `A.Box-client/docs/server-handoff.md` (the wire contract) and
`A.Box-client/docs/contract-publishing.md` (the client's view of this same loop). This doc is
the **server-side** authority; keep the two reconciled.

---

## TL;DR

- Each feature gets **three** assemblies by folder/name convention: `Foo` (internals),
  `Foo.Api` (public-facing request/response DTOs), `Foo.Contract` (cross-feature DTOs +
  integration events).
- **Only `*.Api` is shared off-box.** A single packaging project, `ABox.Api`, **auto-discovers
  every `*.Api` via a wildcard** and bundles their DLLs into **one** `.nupkg`. No curation.
- Publish is **tag-driven** from the server (MinVer + a GitHub Action → GitHub Packages).
  Fetch is a **Dependabot PR** on the client. One package, one version, one `PackageReference`.
- Auto-discovery + a placement/dependency **enforcement test** mean a new feature flows into
  the package for free, and a mis-placed/mis-named one **fails CI** rather than silently
  dropping out.

This **amends [ADR 0011](../design/adr/0011-canonical-feature-slice-shape.md)**: the single
`Contracts` leaf splits into two leaves — `Api` (published) and `Contract` (internal).

---

## Why this shape

Both ends are .NET, in **separate repos** → the industry-standard answer is to **share the
actual contract types as a versioned NuGet package**, not regenerate (OpenAPI/Kiota clones the
DTOs — more machinery, less safety, and only warranted when crossing languages) and not
hand-copy (silent drift). The package gives the client **compile-time safety against the type
shape**; it does **not** close the HTTP-wire gap (camelCase/JSON mismatches stay runtime
errors) — that's unchanged and acknowledged on the client side.

The reason for the **role split** rather than "publish everything under one Contracts leaf":
we want automation *and* enforcement, with **no per-feature curation**. So the seam is *where a
type lives*, not a hand-maintained allow-list. Want something shared externally → put it in
`Api`. Want it server-internal but cross-feature → `Contract`. Want it private → the feature
root. Changing the boundary is moving a file, and the rollup + tests follow automatically.

---

## Target structure (per feature, `Task` as the example)

| Folder | Assembly | Role | Shared off-box? |
|---|---|---|---|
| `Features/Task/` *(excl. `Api`, `Contract`)* | `ABox.Task` | feature internals (endpoints, handlers, domain) | ❌ |
| `Features/Task/Api/` | `ABox.Task.Api` | public-facing DTOs — request/response | ✅ **published** |
| `Features/Task/Contract/` | `ABox.Task.Contract` | cross-feature DTOs — request/response/**events** | ❌ (server-internal) |

`Api` is the **external** surface (the client). `Contract` is the **internal** module-to-module
seam (other features bind it; the client never sees it). The feature root is private to the
feature.

### Dependency direction (enforced by Arch rules)

```
 Inbox (internals) ─────────────► Task.Contract        a feature depends on another ONLY via .Contract
 Task  (internals) ─► Task.Api,  Task.Contract         a feature owns/produces its own surfaces
 Task.Api      ─► (pure DTOs; no internals, no .Contract)        ◄── published
 Task.Contract ─► (pure DTOs/events; no internals)
 client (external) ─► ABox.Api  ==  Σ all *.Api                   ◄── the one shared package
```

Rules (ArchUnitNET, over namespaces — consistent with ADR 0011's enforcement model):

1. A feature may reference another feature **only via its `.Contract`** — never its root or `.Api`.
2. `*.Api` and `*.Contract` may **not** reference feature internals (keeps them shippable, acyclic).
3. **Only `*.Api` is packable.** Roots and `*.Contract` can never reach the feed.
4. **`*.Api` stays dependency-free** (DTOs/records only; at most one shared `*.Api` primitives
   assembly that is *also* bundled). This keeps the rollup trivially self-contained — see the
   transitive-dep caveat below.

---

## The one structural gotcha — nested projects

We want `Task/` to be a project **and** `Task/Api`, `Task/Contract` nested under it. By default
the root's `**/*.cs` glob **swallows the subfolders' files** → double-compile. Fixed by
convention, auto-injected, zero per-feature edits:

```xml
<!-- src/Features/Directory.Build.props — applies to every feature-ROOT project automatically -->
<PropertyGroup Condition="!$(MSBuildProjectName.Contains('.Api')) and !$(MSBuildProjectName.Contains('.Contract'))">
  <DefaultItemExcludes>$(DefaultItemExcludes);Api\**;Contract\**</DefaultItemExcludes>
</PropertyGroup>
```

(Alternative considered: sibling layout — `Task/Feature`, `Task/Api`, `Task/Contract`, root is
just a directory. No exclude needed. **Rejected** in favour of nesting because the nested shape
matches the mental model and the exclude is a one-time convention, not recurring work.)

---

## How the `ABox.Api` rollup works

A **packaging-only** project — no code of its own. It references every `*.Api` project, then
redirects their compiled DLLs into one `.nupkg` instead of letting NuGet turn each into a
separate `<dependency>`.

```xml
<!-- src/Api/ABox.Api.csproj — the ONLY IsPackable=true project in the repo -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>ABox.Api</PackageId>
    <TargetsForTfmSpecificBuildOutput>$(TargetsForTfmSpecificBuildOutput);BundleApiDlls</TargetsForTfmSpecificBuildOutput>
  </PropertyGroup>

  <!-- ① discovery: wildcard, expands at eval time — a new Foo/Api shows up for free -->
  <!-- ② PrivateAssets=all: do NOT emit these as <dependency> in the nuspec -->
  <ItemGroup>
    <ProjectReference Include="$(MSBuildThisFileDirectory)..\Features\*\Api\*.Api.csproj"
                      PrivateAssets="all" />
  </ItemGroup>

  <!-- ③ bundling: copy each referenced Api DLL into the package's lib/ -->
  <Target Name="BundleApiDlls" DependsOnTargets="ResolveReferences">
    <ItemGroup>
      <BuildOutputInPackage Include="@(ReferenceCopyLocalPaths->WithMetadataValue('ReferenceSourceTarget','ProjectReference'))" />
    </ItemGroup>
  </Target>
</Project>
```

### What `dotnet pack src/Api/ABox.Api.csproj` does

```
1. Restore   wildcard ..\Features\*\Api\*.Api.csproj
             → ABox.Task.Api, ABox.Inbox.Api, ABox.Projects.Api, …  (N projects)
2. Build     builds each Api project (+ deps)
             → DLLs land in ReferenceCopyLocalPaths, tagged ReferenceSourceTarget=ProjectReference
3. Pack      TargetsForTfmSpecificBuildOutput → BundleApiDlls runs
             → appends every *.Api.dll to BuildOutputInPackage
             → pack copies rollup DLL + all BuildOutputInPackage into lib/net10.0/
             → PrivateAssets=all ⇒ nuspec <dependencies> stays EMPTY
```

Result:

```
ABox.Api.1.3.0.nupkg
└─ lib/net10.0/
   ├─ ABox.Api.dll            ← empty rollup marker, harmless
   ├─ ABox.Task.Api.dll
   ├─ ABox.Inbox.Api.dll
   ├─ ABox.Projects.Api.dll
   └─ …one per feature        (no <dependency> entries — self-contained)
```

Client: one `<PackageReference Include="ABox.Api" />` → all N `*.Api.dll` become references.
**One package in, N assemblies available.**

### Why each piece is load-bearing

| Piece | If omitted |
|---|---|
| wildcard `ProjectReference` | back to curation; new feature silently missing |
| `PrivateAssets="all"` | each `*.Api` emitted as a `<dependency>` → client tries to restore N packages that don't exist → restore fails |
| `BundleApiDlls` target | with deps hidden *and* nothing copying DLLs in, package ships **empty** |

`PrivateAssets` + the target are a pair: one says "don't reference them externally," the other
says "so embed them physically instead."

### Transitive-dep caveat

- A `*.Api` referencing **another in-repo project** (e.g. a shared `ABox.Api.Primitives`) → add
  it to the same wildcard/bundle so its DLL rides along.
- A `*.Api` referencing an **external NuGet** → `PrivateAssets="all"` would *hide* it from the
  client and break them; a real external dep must stay a proper `<dependency>`, handled
  separately from the bundled P2P refs.
- ⇒ Rule 4 above: keep `*.Api` dependency-free. Then the rollup is trivially correct.

---

## Enforcement (automation + the guardrail)

| Concern | Mechanism | Zero-touch for new features? |
|---|---|---|
| Role + packability by name | `src/Features/Directory.Build.props`: `*.Contract` → `IsPackable=false`; roots → exclude `Api/**;Contract/**`; `*.Api` → owned by the rollup | ✅ inherited by name |
| Discover + bundle | `ABox.Api.csproj` wildcard `ProjectReference` + `BundleApiDlls` target | ✅ glob expands |
| Enforce the convention holds | tests (via `test-rulebook` skill): `Features/*/Api/*` glob-set **==** rollup's resolved refs **==** DLLs in the produced `.nupkg`; **+** the 4 dependency Arch rules | ✅ CI fails on drift |

The enforcement test is the point of the whole design: a `Foo/Api` placed at a wrong path or
flipped to `IsPackable=true` **breaks CI** instead of silently leaving (or sneaking into) the
package.

---

## Publish / share / fetch loop (GitHub Packages)

```
SERVER  (Phase 4)
  git tag v1.3.0 && git push --tags
    └─ publish-contracts.yml fires on tags: v*
         ├─ MinVer reads tag → version 1.3.0
         ├─ dotnet pack ABox.Api  → ABox.Api.1.3.0.nupkg
         └─ dotnet nuget push     → GitHub Packages (owner feed)   ← first push creates the package
THE FEED  ── nuget.pkg.github.com/MgCohen/index.json  (private)
CLIENT  (Phase 6)
  Dependabot polls the feed
    └─ sees 1.3.0 > pinned 1.2.0
         ├─ opens PR: bump <PackageReference> + packages.lock.json
         ├─ client CI restores --locked-mode, builds, tests
         └─ review DTO diff → merge
```

### Publish — server, Phase 4

```yaml
# .github/workflows/publish-contracts.yml
on: { push: { tags: ['v*'] } }
jobs:
  publish:
    runs-on: ubuntu-latest
    permissions: { contents: read, packages: write }
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }                 # MinVer needs full tag history
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet pack src/Api/ABox.Api.csproj -c Release -o ./nupkg
      - run: >
          dotnet nuget push "./nupkg/*.nupkg"
          --source "https://nuget.pkg.github.com/MgCohen/index.json"
          --api-key ${{ secrets.GITHUB_TOKEN }} --skip-duplicate
```

Server **publish auth is free** — built-in `GITHUB_TOKEN` pushes to its own owner's feed.

### Fetch — client, Phase 6

```xml
<!-- nuget.config -->     <add key="github" value="https://nuget.pkg.github.com/MgCohen/index.json" />
<!-- client csproj -->    <PackageReference Include="ABox.Api" Version="1.3.0" />
                          <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
```
```yaml
# .github/dependabot.yml
registries:
  gh: { type: nuget-feed, url: https://nuget.pkg.github.com/MgCohen/index.json, token: ${{ secrets.PACKAGES_READ_PAT }} }
updates:
  - package-ecosystem: nuget
    directory: "/"
    registries: [gh]
    schedule: { interval: daily }
```

### Auth reality — one `read:packages` PAT, three places

The account is a **personal account**, not an org. An org can grant a package to a sibling repo
and let its `GITHUB_TOKEN` read it; a **user-owned package can't be read cross-repo by a bare
`GITHUB_TOKEN`**. So consuming needs a PAT.

| Who | Needs | Why |
|---|---|---|
| Server CI (publish) | `GITHUB_TOKEN` ✅ built-in | pushing to own feed — free |
| Client CI (restore) | PAT `read:packages` (repo secret) | cross-repo read from a user feed |
| **Dependabot** | PAT `read:packages` (in `registries:`) | Dependabot **never** uses `GITHUB_TOKEN` for private feeds |
| Each dev machine | PAT `read:packages`, one-time | `dotnet nuget add source … --password <PAT>` |

One classic PAT with `read:packages` covers all three consumer rows. That single PAT is the
entire "tax" of moving off committed DLLs (which needed no auth and worked offline).

---

## Build plan — phased

| Phase | Repo | Work | Auth | State |
|---|---|---|---|---|
| **0 — Ratify** | server | [ADR 0014](../design/adr/0014-contract-leaf-split-api-and-contract.md): the `Api`/`Contract` role split, dependency rules, "only `*.Api` published." | — | ✅ done |
| **1 — Scaffold** | server | `Projects`/`Inbox` → `root / Api`; `src/Features/Directory.Build.props` (parent-chained import, `Api/**;Contract/**` exclude, `IsPackable=false`); clean build, no double-compile. | — | ✅ done |
| **2 — Rollup** | server | `src/Api/ABox.Api.csproj` (wildcard + `BundleApiDlls` + MinVer). `dotnet pack` → `lib/net10.0/` holds only the `*.Api.dll` leaves, `<dependencies>` empty. | — | ✅ done |
| **3 — Enforcement** | server | Structure rules **Only the Api rollup is packable** + **Every Api leaf is a self-contained bundle input** (placement/wildcard + no-deps); Arch bands + cross-feature channel updated to the two-leaf model; Meta parity green. | — | ✅ done |
| **4 — Publish infra** | server | MinVer in the rollup; `.github/workflows/publish-contracts.yml` (tag `v*` → pack → push). **Cutting `v1.0.0` + confirming the package + setting visibility is the owner's act.** | `GITHUB_TOKEN` | ⏳ workflow in; first tag pending owner |
| **5 — Migrate the rest** | server | All six `*.Contracts` reclassified: Projects/Inbox → `Api`; Git/Tasks/Flows/Decisions → `Contract` (code-derived — see classification below). | — | ✅ done |
| **— Dry run** | both | Publish `v0.1.0`, add the source on one dev box, `dotnet restore`, confirm DLLs resolve — **before** deleting the old DLL-sync path. | one PAT | ⏳ owner/client |
| **6 — Client cutover** | client | `nuget.config` + `PackageReference` + lockfile + `dependabot.yml`; delete `tools/sync-contracts.ps1`, `lib/contracts/*.dll`, `manifest.json`; retire hand-mirrored Inbox stubs (they ride `ABox.Api` now). | `PACKAGES_READ_PAT` | ⏳ client repo |

Phases 0–5 touched build config + CI (protected surface) → landing via PR. The remaining ⏳ rows are
owner/cross-repo acts the bot can't perform: tagging a release, pushing to the feed, and the client repo.

### Classification applied (Phase 5, code-derived)

| Feature | Consumers in-repo | Role | Leaf |
|---|---|---|---|
| Projects | client (`/projects`) | Api | `ABox.Projects.Api` |
| Inbox | client (`/inbox`) | Api | `ABox.Inbox.Api` |
| Git | `Tasks.Create`, `Domain.Git` (cross-feature) | Contract | `ABox.Git.Contract` |
| Tasks, Flows, Decisions | own feature only (intra) | Contract | `ABox.<F>.Contract` |

Only Projects + Inbox are client-facing (per `server-handoff.md`), so only they get an `Api` leaf and
reach the package; the rest are internal `Contract` leaves. Revisit a feature's role the moment a client
endpoint for it appears — move its DTOs into a new `Api` leaf and they ship automatically.

---

## Decisions locked

| # | Decision | Rationale |
|---|---|---|
| **D1** | Three roles per feature: `Foo` / `Foo.Api` / `Foo.Contract`. **Only `*.Api` is published.** | seam = file location, not curation; automation + enforcement |
| **D2** | One shared package, **`ABox.Api`**, bundling all `*.Api` DLLs. | "Contracts" name now means the internal role; published unit is `Api` |
| **D3** | Discovery = **path+name wildcard** (`Features/*/Api/*.Api.csproj`). | pure MSBuild, zero-touch; the Phase-3 test forces the convention |
| **D4** | Nested layout (root is a project) with auto-exclude of `Api/**;Contract/**`. | matches mental model; exclude is one-time convention |
| **D5** | Feed = **GitHub Packages** (private, free); publish tag-driven via MinVer; fetch via Dependabot PR. | both repos on GitHub; deliberate releases + reviewed pulls |
| **D6** | `*.Api` assemblies stay **dependency-free**. | keeps the rollup self-contained (transitive-dep caveat) |

## Resolved during the build

- **Does any cross-feature type also need to reach the client?** **None today** — the client touches
  only Projects + Inbox HTTP DTOs, so only those became `Api`; no type needed to be in both roles. If
  one ever does, it lives in `Api` (the feature reads it from there) — `Contract` never ships.
- **Existing `*.Contracts` rename** — all six migrated in one coherent change (a half-migrated tree
  can't keep the harness green, since the placement/Arch rules swap from one leaf to two atomically).

## Owner / cross-repo follow-ups (the bot cannot do these)

- **Cut the first tag** (`git tag v1.0.0 && git push --tags`) to fire `publish-contracts.yml`, then
  confirm `ABox.Api` appears under the account's Packages and set its visibility.
- **Mint one `read:packages` PAT** for the three consumer rows (client CI, Dependabot, dev machines).
- **Phase 6 in the client repo** + the dry-run before deleting the old DLL-sync path.
- **(Optional)** add `src/Api/**` to `governance/protected-paths` if the published surface should
  require code-owner review like the rest of the enforcement surface.

---

## Sources

- Shared contract assembly is the documented best practice for Blazor + ASP.NET Core when both
  ends are .NET: https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models
- Multiple project DLLs in one package (the `TargetsForTfmSpecificBuildOutput` +
  `BuildOutputInPackage` recipe): https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package
- Publishing NuGet to GitHub Packages: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry
- MinVer (git-tag-driven versioning): https://github.com/adamralph/minver
- Dependabot for NuGet private registries: https://docs.github.com/en/code-security/dependabot/working-with-dependabot/configuring-access-to-private-registries-for-dependabot
