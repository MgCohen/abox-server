using System.Runtime.CompilerServices;

namespace Probe;

// The snippet catalog. Each method is REAL, compiling, type-checked C# — its body IS the
// template (exactly like the original spike's Loop -> for). The methods are never invoked;
// they exist to be authored/validated by the compiler and parsed by the emitter.
//
// Mutate's fills:
//   @store  (marker) : the provider receiver name (repo / bucket) — from the StoreCatalog.
//   key     (param)  : the lifted key expression (command.Email / new BucketKey(...)) — glue.
//   body    (block)  : Block.Of("body") -> the lifted body statements — glue + business logic.
//
// The canonical recipe verbs .Get / .Save are REWRITTEN to the provider's idiomatic methods
// (Repo -> Load/Store, Bucket -> Download/Upload) at lower — the conversion that leaving
// IStore freed. Because the scaffold is real C#, it is compiler-checked and editable, not a
// string built in the emitter.
static class Snippets
{
    [Snippet("mutate")]
    public static TReturn Mutate<TArgs, TReturn>(IStore<TArgs, TReturn> @store, TArgs key)
        where TReturn : notnull
    {
        var __key = key;
        var agg = @store.Get(__key);
        Block.Of("body");
        @store.Save(__key, agg);
        return agg;
    }

    // The emitter reads snippets from THIS file (CallerFilePath captured inside the file it
    // refers to). Editing a snippet flows through on the next run — no emitter change.
    public static string SourcePath { get; } = Capture();

    static string Capture([CallerFilePath] string path = "") => path;
}
