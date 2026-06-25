# Rulebook ⇄ doc-engine — status & remaining work

Home of record for the effort that unifies the test **Rulebook** model with the
standalone **doc-engine** (`tools/doc-engine/`). Decision rationale lives in
[ADR 0013](../design/adr/0013-rulebook-as-document.md); the doctype roadmap in
[`tools/doc-engine/planned-doctypes.md`](../tools/doc-engine/planned-doctypes.md).
Keep this short — it tracks state and the open forks, not the design.

## The idea (one line)

Unify the **model**, not the engine: the doc-engine validates *intra-document*
structure; the test engine owns *cross-artifact* parity + test-specific rule
semantics. The dependency arrow points **out of** the zero-dependency enforcement
spine — Harness never depends on the engine (ADR 0012 / 0013).

## Shipped (PR stack #89 → #90 → #92)

| PR | Branch | What landed |
|---|---|---|
| #89 | `claude/rulebook-docengine` | doc-engine Rulebook model: `rule`/`links`/`criterion` blocks, `rulebook`/`test-template` doctypes, `labelmap` field-kind, canonical field-order check, fence-aware label parser |
| #90 | `claude/rulebook-adr` | ADR 0013 (proposed) |
| #92 | `claude/docs-test-type` | `Docs` test type — runs `docengine check`/`validate` under `dotnet test` by shelling out (no Harness→engine reference) |

Review remediation (H1/L1/L2/M1/Nit) closed; H2 cut as over-mechanism — see
[`rulebook-review-fixes.md`](rulebook-review-fixes.md).

## Remaining work

### 1. Merge the stack — owner's act
Protected paths + merge-to-`main` are walled off from the agent. Order, bottom-up:
`#89 → main`, then `#90` retargets → main, then `#92`. Flip ADR 0013
`proposed → accepted` when it lands.

### 2. Migrate the real rulebooks — the actual goal (needs decisions)
Today the `Docs` test only validates the **sample** docs in
`tools/doc-engine/out/`. The real `tests/**/Rulebook/rules.md` files are still
governed by the legacy `RulebookFormat` harness. The two models coexist; they are
not yet merged. Three forks to settle **before** touching real rulebooks:

| Fork | Options |
|---|---|
| Block identity | derive `<!-- id -->` from the `### ` header text vs. explicit id comments per rule |
| Front-matter | sweep `docType:`/`testType:` into all seven `tests/**/Rulebook/` files vs. keep implicit/inferred |
| `RulebookFormat` | shrink to cross-artifact/parity only once the engine owns intra-document structure vs. leave whole and run both |

### 3. Field-kind lookup — deferred, smallest
A documented catalog of the field-kinds (`string`/`bool`/`list`/`typespec`/
`attrs`/`strmap`/`fieldmap`/`labelmap`), so a schema author can discover what's
available. Independent of 1 and 2.

## Recommended sequence

Merge (1) first to clear protected-path churn → settle the forks in (2) before
any real-rulebook edits → (3) any time; it's self-contained.
