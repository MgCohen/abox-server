# Spike Phase 2 — Expansion

> Branch `claude/csharp-snippet-merge-expand`, stacked on the baseline PR (#106).
> The baseline (Steps 1 & 2) proved the **mechanism** has no unknowns. This phase
> grows the **vocabulary** — and per the plan, this is where the shape starts to
> move. We build freely here; the baseline branch stays stable.

## Goal

Expand past the toy primitives and, in doing so, force the two foundations the
baseline deliberately left open:

1. **Multi-statement blocks** — the baseline only splices single-statement
   blocks (`Block.Of("id")` → one statement). Real constructs have multi-statement blocks.
2. **The scope / variable-wiring model** — names are still stringly-typed
   (`"acc"`, `"i"`) and only caught late by the compile gate. Real composition
   needs a coherent way for snippets to declare and share variables.

## Order (cheapest-forcing-function first)

| Step | Construct | Forces | Why this order |
|------|-----------|--------|----------------|
| 2a ✅ | **`If`** | multi-statement bodies, branch scope | cheapest construct that exercises both foundations with minimal domain noise |
| 2b ✅ | **scope model** | variable declaration/lookup across snippets | extracted once `If` shows where stringly-typed names break |
| 2c | **Repository fetch** | method calls, real types, async-ish shape | the real-product proof that the model survives non-toy code |

Retire the baseline divergences as they get in the way, not before:
- **generics `<T>`** (drop int-only) — when a construct needs a non-int type.
- **`Call` mode** (vs inline) — when a multi-statement snippet can't be inlined.

## Status — 2a (`IfElse`) DONE ✅

Built `IfElse` (`bool` condition, named `then`/`else` blocks) + a `LessThan`
comparison primitive. Sample `Samples.IfElseInLoop` (`spike/tests/IfElseTests.cs`):

```csharp
for (int i = 0; i < 5; i++)
    if (i < 3) { acc = acc + i; acc = acc + 1; }   // multi-statement then-block
    else       { acc = acc + 10; }
// => 26
```

Done-when:
1. ✅ Composes, compiles, runs → **26**. Multi-statement `then` block proven.
2. ✅ Regression net green (5 tests).
3. ✅ Scope question **surfaced and named** (below) — deferred to 2b.

What fell out for free (the non-int dodge held): `bool condition` → `IExpr<bool>`
and `LessThan` → `IExpr<bool>` flowed through the generator with **zero changes** —
only the atoms (`Lit`/`Ref`) are int-pinned. Named blocks worked because
`Block.Of("id")` already carries an id.

### The scope finding (→ 2b)

The `then`/`else` branches reference `acc` and `i` **by string** (`Ref("acc")`,
`Ref("i")`). Nothing checks those names are in scope at the branch — correctness is
caught **only at the final compile gate**, never at recipe-authoring. And a variable
*declared inside* a block (a `DefineNode` in `then`) is a new local with no modeled
visibility. So: **there is still no scope model** — names are stringly-typed and
unscoped, exactly as the baseline left them. `IfElse` confirmed where this bites:
the moment branches share outer variables. That's 2b.

## Status — 2b (scope model) DONE ✅

Variable names stop being strings and become typed **handles**. A declaration *binds*
a handle; uses *reference* it. The name is chosen once, on the handle, never re-spelled
at a use site.

```csharp
var acc = new Var<int>("acc");
var i   = new Var<int>("i");

new Block(
    new DefineNode(new Lit(0), acc),                       // binds acc
    new LoopNode(new Lit(5), i, new Block(                 // binds i
        new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i))))),
    new ReturnNode(new Ref(acc)));                         // Ref(acc) : IExpr<int>
```

What changed (handles only — no binder, by decision):
- `Var<T>` (+ `IVar`) added to `Nodes.cs`; `Ref` now takes `Var<int>`, not `string`.
- The two marker recognizers emit `Var<T>` instead of `string`, the `T` read straight
  from the snippet (`int @i = 0` → `Var<int>`, `ref int @target` → `Var<int>`).
- Regenerated nodes: `DefineNode(.., Var<int> Var)`, `AssignNode(Var<int> Target, ..)`,
  `LoopNode(.., Var<int> I, ..)`. The lowering keys markers on `IVar` and renders `.Name`.

What this buys at **authoring** (the jump over strings): typed refs (can't cross types),
rename-safety, and you can't reference a handle you never created. What stays on the
**compile gate** (deliberately not pulled forward): use-out-of-block, a handle with no
declaring node, and identifier collisions. Building a lexical-scope binder for those is
the *second-use* abstraction — added only if scope-escape actually bites.

Forward-compat with 2c: dependencies (`_repo`) and method params (`id`) are just handles
from an outer tier the recipe is *given* rather than *declares* — new handle **sources**,
not a new scope mechanism.

Done-when:
1. ✅ Recipes author variables as `Var<T>` handles; the generate → compile → run gate
   stays green (output byte-identical — handles render to the same names).
2. ✅ Type gate proven: `Ref` rejects a non-handle, `AddNode` rejects a handle where an
   `IExpr<int>` is expected (the two commented lines in `Program.cs`).
3. ✅ Regression net green (5 tests). Tests build their own throwaway recipes — the shared
   `Samples` class is dissolved.

## Original done-when (2a — `If`)

1. An `If` snippet with a multi-statement `then` body composes and the generated
   `ScriptData.cs` compiles and runs with the expected result.
2. The regression net stays green (baseline behavior unchanged).
3. The scope question is *surfaced and named* — we either model it or write down
   exactly why we deferred it again.

## Guardrails (unchanged from baseline)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every expansion keeps the generate → compile → run gate green.
- Grow the instruction set deliberately (YAGNI) — model the construct in front of
  us, not all of C#.
