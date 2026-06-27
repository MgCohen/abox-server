namespace Spike;

// The normalized contract (the Func/Action analogue):
//   IExpr<T> — produces a value of type T
//   IStmt    — produces statement(s)
// Recipe holes are typed against these, so composition is checked at authoring time:
// an IExpr<int> hole rejects anything that isn't an int producer.
interface IStmt;

interface IExpr<out T>;

// Atoms — too trivial to be snippets; rendered directly by the generator.
sealed record Lit(int Value) : IExpr<int>;

sealed record Ref(string Name) : IExpr<int>;

// A sequence of statements. Doubles as the recipe root and as a body-hole filler.
sealed record Block(params IStmt[] Statements);

// Snippet-backed recipe nodes. HAND-WRITTEN for the spike; Step 2 source-generates these
// from the [Snippet] methods. Each field maps to a hole by name (camelCase of the field):
//   Var/Target/I (string) -> name holes      @var / @target / @i
//   Value/Count/A/B (IExpr) -> value holes    value / count / a / b
//   Body (Block)            -> the body hole   Slot.Of<Block>()
sealed record DefineNode(string Var, IExpr<int> Value) : IStmt;

sealed record AssignNode(string Target, IExpr<int> Value) : IStmt;

sealed record AddNode(IExpr<int> A, IExpr<int> B) : IExpr<int>;

sealed record LoopNode(IExpr<int> Count, string I, Block Body) : IStmt;

sealed record ReturnNode(IExpr<int> Value) : IStmt;
