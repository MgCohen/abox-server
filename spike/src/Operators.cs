namespace Spike;

// Lever E: author operator-shaped nodes as operators. These are C# 14 extension operators on the
// constructed Expr<int> — the only form that keeps the generic base AND scopes the operators to int
// (operators declared inside the open Expr<T> would leak onto every T). Declaring `<` requires `>`
// (CS0216), so each maps to its own faithful node — `>` renders `a > b`, not a flipped `<`.
// Supported operators are +, <, > only; equality is the Eq(...) factory (the `==` operator is a
// record-equality trap — see BUILDING-STYLE.md).
static class Operators
{
    extension(Expr<int>)
    {
        public static Expr<int> operator +(Expr<int> a, Expr<int> b) => new AddNode(a, b);
        public static Expr<bool> operator <(Expr<int> a, Expr<int> b) => new LessThanNode(a, b);
        public static Expr<bool> operator >(Expr<int> a, Expr<int> b) => new GreaterThanNode(a, b);
    }
}
