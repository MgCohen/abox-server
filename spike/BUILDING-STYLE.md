> **PARKED EXPLORATION** — paused, not legacy. Anchor + active direction: [README.md](README.md) → *Invariants* + *Document map*. Resume when this thread is picked back up.

# Spike — Building Style

> Branch `claude/csharp-snippet-merge-style`, stacked on the Phase 2 PR (#107).
> A **UX-only** pass: change how a recipe is *authored*, not what it generates.
> **Status — DONE ✅.** All five levers (A–E) are implemented; the generate → compile → run
> gate stays green and `out/ScriptData.cs` is byte-identical to before.

## The itch

Recipes already read as a Flutter-style tree of component instances — `new X(child, child)`,
composed by nesting. The ceremony around that tree was the noise: the `new Block(...)` wrapper,
`new Ref(x)` to *use* a variable, `new` + the `Node` suffix on every node, `new Lit(0)` around a
bare number. None of it is the recipe's *intent* — it's the cost of expressing the tree in C#.

## The levers (all built)

| Lever | Drops | Authoring before → after | How it's implemented |
|-------|-------|--------------------------|----------------------|
| **A** brackets | `new Block(...)` | `new Block(a, b)` → `[a, b]` | `[CollectionBuilder]` on `Block` (+ `IEnumerable<Stmt>` so the element type is known); ctor still works |
| **B** bare handles | `new Ref(x)` | `new Ref(acc)` → `acc` | `Var<T> : Expr<T>` — a handle *is* an expression; `Ref` **deleted** |
| **C** factories | `new` + `Node` | `new LoopNode(...)` → `Loop(...)` | generated into `Factories.Generated.cs` (`static partial class Recipe`) by the gen tool |
| **D-core** generic literals | int-only `Lit` | `Lit<int>` / `Lit<bool>` / `Lit<double>` | `Lit<T>(T) : Expr<T>, ILit` — mirrors `Var<T>` |
| **D-sugar** bare literals | `new Lit(0)` | `new Lit<int>(0)` → `0` | implicit `T → Expr<T>` on the `abstract record Expr<T>` base |
| **E** operators | `Add(...)` / `LessThan(...)` | `Add(acc, i)` → `acc + i`, `LessThan(i, 3)` → `i < 3` | C# 14 extension operators on `Expr<int>` (`Operators.cs`) |
| **F** single-statement | `[...]` around one statement | `Loop(i, 5, [Assign(…)])` → `Loop(i, 5, Assign(…))` | implicit `Stmt → Block` on the `abstract record Stmt` base; `[...]` kept for multiple |

### One recipe, four styles — all emit byte-identical source

```csharp
// 1 — explicit ctors
new Block(new DefineNode(acc, new Lit<int>(0)),
    new LoopNode(i, new Lit<int>(5), new Block(
        new AssignNode(acc, new AddNode(acc, i)))),
    new ReturnNode(acc))

// 2 — brackets (A)
[ new DefineNode(acc, new Lit<int>(0)),
  new LoopNode(i, new Lit<int>(5), [
      new AssignNode(acc, new AddNode(acc, i))]),
  new ReturnNode(acc) ]

// 3 — factories (C) + literals (D) + bare handles (B) + single-statement body (F)
[ Define(acc, 0),
  Loop(i, 5, Assign(acc, Add(acc, i))),
  Return(acc) ]

// 4 — + operators (E)
[ Define(acc, 0),
  Loop(i, 5, Assign(acc, acc + i)),
  Return(acc) ]
```

Fields read in **template order** — a fill sorts by where it appears in the snippet body, so the
variable a construct introduces comes first: `Define(acc, 0)` (`acc = 0`), `Loop(i, 5, …)`
(`for i … < 5`). This replaced the original signature-position-for-params rule, which read
`Define(0, acc)` value-first.

## The keystone that wasn't — verified empirically

The plan called the **interface → class flip** the keystone the whole set rode on. Four throwaway
compile probes (.NET 10 / C# 14, SDK `10.0.109`) proved that wrong. A flip is needed by exactly the
two levers that hang a **conversion** off a base — and only because a user-defined conversion can't
come from an interface (`CS0552`):

| Lever | Needs a flip? | Decisive evidence |
|-------|---------------|-------------------|
| A brackets | no | built-in attribute |
| B bare handles | no | a record implementing an interface — a *deletion* |
| C factories | no | vanilla static methods |
| **E operators** | **no** | C# 14 extension operators attach to `Expr<int>` **or** even `interface IExpr<int>` with no flip. Operators *inside* a generic base fail (`CS0563`) or leak onto every `T`; extension operators scope cleanly to int. |
| **D-sugar** literals | **yes** (`IExpr<T>` → `Expr<T>`) | a conversion **cannot target an interface** (`CS0552`), the leaf-`int→Lit` chain won't fire through an upcast (`CS0029`), and extension *conversions* don't exist (`CS9282`). Only an in-type generic conversion on a **class** base works. |
| **F** single-statement | **yes** (`IStmt` → `Stmt`) | same wall — `implicit operator Block(IStmt)` is `CS0552`; from `abstract record Stmt` it compiles and resolves at a `Block` param site with no ambiguity against the `[CollectionBuilder]` path. |

### Why D survived (the generic-`Lit<T>` fix)

D was first judged *drop it* — the only conversion that compiled (`implicit operator Expr<T>(T)`)
was leaky: with an int-only `Lit`, `Expr<string> s = "hi"` compiled and threw at runtime. The fix:
make the literal node **generic**, mirroring `Var<T>`. Then the conversion is `T → Expr<T>`,
*exactly typed* — verified: `5` and `true` resolve to `Lit<int>`/`Lit<bool>`, while `Expr<int> = "hi"`
and `= true` are hard compile errors (`CS0029`/`CS1503`). `Lit<T>` is generic for any `T`, but only
`int`/`bool` are *reachable* through the current int-only snippet catalog (no slot accepts an
`Expr<double>`/`Expr<string>`); rendering goes through Roslyn's invariant-culture literal formatter
so a future non-int literal emits valid C#. The "leak" was never the type system failing — it was an
int-only node impersonating all literals. No footgun: an already-`Expr` value (a `Var`) outranks the
conversion, so handles are never wrapped.

### What the flip costs

Nothing usable. The only interface capability a class can't have is `out T` **covariance** — and
covariance needs reference-type `T`, while `Expr`'s `T` is a value type (`int`/`bool`), so `out` was
inert; `Stmt` has no type parameter, so it had no variance to give up at all. Records keep
value-equality / `with`; and `Generator.cs` barely names the bases (it routes on `is IVar` / `is
Block` / else and renders statements via `RenderStmt`), so the blast radius was the node definitions
+ the gen tool's field-type strings.

## How byte-identical is kept

The generator routes a fill by its **field type**, not its value: a `Var<T>`-typed field is a
binding marker (`@var`), a `Var` at an `Expr<T>`-typed site is an expression (its bare name).
`RenderExpr` renders `IVar` → name and `ILit` → the literal text (via Roslyn
`SymbolDisplay.FormatPrimitive`, invariant culture). So every style collapses to the same node tree
and the same source. Enforcement: the regression golden in `GeneratorRegressionTests`, the driver's
four-styles `byte-identical=True` check, and the committed `out/ScriptData.cs` (zero diff across this
change).

## Known sharp edges (from review)

- **The `==` operator is a trap — use `Eq(...)`.** `IfElse(i == 3, …)` does *not* error: `Expr<T>`
  is a record, so `==` is record value-equality run at author time, yielding a `bool` that the
  `bool → Expr<bool>` conversion absorbs into `Lit<bool>(false)` → `if (false)`, a silently wrong
  program. It can't be blocked cleanly (records synthesize `==`; the conversion is needed for generic
  literals), so the **`Eq(a, b)`** factory is the sanctioned equality path and `==` is documented as
  a trap.
- **Operator coverage is deliberately small.** Modeled operators are `+`, `<`, `>` only — each a
  faithful node (`>` renders `a > b`, not a flipped `<`). Equality is `Eq(...)`. Anything else
  (`-`, `*`, `<=`, `!=`) is *not* modeled: no operator and no factory until a snippet is added.
- **Non-associative substitution is unparenthesized (deferred).** `SubstituteExpr` splices a child
  expression without protective parens, so a future `Sub`/`Div` snippet would mis-group
  (`Sub(x, Sub(x, 1))` → `x - x - 1`). Safe today because `+` is the only binary op and it's
  associative. Add paren-aware substitution when the **second binary operator** lands.
- **Handle name is spelled twice (language-blocked).** `var acc = new Var<int>("acc")` — the C#
  identifier and the string can diverge silently (the generator emits the string).
  `CallerArgumentExpression` reads arguments, not the assignment target, so this can't be auto-derived.
- **`out`/`in` snippet params are dropped silently** — recognizers handle by-value and `ref` only.
  Out of scope; would need an explicit "unsupported modifier" error.

## Open / deferred

- **Hiding the ctor** (forcing `[...]` / factory-only, no `new`) — needs an **assembly boundary**
  (non-public ctors + public factories + recipes in a separate assembly), not a spike toggle. Worth
  it only once a factory enforces an invariant the ctor doesn't; the product's existing assembly
  seams are where it would land. Deferred.
- **Renaming node types** (`LoopNode` → `Loop`) — the suffix still disambiguates the node from the
  `Snippets.Loop` method; the factory already gives the clean `Loop(...)` call surface. Not pursued.

## Done-when

1. ✅ Recipes author every style (brackets/factories/literals/operators); the gate stays green and
   `out/ScriptData.cs` is **byte-identical**.
2. ✅ Regression net green (5 tests), driver `PASS`, warning-free build.
3. ✅ The factory question (and D, and the flip) decided with recorded, *verified* reasons above.

## Guardrails (unchanged from Phase 2)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the generate → compile → run gate green.
- YAGNI — least mechanism. Each lever earned its surface against a real recipe.