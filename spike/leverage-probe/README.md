# Leverage probe — does a motif make the recipe smaller than the feature?

> **⛔ API/DIALECT REJECTED — superseded by `../extraction-probe/`.** This probe proves the **tech** (a
> motif can carry the scaffold so the author writes only the divergence) — but the authoring **style
> shown here is rejected**: it is stringly-typed free text (`key`/`command`/`models`/`with` as strings),
> which breaks type-safety, structure, and swappable typed components. The leverage *number* is
> contaminated by that stringiness. **The typed dialect that replaces it lives in
> `../extraction-probe/`** (generics + real-code lambdas, 0 business strings, same 9 lines, same emitted
> output). Read this probe for the *tech*; read the extraction probe for the *dialect*. See
> `spike/PROBES.md` for the full disclaimer.

> Answers the **leverage gap** the integration slice left open (`spike/PROBES.md`).
> The slice proved the *pipe* (mint/wire/render/emit/forward-ref compose into a
> running feature) but **failed the value**: its authored recipe had **more**
> surface than just hand-writing the feature. This probe re-authors the **same**
> feature under a **`Mutates<User>` motif** and measures the surface drop.

## Idea

The slice's recipe lost because its glue was a verbatim string copy of the handler
body and its models were spelled field-by-field — the recipe carried *everything*
and the components carried *nothing*, inverting the invariant *"components carry
the standards; custom glue is the slotted exception."*

A **motif component** flips that back. `Mutates<TAggregate>` is a reusable
component that **carries the aggregate-mutation scaffold**; the author's recipe
slots in **only the divergence**. Leverage = (what the motif implies) is large and
written once; (what the author writes) is just the irreducible business logic.

```
terse motif recipe ──emit──►  emitted/AddItem.Models.cs   (minted records)
  (9 authored lines)          emitted/AddItem.Handler.cs  (motif scaffold + divergence + wiring)
                                          │
                                          ▼  build + run
                            add an item to ada's cart → 1 → 2 items; wiring load-bearing
```

Same emit machinery as the slice (mint → resolve → wire → render → assemble),
reused wholesale. The probe is a **thin motif layer on top**, not a new pipeline.

## The motif — what scaffold it carries

`Feature.Mutates<TAggregate>(key, command, models, with)` (`src/Motif.cs`).
Written **once**, reused by every aggregate-mutation feature. It implies — the
author never writes — all of:

| Implied by the motif | Emitted as |
|---|---|
| load the aggregate by key | `var __repo = scope.Get<Repo<TAgg>>(); var agg = __repo.Load(<key>);` |
| the handler shape | `public static TAgg Handle(Scope scope, TCommand command)` |
| persist | `__repo.Save();` |
| return the mutated aggregate | `return agg;` |
| **DI wiring for the aggregate repo** | `EnsureService<Repo<TAgg>>()` — falls out of the motif's *own* `scope.Get` marker, scanned by the same additive pass (probe B) |
| feature name + namespace | derived by convention from the command + aggregate |
| field-by-field model ceremony | replaced by a terse `"Name(field:Type, …)"` parser (`src/TerseModel.cs`) |

The author writes only: a **terse command + model declaration** and the
**`with:` divergence** — the cross-module `Ask<BookDetails>` and building/adding the
`CartItem`. The repo load/save/return and the `Repo<User>` registration are gone
from the author's hands entirely.

## Surface-area result (the deliverable)

Counting **only what the author writes** — generated code and the one-time
motif/catalog/runtime/machinery definitions are excluded (written once, amortised).
Lines = non-blank, comment-stripped. Tokens = a length-independent proxy for
"decisions the author made" (string literals collapse to one token each). Counts
reproduced by `evidence/measure.py`; raw in `evidence/04-surface-area.txt`.

| version | authored lines | author tokens |
|---|---:|---:|
| **motif recipe** (this probe) | **9** | **37** |
| hand-written baseline (the "real case") | 17 | 147 |
| integration-slice recipe (verbose) | 31 | 116 |

**Verdict — leverage PROVEN. Win condition `motif << hand-written << verbose slice` is MET.**

- Motif recipe is **1.9× smaller** (lines) / **4.0× fewer tokens** than the
  hand-written feature. The motif recipe is *meaningfully smaller than what you'd
  type by hand* — the bar the slice failed.
- The verbose slice recipe is **1.8× larger** (lines) than the hand-written
  feature — the failure the probe set out to reverse, reproduced.
- Motif recipe is **3.4× smaller** (lines) than the verbose slice recipe.

**Honest nuance on the numbers.** By *lines*, the ordering is clean
(9 < 17 < 31). By *tokens* the slice (116) lands *below* the baseline (147), not
above — because the verbose recipe is *line*-heavy (`ModelSpec`/`FieldSpec`
wrappers, one per field, each on its own line) while its string-literal glue
collapses to few tokens. So "verbose slice > hand-written" is unambiguous in lines
and in raw verbosity, but the slice's regression is best read as a *structural*
(line/ceremony) blow-up, not a token blow-up. The **motif wins on both metrics
decisively** (lowest lines *and* lowest tokens), which is the load-bearing result.

## What's authored vs implied — every emitted line mapped

Emitted `AddItem.Handler.cs` / `AddItem.Models.cs` (committed in `emitted/`):

| Emitted line | Source |
|---|---|
| `record AddItemCommand(string Email, Guid BookId, int Qty)` | **authored** (terse `command:`) |
| `record CartItem(Guid BookId, int Qty, decimal Price, string Label)` | **authored** (terse `models:`) |
| `Handle(Scope scope, AddItemCommand command)` signature | **motif** (shape) + authored command name |
| `var __repo = scope.Get<Repo<User>>();` | **motif** (load scaffold) |
| `var agg = __repo.Load(command.Email);` | **motif** scaffold + authored `key:` |
| `var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));` | **authored** (divergence) |
| `agg.AddToCart(new CartItem(…));` | **authored** (divergence) |
| `__repo.Save();` | **motif** (persist scaffold) |
| `return agg;` | **motif** (return scaffold) |
| `RegisterDiscovered` + `EnsureService<Repo<User>>()` | **motif** (its `scope.Get` marker, auto-wired) |
| `EnsureQuery<BookDetails>()` | **authored** divergence's `scope.Ask`, auto-wired |
| `using …;`, `namespace Acme.Users;` | **motif** (derived) |

Six of the handler's lines are pure motif scaffold the author never touched; the
two divergence lines are the irreducible business logic. That ratio **is** the
leverage.

## How to run

```
# 1. emit the feature + run the full live→emit→edit→re-emit self-check
dotnet run --project src -- prove

# 2. just emit to the committed target (emitted/)
dotnet run --project src -- emit

# 3. build + run the EMITTED feature (proof it works, same behaviour as the slice)
dotnet run --project emitted-feature-proof

# 4. reproduce the surface-area table
python3 evidence/measure.py
```

`prove` exits 0 with `PROVE: PASS`; the emitted-feature run prints the cart
mutation and the load-bearing wiring-gap throw (`evidence/01`, `02`). The emitted
feature behaves identically to the integration slice — same item added, same
load-bearing wiring — only the *authored surface* changed.

Layout:

```
src/                     the emit tool — motif layer ON TOP of reused slice machinery
  Motif.cs               THE MOTIF: Feature.Mutates<TAggregate> (written once, reused)
  TerseModel.cs          terse "Name(field:Type,…)" parser (written once, reused)
  AuthoredRecipe.cs      THE authored motif recipe — the measured author surface
  Emitter.cs             expands the motif (re-introduces load/save/return/wire) then lowers
  Catalog.cs Runtime.cs  reused catalog + DI seam (one-time infra)
  ResolutionModel.cs WiringScan.cs Net.cs   reused probe-D/B machinery + refs
  Program.cs             live | emit | prove
emitted/                 the COMMITTED owned feature emitted from the motif recipe
emitted-feature-proof/   compiles emitted/*.cs verbatim + drives it (build+run proof)
evidence/                prove output, emitted-run output, emitted snapshot,
                         baseline-handwritten.cs, measure.py, the surface table
```

## Honest limitations

- **The motif is aggregate-mutation-specific.** `Mutates<TAggregate>` carries the
  load→mutate→save→return motif and *only* that. A projection/reactor/ingest feature
  needs a different component (`.Projects` / `.Republishes` / `.Ingests`, per
  `authoring-dialects.md`). This probe proves leverage *for one motif*; it does not
  prove one universal motif exists (the scorecard there says it doesn't — terseness
  is layered, not universal).
- **One-time motif cost is real, just amortised.** The leverage comes from *not*
  counting `Motif.cs` + `TerseModel.cs`. That's legitimate — they're written once
  and reused across every mutation feature — but the **first** mutation feature in a
  fresh codebase pays that cost. Leverage is a *per-feature-after-the-first* win, and
  it grows with feature count. Honestly: the motif moves surface *into the component*,
  but unlike the slice it is a **net reduction at the author's site** *and* the moved
  surface is reused, not per-feature.
- **Glue is still a string** (the `key:` and `with:` text). Apples-to-apples with the
  slice on purpose. Its markers and minted-type uses are validated only at the emitted
  feature's compile, not while authoring — the **§8 #10 glue-typing decision**
  (lambda-leaf vs string), explicitly **out of scope** here.
- **Convention-derived names.** Feature name = command minus `"Command"`; namespace =
  `Acme.{Aggregate}s`. Cheap leverage, but it means the author gives up naming control
  unless we add an optional override (deliberately not added — YAGNI for the probe).
- **Same residuals as the slice** carry over unchanged: hardcoded open-namespaces,
  resolution off the runtime TPA list, mint renders twice, emit is a console tool not
  an in-build generator, no reconciliation on re-emit.
