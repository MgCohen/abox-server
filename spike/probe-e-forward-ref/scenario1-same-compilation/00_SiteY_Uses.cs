// === SCENARIO 1, SITE Y (the USE site) ===
// This file is named 00_* so it sorts/compiles textually BEFORE the mint site
// (ZZ_SiteX_Mints.cs). It references `Foo` — a type that is minted by a DIFFERENT
// recipe site, in a DIFFERENT file, that appears LATER. If this compiles, a minted
// type is a real, first-class type across the whole compilation, with NO name-based
// TypeRef and NO ordering constraint. `Foo` is used here as:
//   - a field type
//   - the element type of a user-defined generic Repo<Foo>
//   - the element type of List<Foo>
namespace Scenario1;

internal sealed class FooHolder
{
    // A field typed `Foo` — the minted type used as a plain <T>-style type reference.
    public Foo? Current { get; private set; }

    private readonly Repo<Foo> _repo = new();
    private readonly List<Foo> _all = new();

    public int Add(Foo foo)
    {
        Current = foo;
        _repo.Add(foo);
        _all.Add(foo);
        return _repo.Count + _all.Count;
    }
}

// A user-defined generic, instantiated over the minted type elsewhere.
internal sealed class Repo<T>
{
    private readonly List<T> _items = new();
    public void Add(T item) => _items.Add(item);
    public int Count => _items.Count;
}
