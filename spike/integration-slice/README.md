# Integration slice — five mechanics, one feature emission

> The walking skeleton that proves the five already-proven probe mechanics
> **compose into one end-to-end feature emission**. This is a SPIKE: tech
> validation only, throwaway API. Nothing here is the chosen design.

## Idea

The five probes (`spike/probe-*/`) each proved **one** mechanic in isolation. The
open question they leave is whether the mechanics **compose or fight** when you
emit a *real* feature. This slice authors one real vertical slice — an
aggregate-mutation feature modelled on RiverBooks `AddItemToCart` — as a
**recipe**, and lowers it to **owned, multi-file C#** that **compiles and runs**.

The feature: load a `User` aggregate, build a cart line-item, add it, persist.

```
recipe (provisional API)  ──emit──►  emitted/AddItemToCart.Models.cs   (minted records)
                                     emitted/AddItemToCart.Handler.cs  (wiring + glue)
                                                │
                                                ▼  build + run
                          add an item to ada's cart → cart has 1 → 2 items
```

## What's integrated — the five mechanics mapped

| # | Mechanic | Probe | Where it shows up in the slice |
|---|---|---|---|
| 1 | **Mint** a model from a use-site | A | `CartItem` + `AddItemCommand` are declared as data in the recipe's `Models` (`src/AuthoredRecipe.cs`) and lowered to `emitted/AddItemToCart.Models.cs` by `Emitter.MintModelsSource`. |
| 2 | **Wire** DI from `scope.Get<T>` markers | B | `WiringScan.Scan` parses the glue text, finds `scope.Get<Repo<User>>()` / `scope.Ask<BookDetails>()`, and emits `RegisterDiscovered` into the handler file — additively, body untouched. |
| 3 | **Render** every type semantically | D | `ResolutionModel` (idiomatic `SymbolDisplayFormat` + derived usings) renders `Guid`, `int`, `decimal`, `Repo<User>`, `User`. The handler's `using` set falls out of the symbol graph. |
| 4 | **Glue** — irreducible business logic inline | position 4 | `GlueSpec.Body` in `AuthoredRecipe.cs` — the author writes how the item is built and added; dropped verbatim into the typed `Handle` slot. |
| 5 | **Emit** — explicit gate to owned files | C | `dotnet run -- emit` lowers to the configured target `emitted/` (namespace `Acme.Cart`); `live` writes only the preview; detachment + re-emit override proven by `prove`. |
| 5+| **Forward-ref** minted type as `<T>` | E | The glue names the minted `CartItem` and `AddItemCommand` as ordinary types; they resolve because mint feeds the **shared resolution compilation** (same namespace, no using). |

## What was proved end-to-end (real evidence)

**The emit tool** (`dotnet run --project src -- prove`) — live→emit→edit→re-emit,
self-checked (`evidence/01-emit-prove.txt`):

```
[PASS] live did NOT create the owned target
[PASS] emit created the owned handler file
[PASS] wiring (probe B): Repo<User> registered
[PASS] wiring (probe B): BookDetails query registered
[PASS] mint (probe A): CartItem record emitted to the model file
[PASS] live preview tracked the recipe edit (+Notes)
[PASS] DETACHMENT: emitted target untouched by live
[PASS] OVERRIDE: re-emit reflects the recipe edit
PROVE: PASS
```

**The emitted feature builds + runs** (`dotnet run --project emitted-feature-proof`)
— a separate project that compiles the committed `emitted/*.cs` verbatim + the
catalog/runtime + a driver (`evidence/02-emitted-feature-run.txt`):

```
RegisterDiscovered: all statically-discovered deps are bound — OK
AddItemToCart -> user ada@example.com now has 1 cart item(s)
  item: 2 x Domain-Driven Design by Eric Evans @ 49.99 (BookId 11111111-1111-1111-1111-111111111111)
after a second add -> cart has 2 item(s)
wiring is load-bearing -> Wiring gap: a handler asks for Slice.Catalog.BookDetails (scope.Ask<BookDetails>) but no query produces it.
```

The last line is the rigor check: delete a registration and the **generated**
`RegisterDiscovered` throws an actionable wiring-gap error — the wiring is
load-bearing, not decorative (probe B's key property, preserved through
composition).

## The composition findings (the real question)

**Verdict: the mechanics compose — they do not fight — but only once you accept a
single ordering constraint and one shared substrate.**

### They compose, given an ordered pass set over one shared compilation

The slice runs **one ordered emit pass set** (`Emitter.Lower`), not five
independent generators:

```
1. MINT   → lower minted models to record source (bootstrap-rendered)
2. BUILD  → one ResolutionModel = catalog source + minted-model source
3. WIRE   → scan glue text for scope.Get/Ask, resolve T against the model
4. RENDER → resolve + render the return type against the SAME model
5. ASSEMBLE → model file + handler file (glue dropped verbatim)
```

**The keystone is step 2.** The probes never had to share a symbol table; the
slice does. Mint (A), render (D), wire (B) and forward-ref (E) **all resolve
against one `ResolutionModel`** built over catalog + minted source. That single
shared compilation is what makes `CartItem` (minted) and `Repo<User>` (catalog)
render identically and resolve as `<T>` in the same file (probe E,
same-compilation). Without it, the four passes would each spin up their own
compilation and the minted type would be invisible to the wiring/return-type
resolution.

### Ordering IS required — mint must run first

`MINT` **must precede** `BUILD`, because the resolution model has to contain the
minted record source for render/wire/forward-ref to see the minted types. This is
a hard ordering edge the isolated probes never exposed (each owned its whole
compilation). It is a small, fixed order — not a fight — but it is real: you
**cannot** do mint + wire + render in a single unordered generator pass. A small
**ordered set** (mint → resolve → {wire, render} → assemble) is the answer.

### One coherent multi-file feature, compiled as a unit

`emit` produces two files that compile together: a model file (minted records) +
a handler file (wiring + glue) referencing them cross-file in the same namespace.
The `emitted-feature-proof` project compiles them verbatim and runs — coherent
unit, confirmed.

### Friction that only appeared when combining (invisible in the isolated probes)

1. **Implicit-usings gap (the first real friction).** The author writes idiomatic
   short type names (`"Guid"`, `"Repo<User>"`), but a `ParseText` compilation does
   **not** apply the project's `ImplicitUsings`. The catalog source uses `Guid`
   unqualified and the resolution probe failed with `TypeKind.Error` until the
   probe tree explicitly opened `System`, `System.Collections.Generic`,
   `Slice.Catalog`, `Slice.Runtime`, and the feature namespace
   (`ResolutionModel.Resolve`). In the isolated probes the author wrote
   fully-qualified text or `typeof(...)`, so this never bit. **A real emit needs a
   recipe-level "open namespaces" config** so short names resolve the way the
   author means.

2. **Two-phase mint rendering (a bootstrap pass).** A minted model's own field
   types must render before the shared model exists — so `MintModelsSource` runs
   **twice**: once bootstrap (raw text) to build the model, once for real
   (semantic render). Harmless here (minted fields are BCL types), but it is an
   ordering wrinkle inside the mint step itself — minting and rendering are
   entangled, not cleanly separable.

3. **Glue text is opaque to the type system at authoring time.** The glue body is
   a `string`, so the `scope.Get<T>` markers and the minted-type references in it
   are only validated at the **composition gate** (the emitted feature's build),
   never while authoring the recipe. The probes that owned real C# (A/B) got
   authoring-time checking; lowering glue-as-text trades that away. This is the
   sharpest seam: the glue's correctness rides entirely on the final compile.

4. **Runtime DI semantics are orthogonal but leak into the proof.** A
   transient-factory registration gives a fresh aggregate per `scope.Get`, so the
   mutation appears not to persist — a runtime-DI choice, not a composition bug,
   but it surfaces only when you actually run the composed feature (the proof uses
   a singleton-by-capture repo). The probes never ran a multi-call mutation.

## Provisional API (clearly throwaway)

The recipe dialect in `src/Recipe.cs` + `src/AuthoredRecipe.cs` is the **smallest
throwaway API** needed to drive the slice. It is **NOT the chosen authoring
surface** — every name is placeholder:

```csharp
new FeatureRecipe(
    Namespace: "Acme.Cart", FeatureName: "AddItemToCart", Command: "AddItemCommand",
    Models: [ new ModelSpec("CartItem", [ new FieldSpec("BookId", "Guid"), ... ]) ],
    Glue: new GlueSpec(Returns: "User", Body: """
        var users = scope.Get<Repo<User>>();
        var item  = new CartItem(command.BookId, command.Qty, book.Price, book.Label);
        user.AddToCart(item);  return user;
        """));
```

Known-throwaway choices: glue is a raw `string` (no authoring-time typing);
`TypeRef` is C# type text; the "configured target" is a folder path, not a config
loader; the catalog "open namespaces" are hardcoded in `ResolutionModel`.

## How to run

```
# 1. emit the feature + run the full live→emit→re-emit self-check
dotnet run --project src -- prove

# 2. just emit to the committed target (emitted/)
dotnet run --project src -- emit

# 3. build + run the EMITTED feature (the proof it works)
dotnet run --project emitted-feature-proof
```

Layout:

```
src/                     the emit tool: recipe API, generators (reused from probes), emitter
  Recipe.cs              provisional recipe dialect (throwaway)
  AuthoredRecipe.cs      THE authored AddItemToCart slice
  Catalog.cs Runtime.cs  hand-authored catalog + DI seam the feature composes against
  ResolutionModel.cs     probe D renderer + probe E shared symbol table (the keystone)
  WiringScan.cs          probe B marker scan over the glue text
  Emitter.cs             the ordered pass set (mint → resolve → wire → render → assemble)
  Program.cs             live | emit | prove
emitted/                 the COMMITTED owned feature (probe A models + probe B/D/glue handler)
emitted-feature-proof/   compiles emitted/*.cs verbatim + drives it (the build+run proof)
evidence/                captured run output + the emitted .cs snapshot
```

## Honest limitations

- **Glue is untyped at authoring.** The glue body is a `string`; its
  `scope.Get<T>` markers and minted-type uses are checked only at the emitted
  feature's compile — not while the recipe is authored. Biggest gap vs. the model's
  "type system is the schema" invariant; would need lambda-as-source (backlog #10)
  to recover authoring-time typing.
- **Hardcoded catalog open-namespaces.** `ResolutionModel` opens a fixed using
  set so short type names resolve. A real emit must derive this from recipe/catalog
  config (PROBES residual #2: render against the *target* compilation).
- **Resolution runs off the running runtime's TPA list**, not a pinned reference
  pack (same shortcut as probe D). Fine for a slice; wrong for a real emit.
- **Mint renders twice** (bootstrap + real) — works because minted fields are BCL
  types; a minted field naming *another* minted type would need a dependency order
  among minted models (not exercised).
- **Single feature, single command, two files.** No multi-feature catalog, no
  cross-*recipe* forward refs (the unresolved-producer corner, probe E residual
  #1), no diagnostics-as-`Diagnostic` (probe B residual #3 — gaps are runtime
  throws), no reconciliation on re-emit (probe C residual #5).
- **Emit is a console tool**, not an in-build source generator — deliberate
  (matches probes C/D); the in-build generators (A/B/E) were re-expressed as emit
  passes, which is itself a finding: the two execution models unify under "one
  ordered pass set over a shared compilation."
```
