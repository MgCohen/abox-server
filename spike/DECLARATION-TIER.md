> **PARKED EXPLORATION** — paused, not legacy. Anchor + active direction: [README.md](README.md) → *Invariants* + *Document map*. Resume when this thread is picked back up.

# Spike — Declaration Tier (M1)

> Branch `claude/csharp-snippet-merge-decl`, stacked on the building-style PR (#109).
> **Reframed:** this is **M1** of the north star (`NORTH-STAR.md`) — the file-tier step that lets a
> recipe emit a *whole owned file* (a component), not a body in a toy shell. The forks below (F1–F4)
> are the mechanism for M1; read `NORTH-STAR.md` first for *why* and *toward what*.
> The next frontier: compose the *element that holds a body*, not just the body.

## The frontier

Everything built so far composes the **body** of a fixed shell. The generator hardcodes:

```csharp
public static class ScriptData { public static int Run() { <recipe> } }
```

and the recipe only fills `<recipe>` (a `Block` of statements). Nothing models the **end result** as
a value — the method, the class, the file. The declaration tier is the same modeling move we made
for statements, applied one level up: from "compose statements" to "compose declarations."

`2c` (the repository-fetch recipe) is the forcing function — the first non-toy recipe with method
calls and real types, which can't be expressed as a bare statement body.

## The forks to settle (F1–F4, from the backlog)

| Fork | The question |
|------|--------------|
| **F1** | What *is* the generated element — a `MethodNode` (name, return type, params, `Block` body)? Does the class shell stay hardcoded or become a `ClassNode`? |
| **F2** | Is there a `Result`/`Program` type above `Block`, and is the model one **containment spine** `result → … → Block → … → Var<T>`? Containment vs inheritance. |
| **F3** | How does a recipe **define a new type** — a type-def snippet (`@name` marker + a member region), or a new fill form? |
| **F4** | **Adding a field** — is a member list just another `Block` (field-add ≡ statement-add), or its own region type so a statement can't land in a field slot? |

## Approach (design-first, like the last pass)

1. **Sketch F1 first** — "what is the generated element." Method-first: model `MethodNode`; today's recipe becomes its body. Decide where the shell comes from.
2. **Build the smallest forcing recipe** — a method that calls a repository and returns a real type (`2c`), proving the model survives non-toy code.
3. **Let F2–F4 fall out** as the recipe demands them, not before (YAGNI).

## Status — type-declaration tier DONE ✅

The first M1 step: a recipe emits a **whole owned type**, not a body in a shell. Built the four basic
kinds to validate the model — `record`, `class`, `struct`, `enum` (`src/Declarations.cs`,
`src/TypeEmitter.cs`). Each emits clean owned C# (`spike/out/*.cs`), compiles, and passes a shape
assertion. Driver `PASS`; `DeclarationTests` green (4 facts).

```csharp
TypeEmitter.Emit(new RecordNode("FavoriteArtist",
    new Field<Guid>("Id"), new Field<string>("ArtistId"), new Field<DateTime>("FavoritedAt")))
// => public record FavoriteArtist(System.Guid Id, System.String ArtistId, System.DateTime FavoritedAt);
```

Design calls settled by building it:

| Call | Decision | Why |
|------|----------|-----|
| Where do type nodes come from? | **hand-authored, structural** (like `Block`/`Var`/`Lit`) — *not* snippet-generated | a type declaration is the *container*; snippets model the constructs that go *inside* a body. The two-tier split stays clean. |
| What's the gate? | **compile + reflect-shape**, not `Run()` | a bare entity has nothing to run; correctness = it compiles and the loaded `Type` has the expected kind + members. |
| One node or four? | **four distinct nodes** (`RecordNode`/`ClassNode`/`StructNode`/`EnumNode`) under `abstract record TypeNode` | makes illegal states unrepresentable — an enum can't carry typed fields. `record`/`class`/`struct` currently share `(Name, Field[])`; the `TypeEmitter` switch is the only place they converge. **Collapse-watch:** if they never diverge they fold to one node with a kind; they will diverge (class gains methods, record value-eq, struct value semantics), so kept apart. |
| The new primitive | `TypeRef` (a named type, implicit from `string`) | member/return/base types all *reference* a type; the int-only dodge ends here. |
| Generic field? | **both** — `Field<T>` for **existing** types, string form for **created** types | `Field<Guid>("Id")` is compiler-checked and renders **fully-qualified** (`System.Guid`, via `typeof(T).FullName`) — no keyword table, no `using` (agent-first, so the verbose name is fine). The string form (`Field("repo", "IFavoriteArtistRepository")`) names a type a sibling recipe is still generating. **General, not Field-specific — see the forward-reference note below.** |

### Forward references — the limit of borrowing C#'s type system

The generic-handle design (`Var<T>`, `Lit<T>`, `Field<T>`, `Expr<T>`) gets compose-time type safety
*for free* by borrowing C#'s own type system. That only works for types that **already exist as CLR
types in the recipe's compilation**. A type a sibling recipe *generates* (`FavoriteArtist`) lives in a
different compilation (the output assembly) — it is never `typeof`-able from the recipe, no matter the
order recipes run in. So `Var<FavoriteArtist>` is exactly as impossible as `Field<FavoriteArtist>`;
the limit is general, not Field-specific.

Conclusion: **`TypeRef` (a value-level type *name*) is the fundamental representation; `<T>` is sugar
for the known-type subset**, uniformly across every typed node. `Var`/`Lit` haven't needed the
name-based form only because the catalog is int-only; the first recipe that declares a variable of a
generated type forces `Var(name, TypeRef)`. Cross-use safety for generated types can't come from C#
generics — it either defers to the generate → compile gate (the spike's existing stance for scope,
PHASE-2 2b) or, if ever needed, a recipe-level validator over `TypeRef`s. Deferred (YAGNI).

Deferred from this step (YAGNI until a component forces them): `using`-derivation (hardcoded
`using System;` for now), modifiers/base lists, members beyond fields (methods/ctors — that's where
the body tier re-enters), enum underlying type + explicit values. All tracked in the canonical
backlog, `README.md` §8 (#16/#18/#19).

## Status — method tier DONE ✅

The two tiers join: a `MethodNode(TypeRef Returns, string Name, Block Body)` is a `Member` of a
`ClassNode`, and its **body is the body tier** — `TypeEmitter` renders the class + signature and
delegates the body to `Generator.RenderBody`. The hardcoded `ScriptData.Run()` shell is now a recipe:

```csharp
new ClassNode("Calculator", new MethodNode(TypeRef.Of<int>(), "Run", <loop-sum block>))
// => public class Calculator { public System.Int32 Run() { int acc = 0; for (...) {...} return acc; } }
// gate: compile + invoke -> 10
```

Settled:
- `Member` base; `Field` and `MethodNode` are members; `ClassNode` holds `Member[]` (record/struct
  stay `Field[]` — a positional field slot can't take a method, by design).
- A method **signature** is a hand-written container (declaration tier); its **body** is snippet-driven
  (body tier). Confirms the tier line: *containers hand-written, code-bearing bodies via snippets.*
- The gate gains `Runtime.Invoke` (construct + invoke) — a method can *run*, unlike a bare type.

Deferred: **params wired to body handles** (a signature `int x` introducing a `Var<int>` the body
uses) — the immediate next sub-step; modifiers (`static`/access), ctors/properties — backlog #18.

## Carry-overs to revisit here

- **Class-based snippets (backlog #6)** — a "method" or "type" snippet is naturally class-shaped; the
  class form (ctor-param fills + `Body()` + typed metadata) may earn its keep here even though methods
  win for expression/statement snippets.
- **Generics / `Call` mode** — the catalog is still int-only and inline-only; a real method call likely
  needs both. Retire those divergences as the recipe forces them.

## Guardrails (unchanged)

- Spike stays isolated (outside `dirs.proj` / `ABox.slnx`).
- Every change keeps the generate → compile → run gate green.
- YAGNI — model the construct in front of us, not all of C#.