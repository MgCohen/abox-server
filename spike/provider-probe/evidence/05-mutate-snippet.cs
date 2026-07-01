using System.Runtime.CompilerServices;

namespace Probe;

// The snippet catalog. Each method is REAL, compiling, type-checked C# — its body IS the
// template (exactly like the original spike's Loop -> for). The methods are never invoked;
// they exist to be authored/validated by the compiler and parsed by the emitter.
//
// Mutate lowers by turning its PARAMS into locals (store <- the `store:` arg, key <- the
// `key:` arg) and filling Block.Of("body") with the lifted body. store.Get / store.Save
// SURVIVE verbatim — the store is a real component, so there is nothing to translate and no
// __key alias to invent.
static class Snippets
{
    [Snippet("mutate")]
    public static TReturn Mutate<TArgs, TReturn>(IStore<TArgs, TReturn> store, TArgs key)
        where TReturn : notnull
    {
        var agg = store.Get(key);
        Block.Of("body");
        store.Save(key, agg);
        return agg;
    }

    // The emitter reads snippets from THIS file (CallerFilePath captured inside the file it
    // refers to). Editing a snippet flows through on the next run — no emitter change.
    public static string SourcePath { get; } = Capture();

    static string Capture([CallerFilePath] string path = "") => path;
}
