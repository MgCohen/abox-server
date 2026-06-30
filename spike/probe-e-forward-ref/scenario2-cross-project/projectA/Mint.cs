// === SCENARIO 2, PROJECT A (the producer assembly) ===
// Project A mints `Foo` from a CreateModel(...) marker. The generated record is
// `public record Foo(...)`, so it becomes part of A's EMITTED ASSEMBLY — an ordinary
// public type any referencing assembly can see. Nothing here is special to B.
namespace ProjectA;

public static class Mint
{
    public static void Declare()
    {
        Models.CreateModel("Foo", ("Id", typeof(System.Guid)), ("Name", typeof(string)));
    }

    // Prove A itself can use its own minted type (and that the type is genuinely public
    // by surfacing it through a public API B will call).
    public static object MakeFoo(System.Guid id, string name) => new Foo(id, name);
}
