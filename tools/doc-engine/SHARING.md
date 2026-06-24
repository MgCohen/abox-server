# Sharing the doc catalog with the render repo

How the **catalog** (the block/doctype/kind vocabulary) crosses from this server
repo to the client/render repo (`abox-client`), so the client can build renders
that match what the engine produces.

## What is shared — and what is not

| Artifact | Source | Shared? | How |
|---|---|---|---|
| **Catalog / schema** | `kinds/` + `blocks/` + `doctypes/` | **Yes** | one generated JSON, copied beside the DLLs |
| **Document instances** | `out/<slug>.md` (e.g. `claude-planning.md`) | **No** | runtime payload — plain markdown, sent per-document |

The schema is a **vocabulary**, shared once and updated when it changes. Instances
are **content**, produced per-run and delivered as the already-human-readable
markdown the engine emits — never copied like a DLL.

Rule of thumb: **share the data, not the engine.** The client never takes
`ABox.DocEngine.dll`. It takes one data file describing the vocabulary and writes
its own renderers against it.

## Why a file, not a DLL

The DLLs the client copies are compiled **behavior**. The catalog is **data** —
the engine itself "names no kind"; blocks and doctypes are pure YAML. Shipping the
engine DLL would hand the client a *parser* it can't render with (and drag
YamlDotNet + the CLI into a render path). The client needs the *vocabulary*, then
owns presentation. So we ship the vocabulary as one file.

---

## Producer side — what we build (this repo)

### 1. A catalog export command

Add to the `docengine` CLI:

```
docengine catalog --json > doc-catalog.json
```

It serializes the already-loaded `kinds` + `blocks` + `doctypes` into one
machine-readable file. No new domain logic — `Catalog` already loads all of it for
the text `catalog` command; this is a serializer over the same data.

### 2. A version stamp

The top of the file carries a `catalogVersion`. Bump it whenever a block/doctype
field, attr, enum, or grouping changes. This is the guard that replaces the
version-pinning a package manager would give us — see *Drift* below.

### 3. Drop it where the DLLs land

The doc-engine is **not** in `ABox.slnx`, so the catalog does not appear in the
product DLL drop on its own — placing it there is a deliberate step. Put
`doc-catalog.json` in the **same output folder you copy the DLLs from**, next to
`ABox.*.Contracts.dll`. Script it into the existing copy step so it is never
forgotten.

> Alternative (zero new files): embed `doc-catalog.json` as an **embedded
> resource** in the Contracts DLL the client already takes, and read it via
> `Assembly.GetManifestResourceStream`. Faithful to "we just move the DLLs," at
> the cost of build wiring + folding a tooling concern into the wire-contracts
> assembly. Use only if "remember to copy one more file" actually bites.

### Proposed `doc-catalog.json` shape

```json
{
  "catalogVersion": "1",
  "kinds":    { "block": { "...": "..." }, "doctype": { "...": "..." } },
  "blocks": {
    "phase": {
      "description": "One ordered, shippable step of the build.",
      "collection": true,
      "group": "Phases",
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

---

## Consumer side — how the client reads it (`abox-client`)

### 1. Copy the file

Same step that copies the DLLs: pull `doc-catalog.json` from the server drop into
the client's shared/libs folder. (Or, with the embedded-resource option, read it
out of the Contracts DLL — nothing extra to copy.)

### 2. Load it once at startup

Deserialize the JSON into an in-memory catalog. Check `catalogVersion` against the
version the client was built for (see *Drift*).

### 3. Build a renderer per block type

For each block `type`, write a render component driven by the catalog entry:

- **`body.type`** → how to render the body (`markdown` → markdown component).
- **`attrs`** → which fields to show; `enum` attrs render as a known, fixed set
  (e.g. a `status` chip), so the client can style each value.
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

## Drift — the one thing manual copying does not give you for free

Copying files by hand is a package manager **without** version pinning. Nothing
stops a **stale catalog** meeting a **new instance** with a block type it has never
seen. Guard it explicitly:

- `catalogVersion` lives in the catalog file.
- Each instance carries its `docType` (and ideally the catalog version it was
  produced against).
- On an unknown block type or a version mismatch, the client **fails loud or
  degrades gracefully** (render the raw block, flag it) — never silently drops it.

## Cadence

| Event | Action |
|---|---|
| Block/doctype/attr/enum/group changes | bump `catalogVersion`, re-export, re-copy with the next DLL drop |
| New document produced | nothing — it ships as a runtime markdown payload |
| Client built against an older catalog | version check catches it; update the copied file |
