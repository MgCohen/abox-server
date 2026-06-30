using static InlineTypes;

// === PROBE A: usage drives generation ===
// 1) Declare the needed types INLINE, as values (string name + (field, typeof(T)) tuples).
//    These are bare statements — no `var x = ...` is required.
CreateRecord("Foo", ("Id", typeof(Guid)), ("Name", typeof(string)));
CreateRecord("Point", ("X", typeof(int)), ("Y", typeof(int)));

// 2) Use the MINTED types as real types, later in the SAME compilation. This only
//    compiles because InlineRecordGenerator back-filled `public record Foo(...)` etc.
Foo f = new Foo(Guid.NewGuid(), "bar");
Console.WriteLine($"Foo  -> {f}");

Point p = new Point(3, 4);
Console.WriteLine($"Point-> {p}");

// 3) Bonus: a generic OVER a minted type must also bind.
List<Foo> many = new() { f, new Foo(Guid.NewGuid(), "baz") };
Console.WriteLine($"List<Foo> count = {many.Count}");

Repo<Foo> repo = new();
repo.Add(f);
Console.WriteLine($"Repo<Foo> count = {repo.Count}");

// A hand-written generic to prove a user-defined generic binds to the minted type too.
internal sealed class Repo<T>
{
    private readonly List<T> _items = new();
    public void Add(T item) => _items.Add(item);
    public int Count => _items.Count;
}
