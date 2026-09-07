# spike: doc-engine authoring flow — extending the vocabulary (Phase 5)

Enforcement (Phases 1–4) proved the engine *checks* documents once extracted.
This proves the other half: a new home *authors* documents there too — new
**instances**, new **blocks**, and new **doctypes** — against an editable,
repo-local catalog, with no engine change.

See [`PLANS/doc-engine-extraction.research.md`](../../PLANS/doc-engine-extraction.research.md)
for the full analysis and `MANIFEST.md` here for what travels.

## What's here

| Path | What it is |
|---|---|
| `catalog/` | An editable, checked-in catalog (the authoring mode). The meta floor (`_schema`, `kinds`) is verbatim; `blocks/finding.yaml` and `doctypes/brief.yaml` are **new vocabulary authored here** — neither exists in the engine's home catalog. |
| `bundle/` | The author-agent surface that travels: `create-doc` (agent + command), `selector.md`, the `howto/` guides. |
| `run.sh` | The authoring proof: `check` the extended catalog, discover the new types, author + `validate` an instance, reject a bad one. |
| `gate.sh` | The layer-3 enforcement net: a harness-free driver that discovers every instance and blocks on any drift. |

## What it proves

```
bash run.sh   # PASS — new block + doctype authored as data; instances enforced by the same engine
```

1. **New vocabulary is data, not code** — `check --root catalog` passes with a
   `finding` block and a `brief` doctype the engine never shipped. "The engine
   names no kind": a new structure is a new YAML file.
2. **It's discoverable** — `catalog` lists `brief`/`finding`; `catalog brief`
   shows its blocks. That is the decision matrix the `create-doc` agent reads.
3. **The new doctype has teeth** — a conforming `brief` instance validates
   (4 blocks); one missing the required `finding` is rejected.

The engine used is the already-proven Phase 1 copy — **same engine, new editable
catalog**. That is the whole point: the catalog is what a home owns and extends.

## Layer 3 — enforcing that validation actually runs

Validation itself (does an element follow the schema) is the engine's `check`
(catalog) and `validate` (instance). `gate.sh` adds the third layer: the net that
makes conformance mandatory across a whole repo — what this repo's `Docs` test
does, but with **zero xUnit / ParityGuard**.

```
bash gate.sh   # PASS — discovers every instance against the editable catalog and blocks on any drift
```

It discovers instances the way `DocInstances.Discover()` does — every `*.md` that
opens with a `docType:` front-matter block (a plain README is skipped) — then
`validate`s each against the `--root` catalog. A clean corpus passes; the moment a
malformed instance drifts in, the gate exits non-zero and names it. Because it is
just a shell driver shelling the engine (ADR 0015 process boundary), the same
check can be a CI step or a git hook — the test-harness is one option for layer 3,
not a requirement. The dependency only ever points test-engine → doc-engine, never
the reverse.

## Why the catalog is repo-local here (not baked into the tool)

Phase 3 ships the catalog *inside* the `dotnet tool` for enforce-only consumers.
Authoring needs the opposite: an **editable** catalog under version control, with
the engine pointed at it via `--root`. Both are the same binary — the resolver
(`--root` → CWD walk-up → baked-in) serves whichever the home wants. See
`MANIFEST.md` § "The two catalog modes".

## Isolation

Not in `ABox.slnx` or `dirs.proj`. The new-doctype instances are written to a temp
dir at runtime and never committed — so the host repo's `Docs` test (which uses the
*real* catalog, where `brief` does not exist) never discovers them. No committed
`.md` opens with a `docType:` front-matter block. Delete once the real extraction
lands.
