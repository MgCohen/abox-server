# Probes — technical proofs of the recalibrated model

> Active, under `README.md` → *Invariants*. Several claims were still **paper-only** — three from the
> recalibration (inline type-minting, additive wiring, the emit gate) and two from the Roslyn
> semantic-model pass (type rendering, forward-refs). Each got a secluded, runnable probe under
> `spike/probe-*/`. **All five build and run on net10.0**; tech is left in place as proof. This file
> is the index — each folder has its own `README.md` with full detail.

## Verdict: 5 / 5 proven

| Probe | Claim | Reviewer doubt it settles | Result |
|---|---|---|---|
| **A** `probe-a-inline-typegen/` | A use-site that names a new type *as a value* mints that type, usable in the **same compilation** | source-gen reviewer: *"a value names a type the generator must mint"* (the deepest unknown, #17) | ✅ **PROVEN** |
| **B** `probe-b-additive-wiring/` | `scope.Get<T>` is **both** a real call **and** a marker the generator reads — **additively, no interceptors** | source-gen reviewer: *"the verbs need fragile `[InterceptsLocation]` interceptors"* | ✅ **PROVED** |
| **C** `probe-c-live-emit/` | live preview ↔ explicit emit ↔ detachment ↔ re-emit override | the regeneration policy we locked — does it hold in code? | ✅ **PROVEN** |
| **D** `probe-d-semantic-rendering/` | `ITypeSymbol.ToDisplayString` renders all type cases + derives usings — **and authoring is unchanged** | the int/string/`FullName` naming struggle (§8 #16) | ✅ **PROVEN** |
| **E** `probe-e-forward-ref/` | a minted type is usable by `<T>` same-compilation + cross-project-referenced; `TypeRef` only at the unresolved-producer boundary | the forward-ref / `TypeRef` claim (§8 #17) | ✅ **PROVEN** |

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

## Integration slice — the five compose end-to-end (`integration-slice/`)

Beyond the isolated probes: one real `AddItemToCart`-style vertical slice, authored as a (throwaway,
**provisional**) recipe and **emitted to owned multi-file C# that builds and runs**. Mint (A) + wire (B)
+ render (D) + glue (position 4) + emit/forward-ref (C/E) all meet in one flow. Real run:

```
PROVE: PASS  (live didn't leak; wiring registered Repo<User> + BookDetails; CartItem minted;
              detachment + re-emit override all PASS)
AddItemToCart -> user ada@example.com now has 1 cart item(s)
  item: 2 x Domain-Driven Design by Eric Evans @ 49.99
wiring is load-bearing -> Wiring gap: a handler asks for BookDetails but no query produces it.
```

**They compose — they don't fight — under one hard constraint and one ordering rule:**
- **Keystone: a single shared compilation.** The probes each owned their own compilation; the slice
  forces mint/wire/render/forward-ref to resolve against **one `ResolutionModel`** built over catalog +
  minted source. That shared symbol table is what makes minted `CartItem` and catalog `Repo<User>`
  render identically and resolve as `<T>` in the same file.
- **Ordering is required:** `mint → resolve → {wire, render} → assemble`. Mint must run *before* the
  resolution model is built. You **cannot** do mint+wire+render in one unordered generator pass — but a
  small fixed ordered pass-set does it, and the in-build generators (A/B/E) re-expressed cleanly as
  emit-tool passes (C/D style).

**Friction the isolated probes hid:**
1. **Implicit-usings gap** — `ParseText` compilations don't apply `ImplicitUsings`, so author-written
   short names (`"Guid"`) errored until the resolver explicitly opened the catalog/feature namespaces.
   A real emit needs a recipe-level **open-namespaces config**.
2. **⚠️ Glue is untyped at authoring** — the glue body was authored as a **`string`**, so its markers
   and minted-type uses are validated only at the *emitted* feature's compile, never while authoring.
   This is the sharpest tension with the **"type system is the schema"** invariant, and it's the key
   open *design* question (vs §8 #10's typed-lambda-leaf path). **Not a tech blocker — a decision.**
3. **Mint renders twice** (bootstrap + real) — minting and rendering are entangled in the pass order.

**⚠️ What it did NOT prove — the leverage gap.** The slice proved the *pipe*, not the *value*. The
authored recipe had **≥ the surface area of the hand-written feature**: the glue was authored as a
**string that is the handler body verbatim** (a 1:1 copy — zero compression), the models were spelled
field-by-field, and `FeatureRecipe`/`GlueSpec` ceremony sat on top — so the recipe came out *longer*
than the code it emits. It also **inverts the invariant** *"components carry the standards; custom glue
is the slotted exception"*: here the glue carried everything and the components almost nothing. Leverage
is unsolved and lives entirely in the **dialect + component/motif design** (e.g. a `Mutates<User>` motif
that implies load/save/wire/return so the author writes only the divergence). `leverage-probe/` takes
this on directly: re-author the same feature under a motif and measure the surface drop.

## What this means

The five mechanics the model leans on — **mint-a-type-from-a-use-site**, **read-markers-additively**,
**live/emit detachment**, **semantic-model type rendering**, and **forward-ref resolution** — are no
longer assumptions; they run, **and they compose into one emitted feature** (`integration-slice/`). The
recalibrated invariants ("source-generation back-fills inline", "minimal dialect — in the wiring",
"owned output via explicit emit") have a working floor under them.

## Decision recorded — adopt semantic-model generation (probes D + E)

The core spike's reflection-`FullName` renderer is superseded by the probes' **Roslyn semantic-model**
approach. Recorded because both probes came back fully positive:

- **Type rendering goes through `ITypeSymbol.ToDisplayString`** with a chosen `SymbolDisplayFormat`.
  Idiomatic (`int`, `List<string>`, `Outer.Inner`, `string?`, named tuples) + a **derived `using` set**,
  or fully-qualified with none. Closes §8 #16; ends the hand-rolled alias/`FullName` problem.
- **Authoring is unchanged — only the backend moved.** The recipe is the same typed tree; idiomatic
  vs fully-qualified is an **emit setting**, not an authoring choice. `TypeRef` = C# type text is
  strictly *more* expressive than the old reflection `typeof`/`Of<T>` (it can spell `int?`, `string?`,
  named tuples that `System.Type` cannot).
- **Generation runs over a `Compilation`** (the in-build "live" generator + the Compilation-backed
  "emit" tool share one symbol-rendering core). Emit stays a tool, not a source generator.
- **Forward refs (§8 #17) narrowed:** a minted type is an ordinary `<T>` within the same compilation
  (order-independent) and cross-project when the producer is referenced. `TypeRef` (name-based) is
  required **only at the unresolved-producer boundary** (unbuilt/unreferenced or circular producer).

**Residual technical unknowns** (not blockers, the honest next layer):

1. **Unresolved-producer forward refs** (E's corner) — referencing a generated type whose producer
   isn't built/referenced (or a circular producer). The only place `TypeRef` is genuinely needed;
   §8 #17 now scoped to exactly this.
2. **Pinned reference set for rendering** (D) — the probe resolved types off the running runtime's TPA
   list, not a pinned reference pack / the target compilation. A real emit must render against the
   target's `Compilation` (also where types the target owns / a sibling generates resolve).
3. **Diagnostics quality** (B) — gaps surface as runtime throws; agent-authoring wants build
   `Diagnostic`s with actionable messages (a companion analyzer).
4. **Emit-target config + path resolution** (C) — a real config loader, and the `artifacts/`/base-dir
   trap, for a non-toy emit.
5. **Reconciliation on re-emit** (C) — pulling manual edits back, or detecting drift — future scope.
6. **⚠️ Glue typing (the one real *design* question, from the slice)** — the integration slice authored
   the position-4 glue as a `string`, which loses authoring-time type-checking and collides with "type
   system is the schema." The alternative is §8 #10's typed-lambda-leaf path (lower a real lambda to
   source). This is a decision to make before the dialect is fixed, not a tech unknown.
7. **Open-namespaces config + shared compilation + pass ordering** (slice) — emit must build one shared
   `Compilation` with recipe-declared open namespaces, and run a fixed `mint → resolve → {wire,render}
   → assemble` order.

None of these contradicts the model; each is a known piece of the next build. Item 6 is the one that
feeds the *dialect* decision directly.
