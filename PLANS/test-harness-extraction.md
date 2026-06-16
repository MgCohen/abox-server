# Extracting the test harness to another repo (target: abox-client)

**Status:** 🟡 guide, not yet executed. A step-by-step for lifting this repo's *test
system* — the Rulebook engine, the Meta self-suite, the judge, and the authoring
convention — into another .NET repo. Written generically; **abox-client** (the Blazor
client) is the first target, with its specifics called out inline.

## The mental model — three layers

The system was built as a generic **engine** that *hosts* project-specific **content**.
Extraction is "fork the engine, re-parameterize one token, re-author the content" — not
"install a NuGet" (the engine reads the repo's own source tree on purpose, which is
zero-wiring in-repo and a copy step across repos). Three buckets:

| Layer | What | On extraction |
|---|---|---|
| **Engine** | the Rulebook mechanism, parity, Meta guards, the judge | **moves verbatim** (rename one token) |
| **Seams** | the `<Prefix>`/marker that the engine bakes in | **find/replace** (≈4 spots) |
| **Content** | the actual Rules, doubles, layer model, home folders | **stripped + re-authored** per project |

> Portability note: the *most* portable part is **language-agnostic** — the judge
> (`.claude/`), the `authoring.md` craft rules, and the Rulebook-as-markdown idea would
> work in a Python/TS repo. The `ParityGuard`/ArchUnitNET/xUnit code is the .NET-specific
> shell. abox-client is .NET, so the whole thing transfers.

## Inventory — what moves, what's stripped

### Move verbatim (engine — only the prefix/marker changes)

- `tests/Harness/` — `Rule.cs`, `ParityGuard.cs`, `RulebookFormat.cs`, `TestMarkers.cs`,
  `Report.cs`, `TestTypes.cs`, `RepoTree.cs`, `README.md`, `authoring.md`, the `.csproj`
  (engine deps: **only `xunit`**).
- `tests/Meta/` — `Tests/{ParityTests,TaxonomyTests,RulebookFormatTests}.cs`,
  `Rulebook/{template.md,rules.md}`, `README.md`, the `.csproj`.
- `tests/Tests/SuiteAnchor.cs` — the public handle Meta reflects over.
- `tests/Tests/<csproj>` — the host project (its `src/**` glob + the package refs).
- `tests/README.md`, `tests/Tests/README.md` — genericize the few repo-named examples.
- Root: `Directory.Build.props` (net10 / nullable / warnings-as-errors), the `.editorconfig`
  `[src/**.cs]` + `[tests/**.cs]` IDE0130 blocks, and the `*.slnx` marker (renamed).

### Move the shape, empty the content (per kept type)

For each test type you keep: copy `<Type>/Rulebook/template.md` **as-is** (the header shape +
`## Criteria` are reusable), but **empty `<Type>/Rulebook/rules.md` to a one-Rule starter**,
delete the product tests under `<Type>/Tests/` (leave one starter), and delete the product
doubles under `<Type>/Support/`.

### Re-author per project — do NOT copy

| File / area | Why it's project-specific | abox-client action |
|---|---|---|
| every `rules.md` (the Rules) | they encode *this* product's guarantees | author the client's own |
| `tests/Tests/Support/*` (`FlowHarness`, `ScriptedProvider`, `StubFlow`, `WireApp`, `TempGitRepo`, …) | ABox domain (flows/agents/CLI) | replace with client doubles (e.g. bUnit `TestContext`, an `HttpClient` fake) |
| `Arch/Support/ArchitectureModel.cs` | **is** the project's layer graph | rewrite for the client's layers (Pages → Components → Services → ApiClient) |
| `Structure/Support/HomeFolders.cs` + `SourceTree.cs` | the agreed folder model | rewrite for the client's `src/` shape |
| `Live/Support/LiveFactAttribute.cs` + `RUN_LIVE` gating | claude/codex CLI smoke | **drop** (no CLI in the client) |
| `Wire/Support/WireApp.cs` | `WebApplicationFactory<Program>` over the API host | drop or repurpose — see *Type curation* |

## The parameterization — the find/replace list

Exactly one product token plus the solution marker leak into the engine:

| Token (this repo) | Lives in | Replace with |
|---|---|---|
| `ABox.Tests` namespace root | `TestTypes.Namespace` (`"ABox.Tests.{type}.Tests"`), all `namespace` decls, the `<Using>` entries | `<Prefix>.Tests` (e.g. `AboxClient.Tests`) |
| `ABox.*` csproj names + glob | the three `.csproj` filenames, `tests/Tests/<csproj>` `ProjectReference ..\..\src\**\ABox.*.csproj`, the `..\Harness\` + `..\Tests\` paths | `<Prefix>.*` |
| `ABox.slnx` marker | `RepoTree.Marker` const | the client's `*.slnx`/`*.sln` filename |
| `LiveFactAttribute` | `TestMarkers.Names` | remove if the Live type is dropped (harmless if left — matched by name) |

The namespace falls out of the project dir + folders automatically (default `RootNamespace =
<csproj name>`, e.g. project at `tests/Tests/` ⇒ `<Prefix>.Tests`, folder `Unit/Tests` ⇒
`<Prefix>.Tests.Unit.Tests`), so no explicit `RootNamespace` is needed — just name the csproj
`<Prefix>.Tests.csproj`.

## Type curation — which of the six abox-client keeps

`TestTypes.Registered` is the one place this is decided; a folder under `tests/Tests/` that
isn't registered fails the Meta taxonomy guard, so the registry and the folders must agree.

- **Keep, universal:** `Arch`, `Structure`, `Unit`, plus the `Meta` self-suite.
- **Drop:** `Live` (no claude/codex CLI in the client).
- **Decide:** `Wire` (only if the client has its own HTTP/minimal-API surface to smoke;
  a Blazor UI usually doesn't) and `E2E` (keep if there are real client flows to drive
  end-to-end with a scripted backend).
- **Consider adding:** a **`Component`** type for Blazor component tests (bUnit) — stand it
  up via `Harness/README.md § Standing up a new test type` (new `<Type>/{Rulebook,Tests,Support}/`,
  fill `template.md` + `rules.md`, add to `TestTypes.Registered`, write ≥1 Rule). The engine
  needs no change to gain a type.

## The judge bundle (.claude — transfer as a unit)

Language-agnostic; copy wholesale and it works:

- `.claude/agents/judge.md` (persona), `.claude/workflows/judge.js` (the typed schema),
- `.claude/commands/judge.md`, `judge-rulebook.md`, `judge-authoring.md` (the adapters),
- `.claude/skills/test-rulebook/SKILL.md` (the procedure — genericize the couple of repo names),
- optionally `.claude/workflows/how-to-create-an-agent.md` (to extend the judge later).

`/judge` and `/judge-authoring` resolve a test → its Rulebook / `authoring.md` by path, so they
need the same `tests/Tests/<Type>/Rulebook/` + `tests/Harness/authoring.md` layout — which the
copy preserves.

## Step-by-step

1. **Copy the skeleton** into the client repo at the same paths: all of *Move verbatim* + the
   *shape* of each kept type (`<Type>/{Rulebook,Tests,Support}/`).
2. **Rename the token + marker** (the find/replace table). Rename the three csprojs to
   `<Prefix>.Tests*.csproj`; point `RepoTree.Marker` at the client's solution file.
3. **Curate types:** edit `TestTypes.Registered` to the kept set; delete the dropped type
   folders (`Live`, maybe `Wire`/`E2E`); trim `TestMarkers.Names` if `Live` is gone.
4. **Empty the Rulebooks:** reduce each kept `rules.md` to one starter Rule; keep every
   `template.md` (shape + `## Criteria`) intact.
5. **Re-point the host csproj:** change the `src/**` glob prefix, drop packages a dropped type
   pulled in (`Microsoft.AspNetCore.Mvc.Testing` + the `AspNetCore` `FrameworkReference` if
   `Wire` goes), keep `xunit` + `Microsoft.NET.Test.Sdk` + `coverlet` + `ArchUnitNET` (for `Arch`),
   add `bunit` if you added `Component`.
6. **Re-author the project content:** the new `ArchitectureModel` (client layer bands +
   allow-graph), `HomeFolders`/`SourceTree` for the client's `src/`, and the kept types'
   `Support/` doubles.
7. **Copy the judge bundle** (`.claude/` assets above).
8. **Build + verify the empty skeleton is green:** `dotnet build` warning-free, and the Meta
   self-suite's three guards (Parity / Taxonomy / Rulebook-format) pass over the starter
   Rulebooks — that proves the engine is wired before any real test exists.
9. **Start authoring** real Rules + tests per `tests/Tests/README.md` and the `test-rulebook`
   skill; grade bodies with `/judge-authoring`.

## Done-when

- `dotnet build <client>.slnx` warning-free; `dotnet test` green.
- Meta self-suite green on the starter skeleton (parity holds, every `tests/Tests/` folder is a
  registered type, every Rulebook is well-formed with `## Criteria`).
- One starter test per kept type, each citing a Rule; `IDE0130` enforcing namespace=folder.
- `/judge` and `/judge-authoring` runnable against a client test file.

## Effort & the package question

The engine lift is small (≈a day: tokenize ~4 spots, strip content, keep skeletons). The real
cost is re-authoring *content* — the Arch layer model, home folders, doubles, and the Rules —
which is inherent to the new project, not a tax the harness imposes.

**Stay copy-fork for now.** Per this repo's YAGNI rule, only after a *second* consumer is really
using it would extracting the pure engine (`Rule`, `ParityGuard`, `RulebookFormat`, the Meta guard
logic) into a shared `Abox.TestKit` NuGet pay off — leaving `RepoTree`'s marker, the `src/**`
glob, and the Rulebooks as the per-repo shell, and shipping the judge as a standalone `.claude/`
bundle. Packaging before that second use is the speculative abstraction the codebase warns against;
abox-client is that second use, so this guide is the moment to *learn* the seam — not yet to package it.
