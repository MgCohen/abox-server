# Probes — technical proofs of the recalibrated model

> Active, under `README.md` → *Invariants*. After the model was recalibrated (four positions,
> two-tier invariants, emit policy), three claims were still **paper-only**. Each got a secluded,
> runnable probe under `spike/probe-*/`. All three **build and run on net10.0**; tech is left in
> place as proof. This file is the index — each folder has its own `README.md` with full detail.

## Verdict: 3 / 3 proven

| Probe | Claim | Reviewer doubt it settles | Result |
|---|---|---|---|
| **A** `probe-a-inline-typegen/` | A use-site that names a new type *as a value* mints that type, usable in the **same compilation** | source-gen reviewer: *"a value names a type the generator must mint"* (the deepest unknown, #17) | ✅ **PROVEN** |
| **B** `probe-b-additive-wiring/` | `scope.Get<T>` is **both** a real call **and** a marker the generator reads — **additively, no interceptors** | source-gen reviewer: *"the verbs need fragile `[InterceptsLocation]` interceptors"* | ✅ **PROVED** |
| **C** `probe-c-live-emit/` | live preview ↔ explicit emit ↔ detachment ↔ re-emit override | the regeneration policy we locked — does it hold in code? | ✅ **PROVEN** |

Each is a classic `IIncrementalGenerator` / console tool, `Microsoft.CodeAnalysis.CSharp 4.11.0`
(reused from `spike/gen/`), isolated by a local `Directory.Build.props`, referencing no repo project
and touching nothing outside its folder.

---

## A — inline type back-fill ("usage drives generation")

**Authored (a bare value statement — no `var x =`):**
```csharp
CreateRecord("Foo", ("Id", typeof(Guid)), ("Name", typeof(string)));
Foo f = new Foo(Guid.NewGuid(), "bar");   // binds + runs — Foo was minted into THIS compilation
```
**Generated:** `public record Foo(global::System.Guid Id, string Name);` — additively, **no interceptors**.

**Real run:**
```
Foo  -> Foo { Id = 00ee7f8e-..., Name = bar }
List<Foo> count = 2
Repo<Foo> count = 1
```
**Confirmed beyond the core claim:** `List<Foo>` *and* a user-defined `Repo<Foo>` bind over the minted
type; the marker works **inside method bodies**; it's **order-independent** (use-before-declare); an
un-minted type fails cleanly with `CS0246`; `typeof(Guid)` resolves via the semantic model to the
fully-qualified name. The `CreateRecord` marker itself is generated (`RegisterPostInitializationOutput`),
so the value-call compiles with zero hand-written stub.

**Honest limits:** IDE IntelliSense lags on a freshly-typed marker (non-issue for an agent's
write→build; a rough edge for a human). **Cross-*recipe* forward refs** — a type a *sibling* recipe
generates, living in another compilation — are **not** exercised here; that's the residual slice of
doubt #17. `EmitCompilerGeneratedFiles` double-defines types unless the generated folder is
`Compile Remove`d (bit us once).

## B — additive wiring (the `scope` mechanism)

**Authored (body never rewritten):**
```csharp
Handler<AddItem>(scope => {
    var users = scope.Get<Repo<User>>();          // real container call AND a marker
    var book  = scope.Ask<BookDetails>(/*...*/);
    // ... body runs as written ...
});
```
**Generated beside the lambda:** a typed `Manifest` + `RegisterDiscovered(container)` calling
`EnsureService<Repo<User>>()` / `EnsureQuery<BookDetails>()` — the verb picks the kind (`Get`→service,
`Ask`→query), `T` resolved semantically to the fully-qualified generic.

**Proven load-bearing:** delete the `Read` registration → the *generated* wiring throws
`"Wiring gap: a handler needs service ProbeB.Read (scope.Get<Read>)…"`. The static manifest and the
runtime container are cross-checked. You get **both** at once: the lambda compiles/runs as authored,
*and* the generator statically enumerates the deps — purely additively. **Interceptors are not needed.**

**Honest limits:** static-analyzable only (no `scope.Get(runtimeType)` overload exists — deliberately
closed grammar); looped calls collapse to one dep *per distinct type*, not per call; open-generic
`Get<T>` in a generic method needs monomorphisation; `scope` must be the named lambda param (aliasing
to a local is missed without light data-flow); diagnostics are a runtime throw, not a build `Diagnostic`
yet. The one price — static-analyzability — is a property we *want* for deterministic agent-authored wiring.

## C — live vs emit (the regeneration policy)

**Two states, one explicit gate.** `Live` writes only to `live/` (preview); `Emit` writes only to the
configured target (folder `emitted-sample/Customers/` + namespace `Acme.Customers`, a checked-in record).

**Demonstrated sequence (evidence in `probe-c-live-emit/evidence/`):**

| Step | Action | Result |
|---|---|---|
| 1 | live on recipe v1 | preview written; **target not created** (no leak across the gate) |
| 2 | emit v1 | owned `Customer.cs` at target, 3 fields |
| 3 | edit recipe → v2 (+`Email`), re-run live | preview tracks edit; **emitted file byte-identical to step 2** |
| 4 | re-emit v2 | target now 4 fields |
| 4b | hand-edit emitted, re-emit | hand-edit gone (clobbered) |

`diff` of step-2 vs step-3 emitted = **empty** (detachment); `diff` of step-3 vs step-4 = **exactly the
`Email` line** (override). The emitted sample **compiles standalone as a library** → owned source, not a
blob. `prove` runs the whole sequence with self-checks, exits 0.

**Honest limits:** re-emit blindly clobbers manual edits (4b) — the exact seam where reconciliation /
protected-region checks plug in later (out of scope). "Configured target" is a record showing the
*shape* of the knob, not a config loader. "Live" is run-triggered, not a file-watcher. Real-world catch:
the repo's shared `artifacts/` output dir initially leaked an emit to repo root — fixed by anchoring
paths on `CallerFilePath` (the trick the spike already uses); **emit-target path resolution is a real
thing to get right.**

---

## What this means

The three mechanics the model leans on — **mint-a-type-from-a-use-site**, **read-markers-additively**,
and **live/emit detachment** — are no longer assumptions; they run. The recalibrated invariants
("source-generation back-fills inline", "minimal dialect — in the wiring", "owned output via explicit
emit") have a working floor under them.

**Residual technical unknowns** (not blockers, but the honest next layer):

1. **Cross-recipe / cross-compilation forward refs** (A's residual) — a type one recipe generates that
   another recipe must reference. This is the `TypeRef`-vs-`<T>` seam already named in `README.md` §8 #17.
2. **Diagnostics quality** (B) — gaps surface as runtime throws; for agent-authoring they should be
   build `Diagnostic`s with actionable messages (a companion analyzer).
3. **Emit-target config + path resolution** (C) — a real config loader, and the `artifacts/`/base-dir
   trap, need solving for a non-toy emit.
4. **Reconciliation on re-emit** (C) — pulling manual edits back into the recipe, or detecting drift —
   explicitly future scope.

None of these contradicts the model; each is a known piece of the next build.
