# Spike — Building Style

> Branch `claude/csharp-snippet-merge-style`, stacked on the Phase 2 PR (#107).
> A **UX-only** pass: change how a recipe is *authored*, not what it generates.
> The generate → compile → run gate output stays byte-identical throughout.

## The itch

Recipes already read as a Flutter-style tree of component instances — `new X(child, child)`,
composed by nesting. One thing grates: the **`new Block(...)` wrapper**, worst as a body
argument where it wraps the children:

```csharp
new LoopNode(new Lit(5), i, new Block(          // <- the wrapper is noise
    new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i)))))
```

Flutter doesn't make you name that wrapper — `children:` is a bare list literal `[...]`.
C# can do the same: collection expressions target-type to the parameter, exactly like
Dart's list literal.

## Goal

| Want | Approach |
|------|----------|
| drop `new Block(...)`, author blocks as `[...]` | collection expressions on `Block` |
| keep `new Block(...)` working too (additive, not breaking) | `[CollectionBuilder]`, ctor stays public |
| generated code unchanged | this is authoring-only — output must stay byte-identical |

```csharp
// after
new LoopNode(new Lit(5), i, [
    new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i)))])
```

## Plan

### Step 1 — collection expressions for `Block` (the win)

`[CollectionBuilder]` is a **built-in** .NET attribute (since .NET 8 / C# 12) — the same hook
`ImmutableArray<T>` uses. We write only the tiny factory it points at:

```csharp
[CollectionBuilder(typeof(Blocks), nameof(Blocks.Create))]
sealed record Block(params IStmt[] Statements) { … }

static class Blocks
{
    public static Block Create(ReadOnlySpan<IStmt> statements) => new(statements.ToArray());
}
```

Then convert the recipes (driver + tests) from `new Block(...)` to `[...]`. `Block` stays a
real type — the generator still maps `Block`-typed fields to the `Block.Of("id")`
substitution, so nothing downstream changes.

### Step 2 — factory methods (a bake-off, decide don't assume)

Open question: drop the `new` too. The only way to drop `new` on a record is a factory
function (a second surface). Compare on a real recipe before committing:

```csharp
// style 2 — collection only
new LoopNode(new Lit(5), i, [ new AssignNode(acc, new AddNode(new Ref(acc), new Ref(i))) ])

// style 3 — + factories (using static), drops every new and the Node suffix
Loop(Lit(5), i, [ Assign(acc, Add(Ref(acc), Ref(i))) ])
```

Generate a `X(...)` factory per node alongside the record; hand-write lowercase atom
factories (`Lit`/`Ref`). Write one throwaway recipe each way, eyeball side by side. **Keep
style 3 only if it clearly reads better** — else drop the factories and ship style 2. The
cost of style 3 is the second generated surface; that has to earn itself.

## Non-goals (deliberately deferred)

- **Hiding the `Block` ctor** (forcing `[...]` as the only path) — one-line follow-up once
  the bracket style has proven out; no reason to slam the door early.
- **Renaming node types** (`LoopNode` → `Loop`, dropping the suffix) — only if the bake-off
  says so; the suffix currently disambiguates the node from the `Snippets.Loop` method.

## Done-when

1. Recipes author blocks as `[...]`; the generate → compile → run gate stays green and the
   generated `ScriptData.cs` is **byte-identical** to before (authoring-only change).
2. Regression net green.
3. The factory question (step 2) is decided — kept with a recorded reason, or dropped with
   one.

## Guardrails (unchanged from Phase 2)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the gate green.
- YAGNI — least mechanism. Add the factory surface only if the bake-off earns it.
