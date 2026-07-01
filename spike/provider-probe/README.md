# Provider probe — pluggable stores via the Args-play (snippet-authored scaffold)

> Validates the **shape** for swapping a component's provider under three corrections:
> `IStore` is a **recipe-only** interface (no lowering pushed to the real world); the
> `Mutate` scaffold is a **real, compiling `[Snippet]`** (like the original spike's `Loop`),
> not string-built; and the surface is **builder/action** — `new Feature<Cmd>(scope =>
> Mutate(scope, …))`, where TCmd flows down the builder lambda. One recipe shape → two
> different owned handlers; swapping the provider forces the key to match at compile time.

## What changed from the first cut

| Concern | First cut (rejected) | This cut |
|---|---|---|
| `IStore` carried `Lowering` | store told the emitter its own load/save | **`IStore` is recipe-only** (canonical `Get`/`Save`); lowering lives in `StoreCatalog` (tooling) |
| scaffold string-built in `Emitter` | `sb.AppendLine("var agg = …")` | **`[Snippet("mutate")]`** real C# in `Snippets.cs`, substituted like `Loop` → `for` |
| surface `Feature.For<TCmd>().Mutate(…)` | `For<TCmd>()` stood in for the binding surface | **`new Feature<Cmd>(scope => Mutate(scope, …))`** — TCmd flows down the builder lambda |

## The Args-play (`src/Store.cs`) — now recipe-only

```csharp
// RECIPE interface. Canonical verbs so the Mutate snippet compiles + the swap type-checks.
// No lowering metadata. Never appears in emitted code.
interface IStore<TArgs, TReturn> { TReturn Get(TArgs args); void Save(TArgs args, TReturn value); }

Repo<User>   : IStore<string,    User>   //  Get(string)    -> User
Bucket<User> : IStore<BucketKey, User>   //  Get(BucketKey) -> User
```

The Repo→`Load/Store`, Bucket→`Download/Upload` mapping moved into **`src/StoreCatalog.cs`**,
keyed by the factory name the recipe used (`Stores.Repository` / `Stores.BucketStore`). That is
the "conversion that happens in tooling" — the store type carries none of it.

## The scaffold is an authored snippet (`src/Snippets.cs`)

```csharp
[Snippet("mutate")]
public static TReturn Mutate<TArgs, TReturn>(IStore<TArgs, TReturn> @store, TArgs key)
    where TReturn : notnull
{
    var __key = key;                 // key   (param)  <- lifted key expression
    var agg = @store.Get(__key);     // @store (marker) <- receiver; .Get <- provider load verb
    Block.Of("body");                // body  (block)  <- lifted body statements
    @store.Save(__key, agg);         //                   .Save <- provider save verb
    return agg;
}
```

Real, compiler-checked C# — the same template mechanism as the spike's `Loop`. The emitter
(`src/Emitter.cs`) parses this method, fills `@store`/`key`/`Block.Of("body")`, and rewrites
`.Get`/`.Save` to the provider's idiomatic methods. It does **not** string-build the scaffold.

## Builder/action surface — one token different (`src/RepoRecipe.cs` / `src/BucketRecipe.cs`)

```csharp
// Repo                                              // Bucket
new Feature<AddPointsCommand>(scope =>               new Feature<AddPointsCommand>(scope =>
    Mutate(scope,                                        Mutate(scope,
        via:  Stores.Repository<User>(),                    via:  Stores.BucketStore<User>(),
        key:  c => c.Email,           // string            key:  c => new BucketKey(c.Region), // BucketKey
        body: (user, c) => user.AddPoints(c.Points)));     body: (user, c) => user.AddPoints(c.Points))); // identical
```

`Feature` (builder) provides `scope`; `Mutate` (action) consumes it. TCmd flows from
`Feature<AddPointsCommand>` down into `Mutate` — the binding surface, no `For<TCmd>()` stand-in.

## Two owned handlers, emitted from that one shape (`emitted/`)

```csharp
// AddPoints.Repo.Handler.cs                      // AddPoints.Bucket.Handler.cs
Handle(Repo<User> repo, AddPointsCommand command) Handle(Bucket<User> bucket, AddPointsCommand command)
{                                                 {
    var __key = command.Email;                        var __key = new BucketKey(command.Region);
    var agg = repo.Load(__key);                       var agg = bucket.Download(__key);
    agg.AddPoints(command.Points);                    agg.AddPoints(command.Points);   // ← same body
    repo.Store(__key, agg);                           bucket.Upload(__key, agg);
    return agg;                                        return agg;                      // ← same output
}                                                 }
```

Byte-identical to the first cut's output — but every line now traces to the `mutate` snippet
(load/save/return) or a lifted expression (key, body), not to a `StringBuilder`.

## The type-safe swap — the load-bearing result (`evidence/01`)

```
[PASS] MATCHED key (BucketKey) compiles against a Bucket provider
[PASS] MISMATCHED key (string) is REJECTED against a Bucket provider
       compiler said: The type arguments for method
       'Compose.Mutate<TCmd, TArgs, TAgg>(Scope<TCmd>, IStore<TArgs, TAgg>, Func<TCmd, TArgs>, …)'
       cannot be inferred from the usage.
```

## How to run

```
dotnet run --project src -- prove            # emit both + assert outputs + the rejected swap → PROVE: PASS
dotnet run --project src -- emit             # just emit the two handlers
dotnet run --project emitted-feature-proof   # build + run BOTH emitted handlers
```

## Honest limitations

- **`new Mutate<>()` is not the leaf.** A C# constructor cannot infer the class type params
  (TCmd from `scope`, TArgs from `via`), so the leaf is the canonical free function
  `Mutate(scope, via, key, body)` — which infers all three — with `scope.Mutate(…)` as sugar
  over it. This matches "the catalog form is canonical, `scope.X` is generated sugar."
- **Only the scaffold *body* is snippet-authored.** The outer class/method frame is still
  assembled by the emitter — that is the builder's (`Feature`/`Endpoint`) job, and snippet-ising
  the frame is the next step, not done here.
- **`Save` takes the args too** (`Save(TArgs, TReturn)`). A key-addressed store (Bucket) needs the
  key to write back; a tracked store (Repo) ignores it. The aggregate-carries-identity alternative
  (`Save(TReturn)`) is a per-domain choice, not tested here.
- **Providers here have a matched-arity idiomatic surface** (Load/Store, Download/Upload). An event
  store (`Rehydrate` vs `Append(changes)`) would need a richer `StoreCatalog` entry than the four
  method-name fields used here.
- **Minting, wiring, and cross-module Ask are out of scope** — this probe isolates the provider swap.
