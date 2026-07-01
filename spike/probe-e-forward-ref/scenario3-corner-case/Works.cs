// === SCENARIO 3, THE TYPEREF WORKAROUND (what DOES compile at the corner) ===
// In the exact situation Break.cs fails (no symbol for `Foo` in or visible to this
// compilation), the only representation that compiles is a VALUE-LEVEL name: a
// `TypeRef` string. It carries the type's identity without demanding a symbol, so it
// type-checks trivially (it is just a string) and defers any real cross-use safety to
// the later compile gate, once the producing assembly exists and is referenced.
//
// This is README §8 #17's `TypeRef` made concrete: when you cannot name the type as
// `<T>` because its producing compilation isn't present, you name it by value.

// A minimal value-level TypeRef. No symbol required — pure data.
internal readonly record struct TypeRef(string Name);

internal sealed class WantsFooByRef
{
    // Instead of `Foo? Value` (which needs the `Foo` symbol), the reference is carried
    // as data. The recipe/emitter resolves it to a real symbol downstream, in a later
    // compilation where the producer IS referenced.
    public TypeRef Value { get; } = new("Foo");
    public List<TypeRef> All { get; } = new() { new("Foo"), new("Foo") };
}

internal static class Program
{
    private static void Main()
    {
        var w = new WantsFooByRef();
        System.Console.WriteLine($"TypeRef      -> {w.Value.Name}");
        System.Console.WriteLine($"List<TypeRef>-> {w.All.Count}");
        System.Console.WriteLine("Scenario 3: no producer in/visible to this compilation => <T> impossible; value-level TypeRef COMPILES & RAN");
    }
}
