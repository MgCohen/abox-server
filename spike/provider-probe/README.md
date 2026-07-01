# Provider probe — pluggable stores via the Args-play

> Validates the **shape** for swapping a component's provider: `Mutate(via: …)` where the
> store is swappable (Repo ↔ Bucket), the **output is uniform** (the aggregate), the **input
> varies but is typed** (each provider fixes its own key type), and swapping the provider
> **forces the key to match at compile time**. One recipe shape → two different owned handlers.

## The question this answers

Providers for the same aggregate have the **same output** (you get a `User` back) but
**vastly different inputs** (a repo loads by a string key; a bucket loads by a path). Do they
need one interface? **Yes — because the `Mutate` scaffold requires it** (if the store were
wired by hand in the body, no; but then the scaffold wouldn't own load/save and the swap
wouldn't be checked). The **Args-play** makes that one interface work.

## The Args-play (`src/Store.cs`)

One contract, two type parameters: the concrete provider **fixes the input**, stays **generic
over the output**.

```csharp
interface IStore<TArgs, TReturn> { TReturn Get(TArgs args); void Save(TArgs args, TReturn value); … }

Repo<User>   : IStore<string,    User>   //  Get(string)    -> User
Bucket<User> : IStore<BucketKey, User>   //  Get(BucketKey) -> User
```

`Mutate` requires `IStore<TArgs,TReturn>` and takes `key: Func<TCmd,TArgs>` — so **the key
must produce THIS provider's `TArgs`.** `TArgs`/`TReturn` are inferred from `via`; the author
writes only the provider, the key, and the body.

## One recipe shape, one token different

```csharp
// src/RepoRecipe.cs                              // src/BucketRecipe.cs
Feature.For<AddPointsCommand>().Mutate(           Feature.For<AddPointsCommand>().Mutate(
    via:  Stores.Repository<User>(),                  via:  Stores.BucketStore<User>(),
    key:  c => c.Email,              // string        key:  c => new BucketKey(c.Region), // BucketKey
    body: (user, c) => user.AddPoints(c.Points));     body: (user, c) => user.AddPoints(c.Points));
```

Only `via` (and the key it forces) changes. The **body is byte-identical** — it doesn't know
which provider is behind it.

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

Same shape, same body, same output type (`User`). The **load/save lines and the key type
differ per provider** — derived from the provider's `Lowering`, and the key bound once to
`__key` and threaded into both. Both compile and run (`emitted-feature-proof`, `evidence/02`).

## The type-safe swap — the load-bearing result (`evidence/01`)

The probe compiles a Bucket recipe with a **mismatched** `string` key and asserts the compiler
rejects it:

```
[PASS] MATCHED key (BucketKey) compiles against a Bucket provider
[PASS] MISMATCHED key (string) is REJECTED against a Bucket provider
       compiler said: The type arguments for method 'FeatureBuilder<…>.Mutate<TArgs,TReturn>(…)'
                      cannot be inferred from the usage.
```

**You cannot swap the provider and forget to fix its input — it does not compile.** That is the
guarantee the shape was meant to deliver: same interface, same output, different-but-typed
input, minimal surface.

## How to run

```
dotnet run --project src -- prove            # emit both + assert outputs + the rejected swap → PROVE: PASS
dotnet run --project src -- emit             # just emit the two handlers
dotnet run --project emitted-feature-proof   # build + run BOTH emitted handlers
```

## Honest limitations

- **`TCmd` is supplied by `Feature.For<TCmd>()`.** In the real system the wrapper (`Endpoint<…>`)
  supplies the command type; here `For<TCmd>()` stands in for that. This is the **binding surface**
  — the one bit of generic threading still to pin, orthogonal to the Args-play.
- **`Save` takes the args too** (`Save(TArgs, TReturn)`). A key-addressed store (Bucket) needs the
  key to write back; a tracked store (Repo) ignores it. The alternative — the aggregate carries its
  own identity so `Save(TReturn)` suffices — is a per-domain choice, not tested here.
- **Providers here have a matched-arity idiomatic surface** (Load/Store, Download/Upload). A provider
  whose load and save are shaped differently (e.g. an event store: `Rehydrate` vs `Append(changes)`)
  would need a richer `Lowering` than the four method-name fields used here.
- **Minting, wiring, and cross-module Ask are out of scope** — this probe isolates the provider swap.
  The emitted handler takes its provider as a parameter rather than resolving it (DI wiring is probe B).
