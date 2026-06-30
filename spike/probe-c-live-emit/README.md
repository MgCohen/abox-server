# PROBE C — live vs emitted (the emit gate)

A self-contained console tool proving the **regeneration policy**: a recipe self-generates a
**live preview** continuously, an explicit **emit** materializes the **owned** artifact at a
configured target, and once emitted that artifact is **detached** — continued live editing never
touches it. Re-emit overrides the target.

This probe is about the **gate and detachment semantics**, not the lowering. The lowering is a
deliberately tiny, deterministic recipe → two `.cs` files; the interesting part is *which directory
gets written, when, and by which command*.

## Idea

| State | Trigger | Writes to | Owned? |
|---|---|---|---|
| **Live** | continuous, every recipe edit | `live/` (preview dir) | no — regenerated wholesale each run |
| **Emit** | explicit `emit` command | the **configured target** (`emitted-sample/Customers/`, namespace `Acme.Customers`) | yes — real, committed `.cs` |

One recipe (`src/Recipe.cs`), two states, one explicit gate between them. The recipe is the source
of truth; the preview is never hand-edited; the emitted files are detached from the live loop.

## Why

This nails down two anchor points from `spike/README.md`:

- **Invariant — "Owned output, via explicit emit"** (§ Invariants): *"Generated code is normal,
  committed `.cs` — materialized only by an explicit emit, never invisible weaving."*
- **§ "Emit — live vs emitted (the regeneration policy)"**: Live self-generates in place; **emit**
  materializes the real artifact at the configured target; once emitted it is **detached**;
  **re-emit overrides** (and clobbers manual edits — accepted, no reconciliation in scope).

The probe makes those words executable and falsifiable: every claim is a `[PASS]/[FAIL]` check in
`prove`, and the before/after files are captured under `evidence/`.

## What was proved

The `prove` command runs the full sequence and self-checks. Real evidence, file-by-file:

| Step | Action | Effect on `live/` | Effect on the target | Check |
|---|---|---|---|---|
| 1 | LIVE on recipe **v1** (3 fields) | preview written (3 fields) | **target not created** | live does not leak across the gate |
| 2 | EMIT **v1** | — | `Customer.cs` written, ns `Acme.Customers`, 3 fields | owned file at configured target |
| 3 | edit recipe → **v2** (+`Email`), re-run LIVE | preview now 4 fields | **byte-identical to step 2** | **DETACHMENT** |
| 4 | RE-EMIT **v2** | — | target now 4 fields (gains `Email`) | **OVERRIDE** |
| 4b | hand-edit emitted file, RE-EMIT again | — | hand-edit gone | re-emit **clobbers** manual edits |

**The load-bearing evidence** (`evidence/`, captured by a clean manual run):

- `A-emitted-after-emit-v1.cs` vs `B-emitted-after-live-v2.cs` → **identical** (`diff` empty).
  Editing the recipe and re-running live did **not** touch the emitted file. Detachment holds.
- `B-emitted-after-live-v2.cs` vs `C-emitted-after-reemit-v2.cs` → **differs by exactly the
  `public string Email { get; init; }` line**. Re-emit overrode the target. Override holds.

The committed `emitted-sample/Customers/*.cs` is left at the **v2 / post-re-emit** state (4 fields),
and it **compiles standalone** as a normal `.cs` library — verified separately — confirming the
"owned, real source" claim, not just a string blob.

## How to run

```
cd spike/probe-c-live-emit/src

dotnet run -- prove     # full sequence + self-checks (the proof); exit 0 = all PASS
dotnet run -- live      # regenerate the live preview into ../live/ (continuous self-generation)
dotnet run -- emit      # explicit gate: write owned files to the configured target

# simulate "the author edited the recipe" between runs:
PROBE_C_RECIPE_VARIANT=v1 dotnet run -- live   # 3-field recipe
PROBE_C_RECIPE_VARIANT=v2 dotnet run -- live   # 4-field recipe (+Email)
```

`prove` starts from a clean slate (deletes `live/` and the target folder) so it is repeatable.
No reference to repo projects; nothing outside this folder is touched (build artifacts land in the
repo's shared `artifacts/` dir; path resolution is anchored on the source file via
`CallerFilePath`, so the tool never writes outside `probe-c-live-emit/`).

## Where the pieces live

| File | Role |
|---|---|
| `src/Recipe.cs` | the recipe (tiny typed tree) + the working recipe / variant switch (the "edits") |
| `src/EmitTarget.cs` | the **configured target** — folder + namespace, as a checked-in record |
| `src/Lowering.cs` | deterministic recipe + target → `.cs` artifacts (same input → same bytes) |
| `src/Engine.cs` | `Live` (preview dir only) and `Emit` (configured target only) — the gate |
| `src/Program.cs` | `live` / `emit` / `prove` commands + the self-checking sequence |
| `emitted-sample/` | committed proof of emitted output (the owned files) |
| `live/` | committed proof of the live preview |
| `evidence/` | captured before/after snapshots + console logs |

## Honest limitations

- **Re-emit clobbers manual edits.** By design, and demonstrated (step 4b). Re-emit is a blind
  overwrite of the target; any hand-edit to an emitted file is lost on the next emit. This matches
  the policy ("accepted for now, no auto-sync") and is the deliberate seam where **reconciliation /
  protected-region checks** would later plug in (out of scope here): emit would diff against the
  on-disk file and either merge protected regions or refuse on conflict.
- **"Configured target" is a checked-in record, not real project config.** `EmitTarget.Sample`
  hardcodes one folder + namespace. A real system reads this from recipe/project settings; the
  probe shows the *shape* of the knob (folder + namespace flow into the lowering and placement), not
  a config-loading mechanism.
- **The lowering is intentionally trivial.** A record + a field-name array, plain string templating
  — no Roslyn, no snippet substitution (that mechanism is proved elsewhere in the spike). This probe
  deliberately swaps real codegen for a stub so the gate/detachment behavior is the only variable.
- **"Live" is run-triggered, not a watcher.** Each `live` invocation regenerates the preview once;
  there is no file-watch loop. The continuous-self-generation property is modeled as "re-run live
  and the preview tracks the recipe," which is sufficient to prove the preview is never the owned
  artifact and is regenerated wholesale.
- **Single target, single recipe variant axis.** No multi-target emit, no recipe *variations*
  (a parked future item). The variant switch (`v1`/`v2`) exists only to simulate an author edit
  between runs.
