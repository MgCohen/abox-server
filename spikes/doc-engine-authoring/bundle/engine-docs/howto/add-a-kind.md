# HOWTO: add a new kind

A **kind** is a *category of definition*. `block` and `doctype` are the two kinds
that ship — each is a data file under `kinds/` describing what its definitions must
look like. Adding a kind means teaching the engine to enforce a new *family* of
structured files, with no code change.

One meta-schema, `_schema/kind.schema.yaml`, says what a kind file must contain —
and it is itself a kind, so it conforms to itself. The engine names no kind; it
loads `kinds/*.yaml` and enforces each kind's definitions.

You will: write `kinds/<name>.yaml`, create its definitions directory with at least
one definition, and run `check` until it passes.

## 1. Create `kinds/<name>.yaml`

Required fields: `name`, `description`, `defs`, `fields`. Optional: `constraints`.
Example — a `persona` kind (a reviewer lens with a checklist):

```yaml
name: persona
description: A reviewer persona — a lens with a checklist of concerns.
defs: personas/*.yaml
fields:
  persona:
    kind: string
    required: true
  description:
    kind: string
    required: true
  concerns:
    kind: strmap
    required: true
```

| Field | What it is |
|---|---|
| `name` | the kind's name |
| `description` | one line |
| `defs` | a glob (relative to the tool root) for this kind's definition files |
| `fields` | a `fieldmap`: each entry is a field every definition must satisfy |
| `constraints` | optional cross-field rules (see step 3) |

## 2. Field specs — the `fields` map

Each field is a map with a `kind` and an optional `required` (defaults false). The
available field kinds:

| `kind` | Validates the value is… |
|---|---|
| `string` | a string |
| `bool` | a boolean |
| `list` (+ `of: string`) | a list (optionally of strings) |
| `typespec` | one of `markdown` / `string` / `enum` (bare or as a map) |
| `attrs` | a map of `name → typespec` |
| `strmap` | a map of `id → string` (the rubric shape) |
| `fieldmap` | a map of `name → field spec` (used by the meta-schema itself) |

Write every spec in block notation — `kind:` and `required:` on their own lines. No
bare-string shorthand. A `list` field puts `of:` as a sibling of `kind:`:

```yaml
steps:
  kind: list
  required: true
  of: string
```

Rule of thumb: `strmap` for *named* entries (`id → text`), `list` (+ `of: string`)
for an *ordered, unnamed* sequence.

## 3. Constraints (optional)

Cross-field rules, chosen from a fixed registry:

```yaml
constraints:
  - rule: requires_when     # if `when` is set, `then` must be set too
    when: collection
    then: group
  - rule: subset            # the list in `of` must be a subset of the list in `in`
    of: required
    in: blocks
```

Only `requires_when` and `subset` exist. An unknown rule name is reported as an
error.

## 4. Create the definitions directory + one definition

Make the directory your `defs` glob points at, and add at least one file:

```yaml
# personas/security.yaml
persona: security
description: Reviews for security and trust-boundary issues.
concerns:
  authz: Every entry point checks authorization.
  secrets: No secret is logged or persisted in plaintext.
```

## 5. Verify

Run from the **tool root** — the directory that holds the project file (in this
repo, `tools/doc-engine/`); `--project .` means "the project here", and every
`defs` glob is resolved relative to this same root.

```bash
dotnet run --project . -- check
```

`check` validates your new `kinds/<name>.yaml` against the meta-schema, then every
file matched by its `defs` glob against the kind's `fields` + `constraints`. Expect
`PASS`. Errors name the file and the exact problem.

## Scope note (important)

`check` gives your new kind full **definition-floor** enforcement — its definitions
are validated like blocks and doc types are. It does **not** give you *instance*
validation: `validate` understands documents composed of blocks under a doc type
(the block→doctype composition), and that composition is not yet generalized to
arbitrary kinds. So a `persona` kind is enforced as data, but there is no
`validate`-able "persona document" until composition is wired (a deliberate,
deferred step). If all you need is enforced, structured definition files, a kind is
enough.
