namespace Probe;

// The per-provider lowering that LEFT IStore. Keyed by the FACTORY name the recipe named in
// `via:` (Stores.Repository -> "Repository", Stores.BucketStore -> "BucketStore"). Maps the
// canonical recipe verbs to each provider's idiomatic surface + the emitted parameter shape.
//
// This is the "conversion that happens in our tooling" — recipe world never sees it, and the
// store type carries none of it.
static class StoreCatalog
{
    public sealed record Provider(string ParamTypeFormat, string Receiver, string Load, string Save, string Tag)
    {
        public string ParamType(string aggregate) => string.Format(ParamTypeFormat, aggregate);
        public IReadOnlyDictionary<string, string> VerbMap => new Dictionary<string, string>
        {
            ["Get"] = Load,
            ["Save"] = Save,
        };
    }

    static readonly Dictionary<string, Provider> ByFactory = new()
    {
        ["Repository"] = new("Repo<{0}>", "repo", "Load", "Store", "Repo"),
        ["BucketStore"] = new("Bucket<{0}>", "bucket", "Download", "Upload", "Bucket"),
    };

    public static Provider Resolve(string factory) =>
        ByFactory.TryGetValue(factory, out var p)
            ? p
            : throw new InvalidOperationException(
                $"no store lowering registered for factory '{factory}'. Add it to StoreCatalog.");
}
