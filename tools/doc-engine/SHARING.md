# Sharing the doc catalog with the render repo

How the **catalog** (the block/doctype/kind vocabulary) crosses from this server
repo to the client/render repo (`abox-client`), so the client can build renders
that match what the engine produces.

> **Transport reconciled with [`PLANS/contract-publishing.md`](../../PLANS/contract-publishing.md).**
> The server no longer hand-copies DLLs to the client — it publishes its
> client-facing surface as the versioned **`ABox.Api`** NuGet package (tag-driven
> via MinVer → GitHub Packages → Dependabot PR). The catalog rides **that same
> loop** as a packaged data asset; it is not copied by hand.

## What is shared — and what is not

| Artifact | Source | Shared? | How |
|---|---|---|---|
| **Catalog / schema** | `kinds/` + `blocks/` + `doctypes/` | **Yes** | one generated JSON, shipped as a versioned asset on the `ABox.Api` package loop |
| **Document instances** | a document's home `*.md` (e.g. a plan, an ADR) | **No** | runtime payload — plain markdown, sent per-document over HTTP |

The schema is a **vocabulary**, versioned with the package and updated when it
changes. Instances are **content**, produced per-run and delivered as the
already-human-readable markdown the engine emits — never packaged like a contract.

Rule of thumb: **share the data, not the engine.** The client never takes
`ABox.DocEngine.dll`. It takes one data file describing the vocabulary and writes
its own renderers against it.

> **The catalog is a superset.** It carries every doctype the engine knows,
> including **internal-tooling** ones the client never renders — the repo models
> its own test Rulebooks as `rulebook` / `test-template` doctypes (guarded by the
> `Docs` test type). The client builds renderers for the block types it
> **actually receives instances of**, doctype by doctype — it does not enumerate
> the whole catalog. Product doctypes (`feature-plan`, `research`, …) are render
> targets; the tooling doctypes are not.

## Why a file, not a DLL

The DLLs in the `ABox.Api` package are compiled **behavior** (the wire DTO types).
The catalog is **data** — the engine itself "names no kind"; blocks and doctypes
are pure YAML. Shipping the engine DLL would hand the client a *parser* it can't
render with (and drag YamlDotNet + the CLI into a render path). The client needs
the *vocabulary*, then owns presentation. So we ship the vocabulary as one file,
versioned alongside the contracts.

---

## Producer side — what we build (this repo)

### 1. A catalog export command

Built — `docengine catalog --json`:

```
docengine catalog --json --root tools/doc-engine > doc-catalog.json
```

It serializes the already-loaded `kinds` + `blocks` + `doctypes` into one
machine-readable file (`CatalogExport`). No new domain logic — `Catalog` already
loads all of it for the text `catalog` command; this is a serializer over the same
data, normalizing the YAML graph to JSON.

### 2. A version stamp

The top of the file carries a `catalogVersion`. Bump it whenever a block/doctype
field, attr, enum, or grouping changes. The **package** version (MinVer tag) pins
what the client *compiled* against; `catalogVersion` is the in-payload marker that
also lets a **runtime instance** be checked against the client's packaged catalog —
see *Drift* below.

### 3. Ship it embedded in the `ABox.Api` package

The server shares its client-facing surface as one versioned package: a tag `v*`
fires `publish-contracts.yml`, MinVer stamps the version, `dotnet pack
src/Api/ABox.Api.csproj` rolls every `*.Api` DLL into `ABox.Api.<ver>.nupkg`, and
the client pulls it via a Dependabot PR (full loop in
[`contract-publishing.md`](../../PLANS/contract-publishing.md)). The package is
**live** — the client already consumes it. The catalog rides **the same package**
as an **embedded resource**, so it is versioned and fetched by the same automation
with no new package, registry, or Dependabot wiring — never hand-copied.

How it is wired (built):

- `src/Api/doc-catalog.json` is the committed export, embedded into the rollup's
  marker assembly: `<EmbeddedResource Include="doc-catalog.json">` with
  `LogicalName` `ABox.Api.doc-catalog.json`. `IncludeBuildOutput=true` already ships
  `ABox.Api.dll`, so the catalog travels inside it — no `lib/` content-file plumbing,
  and `*.Api` stays dependency-free (a resource is not a `<dependency>`).
- The committed JSON is a **generated artifact**, kept current by a `Docs` test
  ("The shared catalog export is committed and current") that re-runs the export and
  diffs it against the file — a stale catalog fails CI before it can ship. Regenerate
  from `tools/doc-engine`: `dotnet run -- catalog --json > ../../src/Api/doc-catalog.json`.

> **Why embedded, not a content file.** The client is Blazor WASM; reading a
> referenced assembly's manifest resource is reliable there, whereas a copy-to-output
> content file means static-asset plumbing. Embedding also needs no `build/*.targets`
> in the package.

> **Alternative considered — a sibling package `ABox.DocCatalog`.** A second packable
> project + a second client `PackageReference`. Cleaner separation if the catalog's
> cadence ever diverges sharply from the wire DTOs, at the cost of more machinery.
> Rejected for now (one small JSON, one consumer) — revisit if that divergence is real.

### `doc-catalog.json` shape

Each definition is serialized **as-is** (the engine names no kind, so the export
special-cases none) and keyed by its identifier — blocks by `type`, doctypes by
`docType`, kinds by `name`. Abridged real output of `docengine catalog --json`:

```json
{
  "catalogVersion": "1",
  "kinds":    { "block": { "...": "..." }, "doctype": { "...": "..." } },
  "blocks": {
    "phase": {
      "type": "phase",
      "collection": "true",
      "group": "Phases",
      "description": "One ordered, shippable step of the build.",
      "attrs": {
        "status": { "enum": ["todo", "doing", "done", "blocked"], "default": "todo" },
        "caveat": { "type": "string" }
      },
      "body": { "type": "markdown" },
      "rubric": { "reuse-first": "Lead with what the step reuses...", "...": "..." }
    }
  },
  "doctypes": {
    "feature-plan": {
      "docType": "feature-plan",
      "description": "Build or change a feature...",
      "blocks": ["summary", "context", "scope", "decision", "phase", "verification", "open-question"],
      "required": ["summary", "phase", "verification"],
      "attrs": { "status": { "enum": ["draft", "approved"], "default": "draft" } }
    }
  }
}
```

This is the whole contract. Everything the client needs to render — block types,
their attrs/enums, which are collections and under what group header, what a
doctype is composed of — is in this one file.

> **Scalars are strings.** YAML scalars (including bool-looking ones) serialize as
> JSON **strings** — note `"collection": "true"`, not `true`. This mirrors the
> engine's string-centric model; blind bool coercion is deliberately avoided
> because it would corrupt enum values like `[yes, no]`. The client applies the
> same truthiness rule (`true/yes/on/1`) the engine does for the few bool fields.

---

## Consumer side — how the client reads it (`abox-client`)

### 1. Read it from the package assembly

`doc-catalog.json` arrives inside the `ABox.Api` package the client already
references — pulled in by the Dependabot bump that pins the version, not by a hand
copy. It is an embedded resource in `ABox.Api.dll`; read it by logical name:

```csharp
using var s = System.Reflection.Assembly.Load("ABox.Api")
    .GetManifestResourceStream("ABox.Api.doc-catalog.json")!;
var catalog = await JsonSerializer.DeserializeAsync<Catalog>(s);
```

### 2. Load it once at startup

Deserialize the JSON into an in-memory catalog. The package version is the pin;
keep `catalogVersion` to compare against runtime instances (see *Drift*).

### 3. Build a renderer per block type

For each block `type`, write a render component driven by the catalog entry:

- **`body.type`** → how to render the body (`markdown` → markdown component).
- **`attrs`** → which fields to show; `enum` attrs render as a known, fixed set
  (e.g. a `status` chip), so the client can style each value.
- **`labels`** → named sub-fields **inside** the body, each `required` or not
  (e.g. `rule` declares `Why:` required, `Outcome:` optional — the `- **Why:** …`
  lines). Render as distinct labeled fields, not lumped into freeform markdown.
- **`collection` + `group`** → render as a `## Group` section with `### member`
  items; singletons render top-level.
- **`description` / `rubric`** → semantics/tooltips, not pixels — the client owns
  the visual.

### 4. Render an instance against the catalog

When a document instance arrives at runtime (the plain markdown), parse it —
front matter, `## Type` headers, `<!-- id: N -->`, `key: value` attrs — and render
each parsed block with the matching catalog-driven component. The on-disk instance
syntax is built to match the render repo's parser, so this stays a thin mapping.

---

## Drift — the runtime instance can outrun the packaged catalog

The package loop pins the **compile-time** vocabulary: the client's
`PackageReference` + `packages.lock.json` fix exactly which `doc-catalog.json` it
built against, and Dependabot is the reviewed channel for moving that pin. That
closes the drift hand-copying left open — but **not** all of it. Instances are
**not** packaged; they arrive at runtime as markdown over HTTP. So a server ahead of
the client's last Dependabot bump can emit an instance with a block type the
client's packaged catalog has never seen. Guard that residual gap explicitly:

- `catalogVersion` lives in the catalog file and ideally is stamped on each instance.
- On an unknown block type or a version mismatch, the client **fails loud or
  degrades gracefully** (render the raw block, flag it) — never silently drops it.

Producer-side drift is CI-guarded: the `Docs` test type runs `docengine check`
under `dotnet test`, so a self-inconsistent catalog (a block/doctype that no longer
conforms to its kind) fails the build **before** it can be exported and shared. The
package version pins the consumer; `catalogVersion` guards the runtime instance;
`check` guards the producer.

## Cadence

| Event | Action |
|---|---|
| Block/doctype/attr/enum/group changes | bump `catalogVersion`, re-export; it ships on the next `v*` tag → Dependabot PR bumps the client |
| New document produced | nothing — it ships as a runtime markdown payload |
| Client built against an older catalog | the lockfile pins it; merging the Dependabot PR moves the pin |
