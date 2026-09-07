# spike: doc-engine packaging — the installable pillar (Phase 3)

Proves the **doc-engine** ships as a `dotnet tool` (`docengine`) that enforces
documents from an installed copy, with its catalog riding inside the package —
no repo checkout required. This is the extraction's payload: the engine on its
new home.

See [`PLANS/doc-engine-extraction.research.md`](../../PLANS/doc-engine-extraction.research.md)
for the full analysis; this is its Phase 3 executable proof.

## What's here

| Path | What it is |
|---|---|
| `Engine/` | The `tools/doc-engine` engine, unchanged except for the two lines packaging demands (below). |
| `Directory.Build.props` + `global.json` | The self-containment an extracted repo carries itself — net10 / nullable / warnings-as-errors and a pinned SDK, with no repo-root props to inherit. |
| `pack-and-run.sh` | The proof: pack → install → enforce from a catalog-less dir → soft-probe. |

## The only changes over the in-repo engine

1. **`csproj`** gains `PackAsTool` + `ToolCommandName` + the catalog as `<Content>`
   copied to output, so it packs into the tool.
2. **`Program.cs` `ResolveRoot`** gains one fallback: after the CWD walk-up
   fails, walk up from `AppContext.BaseDirectory` — the tool's own install dir,
   where the packed catalog lives. In-repo behavior is unchanged (CWD wins).

Everything else — every `.cs`, the whole catalog — is byte-identical to the
in-repo engine.

## What it proves

```
bash pack-and-run.sh   # PASS — engine packs, installs, and enforces as a standalone tool
```

1. **It packs** — `dotnet pack` produces `ABox.DocEngine.Tool.0.1.0.nupkg` with
   the catalog under `tools/net10.0/any/`.
2. **It installs and runs** — `dotnet tool install --tool-path` then
   `docengine check` from a directory with no catalog on its walk-up path; the
   BaseDirectory fallback finds the packed catalog and the check passes.
3. **The gate has teeth** — a conforming `research` doc validates; a doc missing
   required blocks is rejected non-zero.
4. **Soft-wiring** — a consumer probes `command -v docengine`: present → hard
   document check ON; absent → it degrades, never a hard failure. That is the
   "use it if it's there" edge, at the process boundary, never a project ref.

## The repo ties this phase would repoint (real cutover, not in this spike)

`tests/Central/Docs/Support/DocEngine.cs` (installed tool instead of
`dotnet run --project tools/doc-engine`), `on-doc-change.sh`, the
`.claude/create-doc` path, `dirs.proj` (drop the `tools/**/Tests` glob),
`governance/protected-paths` (keep the catalog-data lines, drop the engine-code
line), and the `abox-version`/client export. Those are protected paths and a
real new home — out of scope for a spike, which by design perturbs nothing.

## Isolation

Not in `ABox.slnx` or `dirs.proj`; pack/install/scratch output goes to a temp
dir, so the repo tree stays clean. No committed `.md` carries `docType:` front
matter, so the `Docs` test does not discover anything here. Delete once the real
extraction lands.
