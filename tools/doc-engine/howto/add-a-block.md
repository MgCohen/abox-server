# HOWTO: add a new block

A **block** is a reusable content unit — one section of a document (a Summary, a
Phase, a Decision). Blocks are pure data: one YAML file per block type under
`blocks/`. The engine names no block; adding one is adding a file.

You will: write `blocks/<type>.yaml`, (optionally) list it in a doc type, and run
`check` until it passes.

## 1. Create `blocks/<type>.yaml`

The filename has no significance — the `type:` field inside is the block's name.
Required fields are `type`, `description`, `rubric`. Example — a `risk` block:

```yaml
type: risk
description: A risk to the work and how it is mitigated.
rubric:
  risk-and-mitigation: State the risk and the concrete mitigation.
  real: A real risk for this work, not a generic disclaimer.
body:
  type: markdown
```

Field meanings:

| Field | Required | What it is |
|---|---|---|
| `type` | yes | the block's name (kebab-case); the `## Header` an instance uses |
| `description` | yes | one line; feeds the `catalog` decision matrix |
| `rubric` | yes | a map of `id: rule` one-liners — the authoring + grading checks |
| `body` | no | `type: markdown` if the block has a prose body (it almost always does) |
| `attrs` | no | typed scalar attributes (see below) |
| `collection` + `group` | no | for repeatable blocks (see step 3) |

## 2. Conventions (the engine enforces these)

- **Uniform block YAML, no shorthand.** A field spec is always a map on its own
  lines. Write `body:` then `  type: markdown` — never the bare `body: markdown`.
- **`rubric` is an `id: rule` map** — each value a short, checkable sentence.
- **`attrs`** are typed scalars. Each attr's value is a *typespec*: `type: string`,
  or an enum:
  ```yaml
  attrs:
    status:
      enum: [todo, doing, done]
      default: todo
    owner:
      type: string
  ```
  Allowed types are `string`, `markdown`, `enum`. Scalar lists (like an enum's
  values) stay inline: `[todo, doing, done]`.

## 3. Singleton vs collection

- **Singleton** (default): appears once, as `## <Type>`. The `risk` block above is a
  singleton.
- **Collection**: repeatable, rendered as a group of members. Set `collection: true`
  and a `group:` label. The engine requires `group` whenever `collection` is set.
  ```yaml
  type: risk
  collection: true
  group: Risks
  description: A risk to the work and how it is mitigated.
  rubric:
    risk-and-mitigation: State the risk and the concrete mitigation.
  body:
    type: markdown
  ```
  An instance then writes `## Risks` followed by `### <title>` members.

## 4. Make it usable: add it to a doc type

A block can be defined and still unused. To allow it in a document, add its `type`
to that doc type's `blocks` list in `doctypes/<docType>.yaml` (and to `required` if
every such document must include it). This step is not needed for `check` to pass —
it is needed before an *instance* may use the block.

## 5. Verify

Run from the **tool root** — the directory that holds the project file (in this
repo, `tools/doc-engine/`). `--project .` means "the project in this directory".

```bash
dotnet run --project . -- check
```

`check` validates every *definition* — kinds, blocks, doc types — including the
block you added and the doctype you edited in step 4. Expect
`PASS — meta-schema, kinds, and every definition conform.` Failures name the file
and the exact problem (a missing required field, a bad attr type, or `collection`
set without `group`); fix and re-run.

To prove the block works end-to-end, author an instance that uses it and run
`validate` — see **add-an-instance.md**. (`check` covers definitions; `validate`
covers a document.)
