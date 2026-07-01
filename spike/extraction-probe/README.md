# Extraction probe — typed components + real-code glue (no strings)

> **This is the answer to the rejected leverage probe.** There, leverage was bought with
> stringly-typed free text (`key`/`command`/`models`/`with` as strings), which broke
> type-safety, structure, and swappability. Here we get the **same leverage** with **zero
> business strings**: the scaffold is extracted into a **typed component** (generics fix the
> variant types) and the business glue is **real, compiler-checked C#** (lambdas), lifted
> into the owned handler via Roslyn. Type creation stays the **closed** inline source-gen
> path (probe A/E) — not re-proven here.

## The exercise (what the user asked for)

Take the real feature and **extract its reusable bits into composed objects**, the way you'd
wrap an endpoint: hardcode the invariants, push the variant types into generics/params, and
leave one slot for the per-feature logic — **as real code, not a string**. New types come from
the inline mint we already proved. See how the surface falls out.

## The progressive extraction — from hand-written to composed

Start from the hand-written handler (`evidence/baseline-handwritten.cs`) and pull bits out:

| Step | What we extract | Where it goes |
|---|---|---|
| 0 | the raw handler: load → ask → mutate → save → return, + the records | hand-written, 17 lines |
| 1 | **load / save / return** ceremony | into `Mutation<TAgg,TCmd>` — *derived from the generics*, never authored |
| 2 | **the aggregate + command + return types** | **generic arguments** `Mutates<User, AddItemCommand>()` — checked, not spelled in a string |
| 3 | **the repo DI registration** | falls out of the scaffold's own `scope.Get<Repo<TAgg>>` marker (probe B wiring) |
| 4 | **the records (CartItem, command)** | the **inline source-gen mint** (probe A/E) — the closed path, out of scope here |
| — | **what's left = the irreducible business logic** | a **typed lambda** the author writes: `LoadBy(...)` + `With(...)` |

What survives in the author's hands is exactly the divergence — and it's **real code**:

```csharp
public static Mutation<User, AddItemCommand> AddItemToCart() => Feature
    .Mutates<User, AddItemCommand>()          // variant TYPES — generic args, checked
    .LoadBy(cmd => cmd.Email)                  // how to FIND — real code, checked
    .With((cart, cmd, ctx) =>                  // the divergence — real code, checked
    {
        var book = ctx.Ask<BookDetails>(new BookDetailsQuery(cmd.BookId));
        cart.AddToCart(new CartItem(cmd.BookId, cmd.Qty, book.Price, book.Label));
    });
```

Every token is type-checked against the real domain. Rename `Email`, mistype `AddToCart`, or
pass the wrong type to `CartItem` and **the recipe does not compile**. Nothing is free text;
nothing can silently drift. That is "structure over rules, type-safety" made literal.

## The new mechanism — lift real code, don't execute it

The glue is a compiler-checked lambda, but the emitter cannot recover clean source from a
compiled delegate. So it reads the lambda's **syntax** from the recipe file and splices it into
the owned handler, **renaming** the author's natural parameter names to the canonical ones
(`Lift.cs`). This is the "merge declaration" / semantic-model tech the spike validated
(probe D), pointed at lambda bodies.

```
typed recipe ──build──►  compiler type-checks the glue (no strings to drift)
     │
     └──emit──► Lift reads the recipe SOURCE, lifts LoadBy + With bodies,
                renames (cart/cmd/ctx → agg/command/scope), splices into the scaffold
                          │
                          ▼  emitted/AddItem.Handler.cs  (owned, byte-for-byte deterministic)
                          ▼  build + run → add an item to ada's cart → 1 → 2; wiring load-bearing
```

The emitted handler is **byte-identical** to the leverage probe's output — same owned code,
same behaviour — but the authored surface upstream is now fully type-safe.

## What's authored vs derived — every emitted line mapped

`emitted/AddItem.Handler.cs`:

| Emitted line | Source |
|---|---|
| `Handle(Scope scope, AddItemCommand command)` signature | **scaffold** (shape) + `TCmd` generic |
| `var __repo = scope.Get<Repo<User>>();` | **scaffold**, derived from `TAgg` |
| `var agg = __repo.Load(command.Email);` | scaffold + **lifted** `LoadBy` selector (renamed) |
| `var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));` | **lifted** `With` (renamed `ctx`→`scope`, `cmd`→`command`) |
| `agg.AddToCart(new CartItem(command.BookId, command.Qty, book.Price, book.Label));` | **lifted** `With` (renamed `cart`→`agg`, `cmd`→`command`) |
| `__repo.Save();` / `return agg;` | **scaffold**, derived |
| `EnsureService<Repo<User>>()` | **scaffold**'s own `scope.Get` marker, auto-wired (probe B) |
| `EnsureQuery<BookDetails>()` | the **lifted** glue's `scope.Ask`, auto-wired (probe B) |
| `using Probe.Domain; namespace Acme.Users;` | derived (probe D rendering + convention) |

## Result (the deliverable)

`evidence/03-surface-area.txt`, reproduced by `evidence/measure.py`:

| version | lines | tokens | **business strings** |
|---|---:|---:|---:|
| rejected string motif (leverage) | 9 | 37 | **4** |
| **typed extraction (this probe)** | **9** | 98 | **0** |
| hand-written baseline | 17 | 147 | 0 |

**The headline is the last column, not lines.** This probe matches the rejected motif's line
count (9) and stays well under hand-written (17), but carries **zero free-text business
strings** where the rejected version carried four (the `key`, `command`, `models`, and `with`
text). Every part of the authored surface is now a generic argument or a compiler-checked
lambda.

**Honest nuance on tokens.** The typed recipe shows *more* tokens (98) than the rejected string
motif (37) — because a collapsed string literal counts as one token while the same logic
written as real code exposes every checked identifier and operator. That is not a regression:
those tokens are the *same business logic*, now **verified by the compiler instead of trusted
as text**. It is still below the hand-written baseline (147), so the surface did not blow up —
we recovered type-safety essentially for free on lines, at the cost of token *visibility* (the
right trade).

## How many ways — the design space (`src/Shapes.cs`)

The extraction is shape-independent. Three author surfaces, all type-checked, all expressing the
same feature with the same real-code glue:

| Shape | Surface | Lift target |
|---|---|---|
| **A** record of typed delegates | `new Mutation<User,AddItemCommand>(loadBy: …, with: …)` | lambda arg |
| **B** fluent builder *(the measured one)* | `Feature.Mutates<…>().LoadBy(…).With(…)` | lambda arg |
| **C** generic base + override (the "wrap the endpoint" form) | `class AddItem : MutationFeature<User,AddItemCommand> { override With(…) }` | override method body |

`src/Shapes.cs` compiles A and C as proof the principle holds regardless of ergonomics; the
emitter lowers B. Choosing between them is an ergonomics decision, not a mechanism one.

## How to run

```
dotnet run --project src -- prove        # live → emit → detach → override self-check (PROVE: PASS)
dotnet run --project src -- emit         # just emit the owned handler to emitted/
dotnet run --project emitted-feature-proof   # build + run the EMITTED feature
python3 evidence/measure.py              # reproduce the surface-area + string-count table
```

Layout:

```
src/
  Feature.cs        THE EXTRACTION: Mutation<TAgg,TCmd> + the builder (written once, reused)
  AuthoredRecipe.cs THE authored recipe (Shape B) — the measured, type-safe surface
  Shapes.cs         the design space — Shapes A & C, compile-only
  Lift.cs           THE NEW MECHANISM: lift + rename the real-code glue via Roslyn
  Emitter.cs        scaffold (derived from types) + lifted glue → owned handler
  Domain.cs         the world the glue is type-checked against (reused infra)
  Minted.cs         CartItem + command — STAND-IN for the source-gen mint (probe A/E), not author surface
  ResolutionModel.cs WiringScan.cs Net.cs   reused probe-D/B machinery
  Program.cs        live | emit | prove
emitted/            the COMMITTED owned handler emitted from the typed recipe
emitted-feature-proof/  compiles emitted/*.cs verbatim + drives it (build + run proof)
evidence/           prove output, emitted-run output, surface table, emitted snapshot, baseline
```

## Honest limitations

- **Minting is assumed, not re-proven.** `CartItem` / `AddItemCommand` are hand-declared in
  `Minted.cs` to stand in for the closed source-gen path (probe A inline mint + probe E
  forward-ref). In the real flow the generator mints them from a field declaration and the
  author writes neither the records nor a string for them. This probe deliberately does not
  re-run that generator; it focuses on the *new* thing (typed extraction + real-code glue).
- **The rename is syntactic.** `Lift.cs` replaces identifier tokens matching a lambda parameter
  name. For this recipe the parameter names (`cart`/`cmd`/`ctx`) are unique within the bodies,
  so it is exact — but a robust version resolves the parameter *symbols* via the semantic model
  (probe D) so only bound references move. Easy to upgrade; not needed to prove the mechanism.
- **The emitter correlates recipe-object → source by convention.** It parses the one known
  recipe file and finds the single `LoadBy`/`With` chain. Production would key the lift by the
  recipe method's symbol; trivial for one recipe, a real lookup at scale.
- **One motif only.** `Mutation<TAgg,TCmd>` carries the aggregate-mutation scaffold and only
  that. Projection / reactor / ingest features need their own typed components — terseness is
  layered per pattern, not universal (the `authoring-dialects.md` scorecard already said so).
- **Same residuals as the earlier probes** carry over: resolution off the runtime TPA list,
  convention-derived names (feature = command minus `Command`, namespace = `Acme.{Aggregate}s`),
  emit is a console tool not an in-build generator, no reconciliation on re-emit.
