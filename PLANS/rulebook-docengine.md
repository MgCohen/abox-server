# Rulebook ⇄ doc-engine — CLOSED

Unified the test **Rulebook** model with the standalone **doc-engine**
(`tools/doc-engine/`): the engine validates *intra-document* structure; the test
engine owns *cross-artifact* parity. The dependency arrow points **out of** the
zero-dependency enforcement spine — the Harness never references the engine; the
`Docs` test type shells out to it.

Decision of record: [ADR 0015](../design/adr/0015-rulebook-as-document.md) (accepted).
Doctype roadmap: [`tools/doc-engine/planned-doctypes.md`](../tools/doc-engine/planned-doctypes.md).

## What shipped

- **doc-engine Rulebook model** — `rule` / `criterion` blocks, `rulebook` /
  `test-template` doctypes; block id is an optional handle; rulebooks declare
  `docType`/`testType`/`template`/`harness` in front matter (the `links` block was
  dropped in favour of front-matter pointers).
- **`Docs` test type** — runs `docengine check` / `validate` under `dotnet test` by
  shelling out (no Harness→engine reference), guarded by `ParityGuard` like any type.
- **Migration** — every real `tests/**/Rulebook/{rules,template}.md` reauthored as a
  front-matter instance (full `### ` headers kept, so `[Rule]` strings + parity keys
  didn't move); `RulebookFormat` + its Meta tests **deleted** (the doctypes subsume
  its intra-document checks; parity stays in `ParityGuard`).
- **No global output dir** — the engine owns no `out/`; a document lives in its home
  folder and is validated in place.

## Deferred (not blocking)

- **Field-kind lookup catalog** — a documented catalog of the field-kinds
  (`string`/`bool`/`list`/`typespec`/`attrs`/`strmap`/`fieldmap`/`labelmap`) so a
  schema author can discover what's available. Self-contained; pick up any time.
- **Front-matter–driven validation** — when the first non-rulebook instance lands
  (likely an ADR), generalize the `Docs` test to discover any `*.md` with a `docType`
  front-matter and validate it in place, instead of scanning `RulebookFolders()` only.
