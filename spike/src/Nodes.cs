using System.Collections;
using System.Runtime.CompilerServices;

namespace Spike;

// The normalized contract (the Func/Action analogue):
//   Expr<T> — produces a value of type T
//   IStmt   — produces statement(s)
// Recipe fills are typed against these, so composition is checked at authoring time.
interface IStmt;

// Expr is a base CLASS, not an interface, so it can host the implicit literal conversion
// (a user-defined conversion can't target an interface — CS0552). `out T` covariance is given
// up by the flip, but it is inert for the value-typed T this models (int/bool), so nothing is lost.
abstract record Expr<T>
{
    public static implicit operator Expr<T>(T value) => new Lit<T>(value);
}

// A typed constant. Generic, mirroring Var<T>: 0 -> Lit<int>, true -> Lit<bool>. ILit lets the
// generator read the value without knowing T.
interface ILit
{
    object Value { get; }
}

sealed record Lit<T>(T Value) : Expr<T>, ILit
{
    object ILit.Value => Value!;
}

// A variable handle: identity + type, created once as a local and threaded into the nodes that
// declare or reference it. It is itself an Expr<T>, so a use site is the bare handle — there is
// no Ref wrapper. Binding vs use is told apart by the FIELD type (Var<T> binds, Expr<T> uses).
interface IVar
{
    string Name { get; }
}

sealed record Var<T>(string Name) : Expr<T>, IVar;

// A sequence of statements. The recipe root and a block-region fill. [CollectionBuilder] lets a
// recipe author a block as a collection expression [...]; the public ctor (new Block(...)) still works.
[CollectionBuilder(typeof(Blocks), nameof(Blocks.Create))]
sealed record Block(params IStmt[] Statements) : IEnumerable<IStmt>
{
    public static Block Of(string id) =>
        throw new InvalidOperationException($"Block.Of(\"{id}\") is a compile-time placeholder; never executed.");

    public IEnumerator<IStmt> GetEnumerator() => ((IEnumerable<IStmt>)Statements).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

static class Blocks
{
    public static Block Create(ReadOnlySpan<IStmt> statements) => new(statements.ToArray());
}

// The snippet-backed recipe nodes (DefineNode, AddNode, …) and their factories (Recipe.Loop, …)
// are SOURCE-GENERATED from the [Snippet] methods into Nodes.Generated.cs / Factories.Generated.cs.
// Regenerate with: dotnet run --project spike/gen
