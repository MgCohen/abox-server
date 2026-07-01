namespace Probe;

// ============================================================================
// THE ARGS-PLAY — one uniform contract, provider-specific input, uniform output.
//
//   IStore<TArgs, TReturn> :  TReturn Get(TArgs)  /  Save(TArgs, TReturn)
//
// The store is a REAL component that SURVIVES into emitted code (like scope.Ask, not like
// Loop): the handler calls store.Get / store.Save uniformly. Each concrete provider FIXES
// its input type (TArgs) and hides its idiomatic surface (Repo -> Load/Store, Bucket ->
// Download/Upload) INSIDE the adapter. So swapping the provider is swapping one `store:`
// argument — the same standard Mutate, no emitter translation table.
//
//   Repo<User>   : IStore<string,    User>   -> Get(string)    -> User   (key = string)
//   Bucket<User> : IStore<BucketKey, User>   -> Get(BucketKey) -> User   (key = a path)
//
// Because Mutate takes `key: TArgs`, the key expression must have THIS provider's TArgs —
// swap the store and a stale key is a plain type error (string vs BucketKey).
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
    public T Load(string key) => _store.TryGetValue(key, out var v)
        ? v : throw new KeyNotFoundException($"No {typeof(T).Name} at key '{key}'.");
    public void Store(string key, T value) => _store[key] = value;
}

// Provider B — an object bucket. Idiomatic surface: Download / Upload by BucketKey.
public sealed class Bucket<T> where T : notnull
{
    readonly Dictionary<string, T> _store = new();
    public T Download(BucketKey key) => _store.TryGetValue(key.Path, out var v)
        ? v : throw new KeyNotFoundException($"No {typeof(T).Name} at bucket path '{key.Path}'.");
    public void Upload(BucketKey key, T value) => _store[key.Path] = value;
}

// The adapters map the uniform verbs (Get/Save) onto each provider's idiomatic methods.
// The emitted code calls Get/Save; the idiomatic call lives HERE, not in a string table.
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

// STATIC stores — one singleton per aggregate type. `Stores.Repository<User>()` returns the
// SAME store every call, so the emitted handler reads/writes shared state without an injected
// parameter. Swapping Repository -> BucketStore in a recipe is the whole change.
public static class Stores
{
    public static IStore<string, T> Repository<T>() where T : notnull => RepoHolder<T>.Instance;
    public static IStore<BucketKey, T> BucketStore<T>() where T : notnull => BucketHolder<T>.Instance;

    static class RepoHolder<T> where T : notnull
    {
        public static readonly RepoStore<T> Instance = new(new Repo<T>());
    }

    static class BucketHolder<T> where T : notnull
    {
        public static readonly BucketStore<T> Instance = new(new Bucket<T>());
    }
}
