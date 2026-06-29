---
type: research
status: reference
tags: [#backstage, #scaffolder, #software-templates, #golden-paths, #dotnet-new, #llm-glue, #deterministic-structure]
sources:
  - https://backstage.io/docs/features/software-templates/
  - https://backstage.io/docs/features/software-templates/writing-templates/
  - https://github.com/backstage/software-templates
  - https://github.com/JBrejnholt/dotnet-template
related: [[scaffolding-and-reference-wiring]], [[golden-paths]]
---

# Backstage Scaffolder — how it works, where it stops, and the LLM-glue thesis

> **Why this exists.** A standalone, cold-readable record of how Spotify
> Backstage's Software Templates (the "Scaffolder") actually work, demonstrated
> on a real public .NET template down to the shipped file bytes; *why* the
> ecosystem stopped at coarse, day-1 scaffolding instead of fine-grained
> composable templates; and the design thesis for pushing it further with an LLM
> doing the glue. Written for a reader with **zero prior context** on Backstage.
> The closing section connects this to A.Box's own "deterministic structure +
> guided agent" model — that connection is the reason this lives in *this* repo,
> which is otherwise unrelated to Backstage.

Date: 2026-06-28. No code in this repo depends on any of this; it is a concept +
prior-art note, not a spec.

---

## 0. TL;DR

| | |
|---|---|
| **What Backstage is** | An open-source developer-portal platform (built by Spotify, donated to CNCF). Four pillars: **Software Catalog**, **Software Templates / Scaffolder**, **TechDocs**, **Plugins**. |
| **What the Scaffolder does** | Run a template once → it renders a **whole new runnable component** (a service/repo), publishes it to GitHub, and registers it in the catalog. |
| **How a template is built** | A `template.yaml` = a **form** (`parameters`) + an ordered list of **actions** (`steps`). The key step `fetch:template` copies a `skeleton/` directory of real source files, substituting `${{ values.x }}` placeholders. |
| **Its altitude** | **Coarse, day-1, golden-path** tooling. It bootstraps a compiling, runnable shell with sample code; **you hand-write the real business logic afterward.** |
| **Where it stops** | It does **not** do fine-grained "template a model → template a service → glue them" composition. The inter-template **glue** (wiring new pieces into existing files) is context-dependent and untemplatable by blind text substitution — so the ecosystem chose the **monolithic, run-once** template instead. |
| **The LLM thesis** | An LLM can write that glue (it reads both sides and authors the seam). But this **inverts the roles**: the template demotes from *the engine that produces code* to *a guardrail/spec that constrains an agent*. Value survives only if the seam stays **small, contract-bounded, and machine-verifiable** (build + tests). |

---

## 1. What Backstage is

**Backstage** is a platform for building **internal developer portals (IDPs)**.
Spotify built it to fight tooling fragmentation across hundreds of autonomous
teams; it was open-sourced in 2020 and is now a CNCF project. A company runs its
own Backstage instance as the single front door to "all our software."

Four pillars:

1. **Software Catalog** — a registry of all components/services/APIs/resources,
   each described by a `catalog-info.yaml` file living in its repo. Makes
   software discoverable and ownership explicit.
2. **Software Templates (the "Scaffolder")** — the subject of this doc: the
   "create new X" button that scaffolds a new component from a template.
3. **TechDocs** — docs-as-code (Markdown in the repo) rendered in the portal.
4. **Plugins** — everything else (CI views, cloud cost, k8s, etc.) is a plugin.

This doc is **only about the Scaffolder.**

---

## 2. The Scaffolder model

A template is one file, `template.yaml`, plus a `skeleton/` directory.

```
my-template/
├─ template.yaml        # the form + the steps
└─ skeleton/            # real source files, with ${{ values.x }} placeholders
   ├─ catalog-info.yaml
   ├─ src/...
   └─ ...
```

### 2.1 `template.yaml` = a form + ordered actions

- **`parameters`** — declares the **form** the developer fills in the portal UI
  (text fields, owner pickers, repo pickers). This is a JSON-Schema-ish block;
  Backstage renders it as a wizard.
- **`steps`** — an **ordered list of actions** run after the form is submitted.
  The three canonical actions:

| Action | What it does |
|---|---|
| `fetch:template` | Copy the `skeleton/` dir into a working dir, substituting `${{ values.* }}` placeholders in **both file contents and file/dir names** (Nunjucks/Jinja2-style templating). |
| `publish:github` | Create a new GitHub repo and push the rendered output. |
| `catalog:register` | Register the new repo's `catalog-info.yaml` into the Backstage catalog. |

There are many more actions (run `dotnet new`, open a PR, publish to GitLab,
etc.), but `fetch → publish → register` is the spine.

### 2.2 The skeleton and placeholder substitution

> *Any occurrence of `${{ values.name }}` within a file in the skeleton directory
> — or in a file/directory name — is replaced with the form value in the
> rendered codebase.* (Backstage "Writing Templates" docs.)

Files you want copied **literally** (no rendering — e.g. GitHub workflow files
that themselves use `${{ }}`) are listed under `copyWithoutRender`.

---

## 3. Worked example — a real, complete .NET template

Public repo: **`JBrejnholt/dotnet-template`** — "Backstage template for .Net 7
code." It is a textbook example: a `dotnet new webapi` output with the project
identifiers tokenized. Everything below is verbatim from that repo (fetched
2026-06-28).

### 3.1 The form — `template.yaml` `parameters`

```yaml
- title: Provide some simple information
  required: [component_id, owner, project_name]
  properties:
    component_id:
      title: Name
      type: string
      description: Unique name of the component - which will be used as the solution file name
      ui:field: EntityNamePicker
    description:
      title: Description
      type: string
      description: Help others understand what this web api is for.
    owner:
      title: Owner
      type: string
      ui:field: OwnerPicker
      ui:options: { allowedKinds: [Group] }
    project_name:
      title: Name
      type: string
      description: Name of the project
- title: Choose a location
  required: [repoUrl]
  properties:
    repoUrl:
      title: Repository Location
      type: string
      ui:field: RepoUrlPicker
      ui:options: { allowedHosts: [github.com] }
```

### 3.2 The steps — `template.yaml` `steps`

```yaml
- id: template
  name: Fetch Skeleton + Template
  action: fetch:template
  input:
    url: ./skeleton
    copyWithoutRender:
      - .github/workflows/*          # copied literally, not rendered
    values:
      component_id: ${{ parameters.component_id }}
      description: ${{ parameters.description }}
      artifact_id: ${{ parameters.component_id }}
      project_name: ${{ parameters.project_name }}
      owner: ${{ parameters.owner }}
      destination: ${{ parameters.repoUrl | parseRepoUrl }}
      http_port: 8080
- id: publish
  name: Publish
  action: publish:github
  input:
    allowedHosts: ["github.com"]
    description: This is ${{ parameters.component_id }}
    repoUrl: ${{ parameters.repoUrl }}
- id: register
  name: Register
  action: catalog:register
  input:
    repoContentsUrl: ${{ steps.publish.output.repoContentsUrl }}
    catalogInfoPath: "/catalog-info.yaml"
```

### 3.3 The skeleton tree (note the templated *paths*)

```
skeleton/
├─ catalog-info.yaml
├─ README.md   docs/index.md   mkdocs.yml
└─ src/
   ├─ ${{values.artifact_id}}.sln
   ├─ .dockerignore
   └─ ${{values.project_name}}/
      ├─ ${{values.project_name}}.csproj
      ├─ Program.cs
      ├─ Dockerfile
      ├─ appsettings.json   appsettings.Development.json
      ├─ Properties/launchSettings.json
      ├─ WeatherForecast.cs                        # ⚠ stock sample entity
      └─ Controllers/WeatherForecastController.cs   # ⚠ stock sample endpoint
```

### 3.4 The skeleton file bodies — verbatim (this is "the template body")

The striking finding: **almost nothing in the bodies is templated.** Only the
namespace line in each `.cs` file. The sample logic is literal.

**`skeleton/src/${{values.project_name}}/WeatherForecast.cs`**
```csharp
namespace ${{values.project_name}};        // ← the ONLY templated token

public class WeatherForecast
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public string? Summary { get; set; }
}
```

**`skeleton/src/${{values.project_name}}/Controllers/WeatherForecastController.cs`**
```csharp
using Microsoft.AspNetCore.Mvc;

namespace ${{values.project_name}}.Controllers;   // ← the ONLY templated token

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(ILogger<WeatherForecastController> logger)
    {
        _logger = logger;
    }

    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        })
        .ToArray();
    }
}
```

**`skeleton/src/${{values.project_name}}/Program.cs`** — **zero** placeholders:
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

**`skeleton/src/${{values.project_name}}/${{values.project_name}}.csproj`** —
the placeholder is in the **filename**, the body is fixed:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="7.0.0" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.4.0" />
  </ItemGroup>
</Project>
```

**`skeleton/catalog-info.yaml`** — by contrast this *is* heavily templated,
because it's Backstage **metadata**, not app code (uses Nunjucks `| dump` and
`{%- if %}`):
```yaml
apiVersion: backstage.io/v1alpha1
kind: Component
metadata:
  name: ${{values.component_id | dump}}
  {%- if values.description %}
  description: ${{values.description | dump}}
  {%- endif %}
  annotations:
    github.com/project-slug: ${{values.destination.owner + "/" + values.destination.repo}}
    backstage.io/techdocs-ref: dir:.
spec:
  type: service
  lifecycle: experimental
  owner: ${{values.owner | dump}}
```

### 3.5 Final rendered state for a concrete scenario

Scenario used as the running example: *"add a feature to favorite artists, save
to a list, and view saved favorites."* Form values:

| Field | Value |
|---|---|
| `component_id` | `spotify-favorites` |
| `project_name` | `SpotifyFavorites` |
| `owner` | `group:music-team` |
| `repoUrl` | `github.com?owner=spotify&repo=spotify-favorites` |

Rendered repo:
```
spotify-favorites/
├─ catalog-info.yaml          # name: "spotify-favorites", owner: "group:music-team", slug: spotify/spotify-favorites
└─ src/
   ├─ SpotifyFavorites.sln
   └─ SpotifyFavorites/
      ├─ SpotifyFavorites.csproj   Program.cs   Dockerfile
      ├─ WeatherForecast.cs                       # ⚠ STILL weather — namespace SpotifyFavorites;
      └─ Controllers/WeatherForecastController.cs  # ⚠ STILL weather — nothing about artists
```

The output is a **runnable weather-forecast API that happens to be named
`SpotifyFavorites`.** It knows nothing about artists.

---

## 4. The three boundaries (the mental model)

Every scaffolded file falls into exactly one bucket:

| Bucket | Definition | In the example |
|---|---|---|
| **Templated var** | Substituted from the form. | Project/solution/namespace names, repo slug, owner, `catalog-info.yaml` metadata. |
| **Fixed skeleton** | Always identical; exists so the output **compiles and runs on day one**. | `Program.cs`, `Dockerfile`, csproj refs, **and the whole `WeatherForecast` model + controller** (the sample endpoint). |
| **Hand-written next** | Not produced by the scaffolder at all; the actual point of the service. | The `FavoriteArtist` model, `FavoritesController` (`POST /favorites`, `GET /favorites`), `DbContext`, migration, storage. |

**Key insight:** the `WeatherForecast` sample surviving into the final output is
the *proof that the scaffolder is domain-agnostic*. It never tried to understand
"favorites."

---

## 5. Why it returned `WeatherForecast`

Not a bug, not laziness specific to this template — it's the **norm**:

1. `WeatherForecast` is the **default sample** that `dotnet new webapi` emits.
2. The template author ran `dotnet new webapi` once, dropped the output into
   `skeleton/`, and find-replaced the project name with `${{values.project_name}}`.
3. They never edited the sample body.

So "the template" is **Microsoft's stock demo with the identifiers tokenized.**
Most Backstage skeletons across languages are exactly this shape: a `<tool> new`
output with names turned into placeholders. The scaffolder's contract ends at
*"a correctly-named, compiling, runnable service"* — turning `WeatherForecast`
into `FavoriteArtist` is hand-work it deliberately never attempts.

---

## 6. Why fine-grained / composable templating never happened

The natural wish: instead of one coarse monolith, have small templates —
"a model," "a CRUD endpoint," "a service shell" — and **compose** them.
Backstage (and the ecosystem) deliberately did **not** go there. Reason:

**Templating composes by blind text substitution.** To glue template B (an
entity) into template A (a service), B's output must mutate files A already owns:
register the `DbContext` in `Program.cs`, add the route, edit the `.csproj`, add
an EF migration. Pre-LLM, the only ways to encode that were:

- **Fragile anchor markers** — A ships `// <scaffold:services>` comments and B
  greps for them to inject text. Breaks the instant a human edits the file
  between scaffolds (which always happens).
- **The monolith** — make one big template that already contains everything, so
  nothing ever needs gluing.

The ecosystem chose the **monolith** (coarse, run-once, day-1 golden path)
because the glue is **context-dependent**: it depends on the *current* state of
files the template did not write. That is precisely what text substitution cannot
do. The "untemplatable glue" is the structural reason fine-grained composition
didn't take off.

---

## 7. The LLM-glue thesis (where this gets interesting for A.Box)

An LLM *can* write the glue — it reads both sides and authors the seam natively.
But the important point is **it inverts the roles**, it does not merely "extend"
templating.

### 7.1 Role inversion

| Concern | Pre-LLM | LLM-glued |
|---|---|---|
| Skeleton / structure | template = **the mechanism** | template = **a guardrail / spec** |
| Domain fill | hand-written | LLM |
| Glue between pieces | impossible → forced monolith | **LLM, reading live file state** |

The template **demotes** from "the thing that produces code" to "the thing that
*constrains* what an agent produces." Consequence: a template no longer needs to
be a complete runnable monolith. It can be a **socket** — a model + an empty
controller + a documented wiring contract — and the LLM closes the circuit.

### 7.2 The non-negotiable constraint

The trap: if the LLM glues **freely**, you've just rebuilt "ask the LLM to write
the whole service" — same nondeterminism, same drift, none of the guarantees that
made scaffolding valuable. The value lives **entirely** in how hard the scaffold
constrains the glue:

- **Scaffold owns the invariants** (deterministic, guaranteed): naming, project
  structure, DI conventions, the *shape* of `DbContext` registration, the
  migration command, the catalog entry.
- **LLM owns only the bounded seam**: "given this new `FavoriteArtist` model and
  this *existing* `Program.cs`, wire it the way the convention dictates" — and the
  result is **verified by a build + tests**, never trusted on faith.

LLM-glue beats the monolith **only when the seam is small, contract-bounded, and
machine-verifiable.** Let the agent roam past that and it degrades into freehand
codegen.

### 7.3 The favorite-artists case, glued

```
Template A (service socket)   → drops SpotifyFavorites/ skeleton (no WeatherForecast)
Template B (entity+endpoint)  → drops FavoriteArtist.cs + FavoritesController.cs (stubbed)
LLM glue step                 → reads Program.cs + AppDbContext + .csproj AS THEY NOW ARE,
                                registers DbSet<FavoriteArtist>, the controller route, the
                                migration; runs `dotnet build` + `ef migrations add`,
                                self-corrects until green.
```

The hand-authored target the LLM is steering toward:
```csharp
// FavoriteArtist.cs
public record FavoriteArtist(Guid UserId, string ArtistId, DateTime SavedAt);

// Controllers/FavoritesController.cs
[ApiController]
[Route("favorites")]
public class FavoritesController : ControllerBase
{
    [HttpPost] public IActionResult Add([FromBody] string artistId) { /* TODO */ }
    [HttpGet]  public IEnumerable<FavoriteArtist> List()            { /* TODO */ }
}
```

B no longer has to ship a whole runnable service to be useful; A no longer has to
anticipate B. The LLM holds both contracts in context and writes the ~6 lines of
seam **neither template could own.**

### 7.4 Connection to A.Box

This is **A.Box's own thesis applied to scaffolding**: *deterministic structure
+ an agent that fills the guided gaps, verified against the structure's
invariants.* Backstage-style templates are simply **one more deterministic
structure to wrap an agent in** — analogous to how A.Box wraps agents in
workflows / document-spec enforcement / guardrails.

The interesting design question is **not** "can an LLM glue templates" (it can).
It is: **what is the smallest contract a template must expose so the glue is
verifiable instead of vibes?** That contract — the socket's wiring spec plus a
verifier — *is* the product surface. A coarse run-once template gives that away
for free by being a monolith; a composable LLM-glued system has to make the
contract explicit and checkable, which is exactly the kind of structure A.Box
exists to provide.

---

## 8. Sources

- Backstage — Software Templates overview: https://backstage.io/docs/features/software-templates/
- Backstage — Writing Templates (placeholder/skeleton semantics): https://backstage.io/docs/features/software-templates/writing-templates/
- Official template collection: https://github.com/backstage/software-templates
- Worked .NET example (all file bytes in §3): https://github.com/JBrejnholt/dotnet-template
