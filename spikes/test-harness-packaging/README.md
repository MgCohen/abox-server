# spike: test-harness extraction — the parity pillar (Phase 4)

Proves the **test-harness** — the cross-artifact/hard enforcement pillar
(`ParityGuard`, `[Rule]`, the Rulebook↔test lockstep) — lifts off this repo's
identity and runs standalone, with the **doc-engine as an optional soft
dependency**. This is the biggest phase (`PLANS/test-harness-extraction.md`);
this spike is its executable core.

See [`PLANS/doc-engine-extraction.research.md`](../../PLANS/doc-engine-extraction.research.md)
for the full analysis.

## The three layers, made concrete

`PLANS/test-harness-extraction.md` frames the extraction as three layers. This
spike demonstrates each:

| Layer | In this spike |
|---|---|
| **Engine moves verbatim** | `Harness/ParityGuard.cs`, `RuleAttribute.cs`, `TestMarkers.cs` — the parity logic (declared `### ` headers vs `[Rule]`-cited `[Fact]`s; the four out-of-sync lists) is the in-repo code, only the namespace changed. |
| **Seams find/replaced** | `Kit.Tests.Harness` for the `ABox.Tests.Harness` namespace; `RepoRoot.LocateBy(marker, …)` turns the hardcoded `ABox.slnx` marker into a parameter; the Rulebook is found via a `RulebookDir` metadata seam. |
| **Content re-authored** | `Sample/` is a fresh consumer suite (its own `Rulebook.md`, `[Rule]` tests) standing in for the re-authored test content a new home supplies. |

## What's here

| Path | What it is |
|---|---|
| `Harness/` | The renamed harness engine (`Kit.Tests.Harness`) — a `[Rule]` + `ParityGuard` library, no ABox tokens. |
| `Sample/` | A consumer suite: `Rules/Rulebook.md` + `[Rule]`-cited `[Fact]`s, the parity meta-runner, the marker-seam tests, and the optional doc-engine edge. |
| `Kit.root` | The root marker this spike names — standing in for `ABox.slnx`, proving that token is a seam. |
| `run.sh` | Builds the engine and runs the suite. |

## What it proves

```
bash run.sh   # PASS — 6 tests: parity + marker seam + optional doc-engine edge
```

1. **Parity travels** — `ParityGuard.For(assembly, "Rules").Assert()` keeps the
   consumer's `Rulebook.md` and its `[Rule]` tests in lockstep, off the ABox
   prefix. Verified to have teeth: an uncited `[Fact]` in scope fails the guard.
2. **The marker is a seam** — `RepoRoot.LocateBy("Kit.root", …)` finds the root
   by a name the new home chooses; a missing marker throws loud rather than
   going vacuously green.
3. **The doc-engine is optional** — `DocEngineOptionalTests` probes for
   `docengine` at the process boundary (ADR 0015): present → it can also validate
   Rulebooks-as-documents; absent → parity stands undiminished, no hard failure.
   That is the soft edge — a runtime probe, never a project reference.

## What a real Phase 4 adds beyond this core

The full harness carries more than the parity engine: multi-assembly `Suites`
discovery, the `Arch`/`Structure`/`Docs` types and their `ArchitectureModel` /
`SourceTree`, `LiveFactAttribute` (curated out), and the `TestTypes.Registered`
vocabulary (re-authored per home). Those are lift-and-curate, not new
invention — and per `PLANS/test-harness-extraction.md` ship as `Abox.TestKit`
only at the abox-client cutover, its blessed second consumer. That cutover needs
the client repo, out of scope for a spike.

## Isolation

Not in `ABox.slnx` or `dirs.proj`, so CI neither builds nor runs it. `Rulebook.md`
carries no `docType:` front matter, so the `Docs` test does not discover it.
`bin`/`obj` are gitignored. Delete once the real extraction lands.
