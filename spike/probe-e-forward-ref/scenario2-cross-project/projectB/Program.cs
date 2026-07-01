// === SCENARIO 2, PROJECT B (the consumer assembly) ===
// B references A and uses `Foo` — the generator-minted type from A's assembly — as a
// real type: `new Foo(...)`, `Repo<Foo>`, `List<Foo>`. B does not run the generator;
// it binds against the public `Foo` already emitted into A's DLL. This is the ordinary
// "referenced assembly exposes a public type" path; the type's provenance (generator vs
// hand-written) is invisible to B.

// `Foo` lives at the global namespace (the generator emits it top-level), so B sees it
// directly once it references A.
Foo f = new Foo(Guid.NewGuid(), "from-B");
Console.WriteLine($"Foo (in B)   -> {f}");

List<Foo> many = new() { f, new Foo(Guid.NewGuid(), "second") };
Console.WriteLine($"List<Foo> ct -> {many.Count}");

Repo<Foo> repo = new();
repo.Add(f);
Console.WriteLine($"Repo<Foo> ct -> {repo.Count}");

// And A's own public API that returns the minted type round-trips through B.
var viaA = ProjectA.Mint.MakeFoo(Guid.NewGuid(), "via-A-API");
Console.WriteLine($"A.MakeFoo    -> {viaA}");

Console.WriteLine("Scenario 2: cross-project, B references A => COMPILED & RAN");

internal sealed class Repo<T>
{
    private readonly List<T> _items = new();
    public void Add(T item) => _items.Add(item);
    public int Count => _items.Count;
}
