// === SCENARIO 3, THE CORNER CASE (the genuine break) ===
// This project wants to reference `Foo` — a type that some OTHER recipe will mint —
// but it neither runs the generator nor references the assembly that mints `Foo`.
// `Foo` is therefore an unresolved NAME at this compilation. There is no symbol to
// type-check `<T>`-style against, so the compiler rejects it: CS0246.
//
// This is the precise boundary where a name-based, value-level TypeRef ("Foo" as a
// STRING) is the ONLY representation that can compile — because a real <T> demands a
// real symbol, and here no symbol exists in or visible to this compilation.
//
// The exact same shape arises with a CIRCULAR producer dependency: if A's mint needs a
// type B mints and B's mint needs a type A mints, neither assembly can be built first,
// so within each compilation the other's type is an unresolved name — same CS0246, same
// conclusion: cross-producer references must be carried by value (TypeRef), not <T>.

// A field typed `Foo` — but `Foo` is undefined in this compilation.
internal sealed class WantsFoo
{
    public Foo? Value { get; set; }            // CS0246: type or namespace 'Foo' not found
    public List<Foo> All { get; } = new();     // CS0246
}
