namespace Probe;

// ============================================================================
// THE ARGS-PLAY — one uniform contract, provider-specific input, uniform output.
//
//   IStore<TArgs, TReturn> :  TReturn Get(TArgs)  /  Save(TArgs, TReturn)
//
// The concrete provider FIXES its input type (TArgs) and stays generic over the
// aggregate it returns (TReturn). So:
//
//   Repo<User>   : IStore<string,    User>   -> Get(string)    -> User   (key = string)
//   Bucket<User> : IStore<BucketKey, User>   -> Get(BucketKey) -> User   (key = a path)
//
// SAME output (the aggregate), DIFFERENT-but-typed input. Because Mutate requires
// this interface, the `key` selector must produce THIS provider's TArgs — so swapping
// the provider forces the key to match, at compile time. That is the whole point.
//
// `Save` also takes TArgs: a key-addressed store (Bucket) needs the key to put the
// value back; a tracked store (Repo) just ignores it. Uniform surface either way.
// ============================================================================

public readonly record struct BucketKey(string Path);

// How the emitter renders load/save for THIS provider — the per-provider lowering.
// The interface question is answered by IStore; this is the (orthogonal) lower flag:
// the emitted handler calls each provider's IDIOMATIC methods, not Get/Save.
public sealed record StoreLowering(string ParamType, string Param, string LoadMethod, string SaveMethod, string Tag);

public interface IStore<TArgs, TReturn> where TReturn : notnull
{
    TReturn Get(TArgs args);
    void Save(TArgs args, TReturn value);
    StoreLowering Lowering { get; }
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
// (Kept separate from the concrete classes so the emitted code can call the idiomatic
// methods directly; IStore exists for authoring-time type-safety, not for the output.)
public sealed class RepoStore<T>(Repo<T> repo) : IStore<string, T> where T : notnull
{
    public T Get(string args) => repo.Load(args);
    public void Save(string args, T value) => repo.Store(args, value);
    public StoreLowering Lowering => new($"Repo<{typeof(T).Name}>", "repo", "Load", "Store", "Repo");
}

public sealed class BucketStore<T>(Bucket<T> bucket) : IStore<BucketKey, T> where T : notnull
{
    public T Get(BucketKey args) => bucket.Download(args);
    public void Save(BucketKey args, T value) => bucket.Upload(args, value);
    public StoreLowering Lowering => new($"Bucket<{typeof(T).Name}>", "bucket", "Download", "Upload", "Bucket");
}

// Sugar so the recipe reads `via: Repo<User>()` / `via: Bucket<User>()`.
public static class Stores
{
    public static IStore<string, T> Repository<T>() where T : notnull => new RepoStore<T>(new Repo<T>());
    public static IStore<BucketKey, T> BucketStore<T>() where T : notnull => new BucketStore<T>(new Bucket<T>());
}
