# spike: doc-engine extraction — the two-pillar carry

Proves, in isolation, that the doc-engine can leave this repo and **stay enforceable**
with none of the test-harness pillar (`ParityGuard`, Rulebook engine, `[Rule]` parity).

See [`PLANS/doc-engine-extraction.research.md`](../../PLANS/doc-engine-extraction.research.md)
for the full analysis; this is its Phase 1 executable proof.

## What's here

| Path | What it is |
|---|---|
| `Directory.Build.props` | Standalone (no import of the repo-root props) — the engine builds under settings it carries itself: net10 / nullable / **warnings-as-errors**. |
| `Engine/` | A verbatim copy of `tools/doc-engine` (code + `_schema`/`kinds`/`blocks`/`doctypes`), its only dependency YamlDotNet. |
| `Guard/` | The engine's own enforcement guard: **plain xUnit**, shelling out to the built engine (ADR 0015 process boundary). References nothing from `tests/`. |

## What it proves

```
dotnet build Engine/ABox.DocEngine.csproj -c Debug   # 0 warnings, 0 errors — clean under its own bar
dotnet test  Guard/DocEngineGuard.csproj  -c Debug   # 3 passed
```

The guard asserts three guarantees — the same enforcement the repo's `Docs` test gives,
minus the harness:

1. **Catalog is self-consistent** — `docengine check`.
2. **A conforming instance passes** — `docengine validate` on a good `research` doc.
3. **A non-conforming instance is rejected** — `docengine validate` on a doc missing
   required blocks exits non-zero, so the gate has teeth.

The good/bad sample instances are written to the OS temp dir at runtime (embedded as
strings), never committed under the repo — so the host repo's own `Docs` test never
discovers them.

## Isolation

Not in `ABox.slnx` or `dirs.proj`, so CI neither builds nor runs it — like every other
spike. It exists to be *run by hand* to validate the seam, and deleted once the real
extraction lands.
