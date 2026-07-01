namespace Probe;

// ============================================================================
// THE ARGS-PLAY — one uniform RECIPE contract, provider-specific input, uniform output.
//
//   IStore<TArgs, TReturn> :  TReturn Get(TArgs)  /  Save(TArgs, TReturn)
//
// This interface is RECIPE-ONLY. It exists so (a) the Mutate snippet compiles against
// canonical verbs (Get/Save) and (b) the swap type-checks: the concrete provider FIXES
// its input type (TArgs) and stays generic over the aggregate (TReturn), so the `key`
// selector is forced to produce THIS provider's TArgs. It never appears in emitted code
// and carries NO lowering metadata — how a provider lowers to real Load/Store lives in
// the tooling (StoreCatalog), not on the type we hand to the recipe author.
//
//   Repo<User>   : IStore<string,    User>   -> Get(string)    -> User   (key = string)
//   Bucket<User> : IStore<BucketKey, User>   -> Get(BucketKey) -> User   (key = a path)
// ============================================================================

public readonly record struct BucketKey(string Path);

public interface IStore<TArgs, TReturn> where TReturn : notnull
{
    TReturn Get(TArgs args);
    void Save(TArgs args, TReturn value);
}

// Provider A — a keyed repository. Idiomatic surface: Load / Store by string key.
public sealed class Repo<T> where T : notnull
{
    readonly Dictionary<string, T> _store = new();
    public Repo<T> Seed(string key, T value) { _store[key] = value; return this; }
    public T Load(string key) => _store.TryGetValue(key, out var v)
        ? v : throw new KeyNotFoundException($"No {typeof(T).Name} at key '{key}'.");
    public void Store(string key, T value) => _store[key] = value;
}

// Provider B — an object bucket. Idiomatic surface: Download / Upload by BucketKey.
public sealed class Bucket<T> where T : notnull
{
    readonly Dictionary<string, T> _store = new();
    public Bucket<T> Seed(BucketKey key, T value) { _store[key.Path] = value; return this; }
    public T Download(BucketKey key) => _store.TryGetValue(key.Path, out var v)
        ? v : throw new KeyNotFoundException($"No {typeof(T).Name} at bucket path '{key.Path}'.");
    public void Upload(BucketKey key, T value) => _store[key.Path] = value;
}

// The IStore adapters — the UNIFORM contract the recipe/builder type-checks against.
// They map the canonical recipe verbs (Get/Save) onto each provider's idiomatic methods;
// the emitted code calls the idiomatic methods directly, so these adapters are authoring-
// time only. NO Lowering here — that mapping is emitter-side (StoreCatalog).
public sealed class RepoStore<T>(Repo<T> repo) : IStore<string, T> where T : notnull
{
    public T Get(string args) => repo.Load(args);
    public void Save(string args, T value) => repo.Store(args, value);
}

public sealed class BucketStore<T>(Bucket<T> bucket) : IStore<BucketKey, T> where T : notnull
{
    public T Get(BucketKey args) => bucket.Download(args);
    public void Save(BucketKey args, T value) => bucket.Upload(args, value);
}

// Sugar so the recipe reads `via: Stores.Repository<User>()` / `via: Stores.BucketStore<User>()`.
// The emitter recognises the FACTORY name (Repository / BucketStore) to resolve the lowering.
public static class Stores
{
    public static IStore<string, T> Repository<T>() where T : notnull => new RepoStore<T>(new Repo<T>());
    public static IStore<BucketKey, T> BucketStore<T>() where T : notnull => new BucketStore<T>(new Bucket<T>());
}
