# PROBE A — inline type back-fill ("usage drives generation")

A self-contained spike proving the riskiest mechanic in the recipe/component model:
**an author names a brand-new type at a use-site (as a value), and a Roslyn
incremental source generator makes that type exist and be usable later in the same
compilation — additively, no interceptors.**

## Idea (the claim)

The author declares the need *inline, as a value* (a string name + `(field, typeof(T))`
tuples), then uses the minted type as a *real type* on a later line:

```csharp
// declare the need inline, as a value:
CreateRecord("Foo", ("Id", typeof(Guid)), ("Name", typeof(string)));

// ...and use the minted type as a real type — this must COMPILE:
Foo f = new Foo(Guid.NewGuid(), "bar");

// bonus: a generic over it also binds:
List<Foo> many = ...;
Repo<Foo> repo = ...;     // a user-defined generic, too
```

The reviewer-flagged tension: `Foo` is written as a **value** (a string literal in a
`CreateRecord(...)` call) but referenced as a **type** (`Foo`). For `Foo` to bind, the
generator must scan for the `CreateRecord("Name", fields...)` markers, parse name +
fields, and emit `public record Foo(Guid Id, string Name);` into the **same**
compilation so the downstream reference resolves.

## Why (which invariant / which doubt)

- **Invariant under test:** *"Source-generation back-fills inline — bounded"*
  (README §"Design principles"): the author declares a need at the use-site and the
  generator mints **types / plumbing from a fixed grammar, never new behavior.**
- **Reviewer doubt #17** (backlog §8): the *forward-reference / two-phase* worry. A
  `<T>` only type-checks against **real** types; a type a recipe *generates* lives in
  "another compilation," so it must be named at the **value level** (`TypeRef` /
  here a string in `CreateRecord`), not as `<T>`. #17 asserts the value-level naming
  is the fundamental representation and the type reference is sugar over the known
  subset. This probe is the concrete test of whether the value→type back-fill closes
  inside one compilation.

## What was proved (concrete evidence)

**It compiles AND runs.** `dotnet build` → 0 warnings / 0 errors (under the repo's
`TreatWarningsAsErrors` it still built clean). `dotnet run` output:

```
Foo  -> Foo { Id = bf15a0d4-7b63-4425-ae2e-539b7af2e1f8, Name = bar }
Point-> Point { X = 3, Y = 4 }
List<Foo> count = 2
Repo<Foo> count = 1
```

The generator emitted, into the consumer's own compilation:

```csharp
// Minted.Foo.g.cs
public record Foo(global::System.Guid Id, string Name);

// Minted.Point.g.cs
public record Point(int X, int Y);
```

Note `typeof(Guid)` was resolved through the **semantic model** to the
fully-qualified `global::System.Guid` — so the minted record needs no `using` and
can't collide on an ambiguous short name.

Proven specifically:

| Claim | Result |
|---|---|
| `CreateRecord("Foo", ...)` as a **bare statement** is enough (no `var x = ...`) | ✅ |
| `Foo f = new Foo(...)` binds and runs; record `ToString()` works | ✅ |
| Works **additively** — no interceptors, no `partial`, no edits to user code | ✅ |
| `List<Foo>` (BCL generic over a minted type) binds | ✅ |
| `Repo<Foo>` (a **user-defined** generic over a minted type) binds | ✅ |
| `CreateRecord` inside a **method body**, not just top-level, mints the type | ✅ |
| **Order-independent**: using the minted type on a line *before* its `CreateRecord` still compiles | ✅ |
| Negative: referencing an un-minted type fails cleanly with `CS0246` | ✅ |

### The key generator snippet

```csharp
[Generator]
public sealed class InlineRecordGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1) emit the marker method so the value-call CreateRecord(...) compiles
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("CreateRecord.g.cs", MarkerSource));

        // 2) find every CreateRecord("Name", (field, typeof(T))...) and mint a record
        var specs = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (n, _) => n is InvocationExpressionSyntax inv
                                            && CalleeName(inv) == "CreateRecord",
                transform: static (ctx, _) => Extract(ctx))   // uses SemanticModel
            .Where(static s => s is not null).Select(...).Collect();

        context.RegisterSourceOutput(specs, static (ctx, all) => Emit(ctx, all));
    }
}
```

`Extract` reads arg[0] as the const-string name, each remaining tuple as
`(constStringField, typeof(T))`, and resolves `T` via
`SemanticModel.GetTypeInfo(...).ToDisplayString(FullyQualifiedFormat)`. `Emit`
de-dups by name and `AddSource`s `public record Name(...)`. The marker method
`InlineTypes.CreateRecord(string, params (string, Type)[])` is itself generated
(post-init), so the value-call site compiles even though the author wrote no stub.

## How to run

```bash
cd consumer
dotnet run
```

That's it — the generator project is referenced as an analyzer
(`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`), so building the
consumer runs the generator in-build. To see what was minted, look in
`consumer/generated/InlineTypeGen/InlineTypeGen.InlineRecordGenerator/`
(`EmitCompilerGeneratedFiles` writes them there for inspection).

**Layout**
```
probe-a-inline-typegen/
  Directory.Build.props   ← isolates the probe (local bin/obj, no repo conventions)
  gen/
    InlineTypeGen.csproj     ← netstandard2.0 incremental generator
    InlineRecordGenerator.cs ← the generator
    Polyfills.cs             ← IsExternalInit shim (records on netstandard2.0)
  consumer/
    Consumer.csproj          ← net10.0 exe, references gen as Analyzer
    Program.cs               ← the use-site + the minted-type usages
```

Package versions match `spike/gen/`: `Microsoft.CodeAnalysis.CSharp 4.11.0`
(cached locally; offline restore works).

## Honest limitations / open questions

1. **The marker call still has to compile as a value.** `CreateRecord(...)` resolves
   to a generated no-op `InlineTypes.CreateRecord(string, params (string, Type)[])`.
   So the *value* side is real, type-checked C# (a wrong arg shape is a normal
   compile error) — but it does mean a real method must exist. We generate it via
   post-init output so the author writes zero stub. Acceptable; worth noting it's not
   *pure* magic — there is a (generated) method behind the call.

2. **Fields syntax is rigid.** Today: `("Name", typeof(T))` tuples only. No
   nullability annotation (`typeof(string?)` is illegal C#), no field modifiers, no
   nested/minted field types referencing *another* minted record (untested;
   forward-ref across two minted types in one compilation is plausible but unproven
   here). A richer grammar (init-only, defaults, attributes) is future work.

3. **`EmitCompilerGeneratedFiles` is an on-disk trap.** Writing the generated files
   to `generated/` and then *also* compiling that folder duplicates every type
   (`CS0101`/`CS0111`). Fixed here with `<Compile Remove="generated/**/*.cs" />`. In
   a real project, keep emitted-for-inspection files out of the compile, or they
   collide with the live generator pass. (This bit on the very first run.)

4. **IDE / IntelliSense caveat (the big one for the agent-authoring story).** This was
   proved at the **compiler/CLI** level. In an IDE, a *newly typed* `CreateRecord(...)`
   line does not always make `Foo` resolvable instantly — the generator-driven type
   can lag a keystroke or a build, showing a transient red squiggle on `Foo` until the
   design-time generator pass catches up. The compile is correct; the *live editing
   experience* may show flicker. For an **agent** author (writes file → builds) this
   is a non-issue; for a **human** in an IDE it's a known source-generator rough edge.

5. **Incremental-cache notes.** The pipeline is `Collect()`-based, so any
   `CreateRecord` change re-runs the `Emit` over all specs (coarse but correct). The
   `RecordSpec`/`FieldSpec` models are value-equatable records, so unchanged specs
   don't retrigger downstream — but because we `Collect()` then de-dup, the final emit
   isn't perfectly incremental per-type. Fine at spike scale; a production version
   would key `RegisterSourceOutput` per-record for tighter caching.

6. **No conflict diagnostic.** Two `CreateRecord("Foo", ...)` with *different* field
   lists silently take "last writer wins." A real impl should emit a diagnostic on
   conflicting definitions.

### Verdict

The riskiest mechanic **holds**: usage genuinely drives generation: a value-level
type name back-fills into the same compilation additively, binds as a real type,
and composes under both BCL and user-defined generics — with no interceptors. The
forward-reference doubt (#17) is answered *within a single compilation*. The
remaining real risks are (a) cross-file / cross-recipe forward refs (separate
compilations — not exercised here) and (b) the IDE live-editing lag, neither of
which blocks the agent build-then-compile workflow.
