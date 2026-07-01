namespace ProbeD;

// The end-to-end sample, authored as a typed recipe (the same shape the spike already uses). Its
// fields deliberately exercise every hard type case the reflection `Type.FullName` path got wrong:
//
//   int                          alias / primitive       (FullName: System.Int32)
//   string                       alias / framework        (FullName: System.String)
//   Guid                         framework value type     (FullName: System.Guid)
//   Outer.Inner                  USER-DEFINED nested       (FullName: ...Outer+Inner — the '+' mess)
//   List<int>                    generic                   (FullName: ...List`1[[System.Int32...]] backticks)
//   Dictionary<string, Guid>     generic, 2 args           (FullName: backtick + assembly-qualified args)
//   int?                         nullable VALUE            (FullName: Nullable`1[[System.Int32...]])
//   string?                      nullable REFERENCE        (no FullName distinction at all)
//   (int Count, string Name)     tuple w/ element names    (FullName: ValueTuple`2 — names lost)
//
// The author writes ONLY the recipe below. SupportTypes is the user's own type definitions that any
// recipe references — here the nested Outer.Inner — and is exactly what a real project would already
// have on disk; the renderer compiles against it to resolve the symbol.
static class Sample
{
    public static readonly RecordRecipe Recipe = new(
        Name: "Customer",
        Fields: new Field[]
        {
            new("Id",        "int"),
            new("Label",     "string"),
            new("Key",       "System.Guid"),
            new("Marker",    "ProbeD.Sample.Support.Outer.Inner"),
            new("Scores",    "System.Collections.Generic.List<int>"),
            new("Lookup",    "System.Collections.Generic.Dictionary<string, System.Guid>"),
            new("Age",       "int?"),
            new("Nickname",  "string?"),
            new("Summary",   "(int Count, string Name)"),
        });

    // The user-defined nested type the recipe references. Note: the author writes the FULLY-spelled
    // type text in the recipe ("...Outer.Inner") so resolution is unambiguous; the IDIOMATIC render
    // then SHORTENS it to `Outer.Inner` for the emitted output. That shortening is the renderer's job.
    public const string SupportTypes =
@"#nullable enable
namespace ProbeD.Sample.Support;

public static class Outer
{
    public sealed record Inner(int Tag);
}
";
}
