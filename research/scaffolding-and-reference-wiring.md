---
type: research
status: reference
tags: [#scaffolding, #dotnet-new, #templates, #project-references, #module-registration, #msbuild]
sources:
  - https://github.com/jasontaylordev/CleanArchitecture
  - https://github.com/ardalis/CleanArchitecture
  - https://github.com/ardalis/modulith
  - https://github.com/abpframework/abp
  - https://github.com/microsoft/MSBuildSdks
related: [[architecture-proposal]]
---

# Scaffolding & reference-wiring tech — how reference DDD/CA/VSA repos do it

> **Why this exists.** We converged on a per-use-case-assembly structure
> (`PLANS/architecture-proposal.md`, `status: converged`) and asked: can adding a
> feature *scaffold itself* — folders, assemblies, dependencies, base-class stubs,
> and wiring into whatever parent needs the new assembly? This records the **tech**
> the well-known templates actually use for **scaffolding, references, and
> dependency wiring**. Our own structural standards are **not** in scope here and
> are unchanged — this is a toolbox survey, not an architecture revisit.

Date: 2026-06-09. We are on **.NET 10** (matters: the native auto-reference path
below shipped in SDK 9.0.100-preview.7, so it is fully available to us).

---

## 1. The four reference repos, by how they handle scaffolding + wiring

| Repo | Scaffold unit | Creates new assemblies? | Reference wiring | Service registration |
|---|---|---|---|---|
| **Jason Taylor — Clean Architecture** | use case | No — files into one existing `Application` project | none needed (no new project) | n/a (handlers auto-found by MediatR assembly scan) |
| **Ardalis — Clean Architecture** | solution (once) | No — fixed `Core`/`UseCases`/`Infrastructure`/`Web` | none after initial gen | n/a |
| **ABP** (`abp new module` / `add-module`, ABP Suite) | module (coarse) | Yes — multi-project | CLI auto-adds project references, swaps package→project refs, edits `DbContext`/module deps | module dependency graph (`[DependsOn(...)]`) |
| **Ardalis — Modulith** | module | Yes — 3 projects (`X` + `X.Contracts` + `X.Tests`) | `dotnet add reference` (manual) **or** native-auto via `modulith-proj` on SDK 9.0.100-preview.7+ | assembly scan: `DiscoverAndRegisterModules()` + per-module `{Module}ModuleServiceRegistrar` |

**One-line read of the field:** scaffolding is universally `dotnet new`; the part
everyone treats as *the hard step* is **adding the new project's reference to its
parent**, and the modern answer is the SDK's native auto-reference rather than
hand-edited csproj or wildcards.

---

## 2. `dotnet new` template mechanics (the shared toolbox)

All of it is the [dotnet/templating](https://github.com/dotnet/templating) engine,
driven by a `.template.config/template.json`.

### 2.1 Item template vs project template
- **`"tags": { "type": "item" }`** — drops files into an **existing** project. No
  new assembly, no solution edit, no parent wiring. (JT's `ca-usecase`.)
- **`"type": "project"`** — emits one or more **new** `.csproj`. Needs solution +
  parent-reference wiring (§3). (Modulith's module template, ABP modules.)

### 2.2 Symbol replacement + computed/generated symbols
JT's `ca-usecase` is the clean worked example:
- `sourceName` is the literal replaced everywhere (filenames + contents).
- **`bind` symbol** reads MSBuild props from the target project, e.g.
  `"binding": "msbuild:RootNamespace"` — so the generated namespace matches where
  you scaffolded.
- **`generated` + `join` generator** composes a namespace from parts and `replaces`
  a placeholder string:
  ```json
  "ComputedNamespaceWithFeature": {
    "type": "generated", "generator": "join",
    "parameters": { "symbols": [
      {"type":"ref","value":"RootNamespace"},
      {"type":"ref","value":"parentNamespace"},
      {"type":"ref","value":"featureName"}],
      "separator": ".", "removeEmptyValues": true },
    "replaces": "CleanArchitecture.Application.FeatureName"
  }
  ```
- **`choice` parameter + `computed` flags** branch the output:
  `useCaseType ∈ {command, query}` → `createCommand`/`createQuery`.
- **`fileRename`** maps a placeholder folder name (`FeatureName`) to the parameter
  value, so files land in `FeatureName/Commands/...`.

### 2.3 Conditional inclusion of whole trees — `sources.modifiers` + `exclude`/`rename`
How a single template emits different shapes. JT use-case template excludes the
unused branch:
```json
"sources": [{ "modifiers": [
  { "condition": "(createCommand)", "exclude": ["FeatureName/Queries/**/*"] },
  { "condition": "(createQuery)",   "exclude": ["FeatureName/Commands/**/*"] }
]}]
```
JT solution template uses the same mechanism to drop entire projects per choice:
```json
{ "condition": "(UseApiOnly)", "exclude": [
    "src/Web/ClientApp/**", "src/Web/ClientApp-React/**",
    "tests/Web.AcceptanceTests/**" ] }
{ "condition": "(UseReact)", "exclude": ["src/Web/ClientApp/**"],
  "rename": { "ClientApp-React": "ClientApp" } }
```
So: **one template, conditional projects/files**, driven by `choice` parameters.

### 2.4 Post-actions
The engine's built-in [post-actions](https://github.com/dotnet/templating/blob/main/docs/Post-Action-Registry.md):
- **Add projects to solution** — action ID `D396686C-DE0E-4DE6-906D-291CD29FC5DE`,
  via `primaryOutputs` + `primaryOutputIndexes`, optional `solutionFolder`
  (SDK 5.0.200+). Known rough edge: named-folder output paths can confuse the
  solution-add ([templating#1580](https://github.com/dotnet/templating/issues/1580)).
- Restore, run-script, print-instructions are the other common ones.

---

## 3. Wiring the new assembly into its parent — the actual hard part

Four observed techniques, roughly worst→best for our case:

1. **Manual `dotnet add reference`** (Modulith pre-9 SDK, ABP under the hood):
   ```bash
   dotnet add eShop.Web/eShop.Web.csproj reference \
     Shipments/eShop.Shipments/eShop.Shipments.csproj
   ```
   Explicit, reliable, but the template can't do it alone — needs a wrapper
   script/CLI step.

2. **Native auto project-reference (SDK 9.0.100-preview.7+ → available on .NET 10).**
   Modulith's `modulith-proj` template injects the reference into a named existing
   project automatically:
   ```bash
   dotnet new modulith-proj --ModuleName Shipments \
     --existingProject eShop.Web/eShop.Web.csproj
   ```
   This is the modern replacement for hand-editing the parent csproj.

3. **Wildcard `ProjectReference`** (e.g. `Include="..\Features\**\*.Module.csproj"`):
   works at CLI/MSBuild, but **Visual Studio rewrites wildcards into explicit items**
   when it touches the file (`ReplaceWildcardsInProjectItems`), silently defeating
   auto-include. Rider tolerates it better. *No surveyed repo relies on this for
   ProjectReference* — they use explicit refs. Flagged as a footgun, not adopted.

4. **`Microsoft.Build.Traversal` `dirs.proj`** ([MSBuildSdks](https://github.com/microsoft/MSBuildSdks/blob/main/src/Traversal/README.md)):
   a wildcard traversal project exists specifically so new projects **build** with
   zero edits, as a solution replacement for build. IDEs don't rewrite it. Useful
   for the *build-everything* root even if solution membership is still explicit
   (`.slnx` has **no** project-wildcard support yet —
   [sdk#41465](https://github.com/dotnet/sdk/issues/41465)).

---

## 4. Avoiding parent edits entirely — assembly-scan registration

The technique that makes the **composition root never need editing** when a feature
is added. Both module-granular repos use it:

- **Modulith:** `builder.DiscoverAndRegisterModules()` scans loaded assemblies; each
  module ships a static `{Module}ModuleServiceRegistrar` with its `ConfigureServices`.
  Adding the module's project reference is sufficient — the host discovers it.
- **Community modular-monolith templates:** `ModuleRegistrationExtensions` scans for
  `IRegisterModuleServices` implementations and invokes each, "so the host never
  needs to know which modules exist."
- **ABP:** module classes with `[DependsOn(typeof(...))]` form a dependency graph the
  runtime walks; the host depends on the top module only.

Net: a tiny `IModule.Register(services)` convention + a boot-time assembly scan
removes the "call `AddX()` in Host" edit. Reference still has to exist (§3), but the
*registration code* is zero-edit.

---

## 5. The combined pipeline these repos demonstrate

```
dotnet new <template> --add <kind> --with-name <Name> --to <Solution>
  ├─ project/item template            (folders + csprojs + stubs, refs baked-in relative)
  ├─ conditional sources              (modifiers/exclude/rename per choice param)
  ├─ generated/bind symbols           (namespaces match target; sourceName replace)
  ├─ post-action D396686C…            (add new projects to the solution)
  ├─ parent reference                 (native auto-ref on .NET 10  ▸ §3.2)
  └─ assembly-scan registration       (DiscoverAndRegisterModules / IModule  ▸ §4)
                                        → host composition root edited: never
```

## 6. Mapping to our toolbox (tech only)

- `dotnet new` template (project type, multi-csproj) + conditional `sources` is the
  proven way to emit our per-feature folder tree with refs baked in as relative
  paths.
- Solution membership: post-action `D396686C…`.
- Parent reference (`Host`→Module, `Web`→`*.Contracts`): **native auto project-ref**
  is on the table because we're on .NET 10 — preferred over wildcard
  `ProjectReference` (VS-hostile) and over hand-edits.
- Composition stays zero-edit via an `IModule`-style scan (`DiscoverAndRegisterModules`
  pattern), consistent with the proposal's "composition = thin add point" seam.
- `Microsoft.Build.Traversal` is the option for a build-everything root if/when we
  want builds decoupled from `.slnx` membership.

> Our architectural standards (per-use-case assemblies, the reference graph as the
> rulebook, `Domain/Agents` as the walled runtime) are unchanged by this note. This
> is purely the mechanism inventory for *if/when* we build a scaffolder.
