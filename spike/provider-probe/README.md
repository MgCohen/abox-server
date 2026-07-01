# Provider probe — pluggable stores via a surviving store + snippet Mutate

> One recipe shape, two owned handlers, and swapping the provider forces the key to match at
> compile time — under four corrections from the last cut: the store **survives** (no lowering
> table, no string bag); the `Mutate` scaffold is a real **`[Snippet]`** with no `__key`; the
> store is **static** (swap = one `store:` argument); and `Mutate` **takes no scope** — the
> command is reached through the ambient `scope.Command` in the business-logic leaves.

## What changed from the previous cut

| Concern | Previous cut (rejected) | This cut |
|---|---|---|
| `ByFactory` dict + `Provider` string-bag | store told the emitter its idiomatic `Load/Store` via a string record | **Deleted.** The store *survives* as `IStore`; the handler calls uniform `store.Get/Save`; `Load/Download` lives *inside* the adapter. No table, no verb rewrite. |
| `__key` alias in the snippet | `var __key = key;` | gone — a snippet param lowers to a clean `var key = …;` local |
| provider baked into the emitter | two code paths (Repo vs Bucket) | **one path** — swapping `Stores.Repository` → `Stores.BucketStore` is the entire change; the store is a static singleton |
| `Mutate(scope, …)` took scope | scope threaded through the action | **`Mutate(store, key, body)`** — no scope; the leaves read `scope.Command` |

## The store survives (`src/Store.cs`)

```csharp
interface IStore<TArgs, TReturn> { TReturn Get(TArgs args); void Save(TArgs args, TReturn value); }

Repo<User>   : IStore<string,    User>   //  Get(string)    -> User    (Load/Store inside)
Bucket<User> : IStore<BucketKey, User>   //  Get(BucketKey) -> User    (Download/Upload inside)

// STATIC stores — one singleton per aggregate. Swapping these two is the whole recipe delta.
Stores.Repository<User>()   // -> the same IStore<string,User> every call
Stores.BucketStore<User>()  // -> the same IStore<BucketKey,User> every call
```

The idiomatic `Load/Download` is hidden in the adapter — the emitted code never mentions it,
so there is nothing for the emitter to translate and no per-provider table.

## The scaffold is an authored snippet, no `__key` (`src/Snippets.cs`)

```csharp
[Snippet("mutate")]
public static TReturn Mutate<TArgs, TReturn>(IStore<TArgs, TReturn> store, TArgs key)
    where TReturn : notnull
{
    var agg = store.Get(key);        // store.Get / store.Save SURVIVE verbatim
    Block.Of("body");                // body slot <- lifted body
    store.Save(key, agg);
    return agg;
}
```

The emitter lowers it generically: each **param becomes a `var <param> = <fill>;` local**
(`store` ← the `store:` arg, `key` ← the `key:` arg), and `Block.Of("body")` ← the lifted
body. No scaffold statement is ever spelled by the emitter.

## Builder/action surface — `Mutate` has no scope (`src/RepoRecipe.cs` / `BucketRecipe.cs`)

```csharp
// Repo                                              // Bucket
new Feature<AddPointsCommand>(scope =>               new Feature<AddPointsCommand>(scope =>
    Mutate(store: Stores.Repository<User>(),             Mutate(store: Stores.BucketStore<User>(),
           key:   scope.Command.Email,        // string    key:   new BucketKey(scope.Command.Region), // BucketKey
           body:  user => user.AddPoints(scope.Command.Points))); body: user => user.AddPoints(scope.Command.Points));
```

`Feature` (builder) provides `scope`; `Mutate` (action) takes only `(store, key, body)`. The
command is reached by the leaves via `scope.Command` — not threaded through the action.

## Two owned handlers, emitted from that one shape (`emitted/`)

```csharp
// AddPoints.Repository.Handler.cs                  // AddPoints.BucketStore.Handler.cs
Handle(AddPointsCommand command)                    Handle(AddPointsCommand command)
{                                                   {
    var store = Stores.Repository<User>();              var store = Stores.BucketStore<User>();
    var key = command.Email;                            var key = new BucketKey(command.Region);
    var agg = store.Get(key);                           var agg = store.Get(key);        // ← identical
    agg.AddPoints(command.Points);                      agg.AddPoints(command.Points);   // ← identical
    store.Save(key, agg);                               store.Save(key, agg);            // ← identical
    return agg;                                         return agg;                      // ← identical
}                                                   }
```

Only the store binding and the key line differ. The load/mutate/save/return are byte-identical
— the same standard `Mutate`, a different static store.

## The type-safe swap — the load-bearing result (`evidence/01`)

```
[PASS] MATCHED key (BucketKey) compiles against a Bucket store
[PASS] MISMATCHED key (string) is REJECTED against a Bucket store
       compiler said: The type arguments for method
       'Compose.Mutate<TArgs, TReturn>(IStore<TArgs, TReturn>, TArgs, Action<TReturn>)'
       cannot be inferred from the usage.
```

`key: TArgs` demands the store's arg type, so a string key against a `Bucket` store does not
compile.

## How to run

```
dotnet run --project src -- prove            # emit both + assert outputs + the rejected swap → PROVE: PASS
dotnet run --project src -- emit             # just emit the two handlers
dotnet run --project emitted-feature-proof   # seed the static stores + run BOTH handlers
```

## Honest limitations

- **The store is a static singleton.** Real wiring would resolve it from DI; here a static
  accessor stands in so the handler needs no injected parameter.
- **Only the scaffold *body* is snippet-authored.** The class/method frame is still assembled by
  the emitter — that is the builder's (`Feature`/`Endpoint`) job, and snippet-ising the frame is
  the next step, not done here.
- **`Save(TArgs, TReturn)`** — a key-addressed store (Bucket) needs the key to write back; a
  tracked store (Repo) ignores it. The aggregate-carries-identity alternative is a per-domain
  choice, not tested here.
- **Providers here have a matched-arity surface** (Get/Save both take the key). An event store
  (`Rehydrate` vs `Append(changes)`) would need a different snippet, not a different table.
