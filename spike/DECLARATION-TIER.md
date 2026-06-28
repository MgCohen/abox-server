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
    new Field("Id", "Guid"), new Field("ArtistId", "string"), new Field("FavoritedAt", "DateTime")))
// => using System;
//    public record FavoriteArtist(Guid Id, string ArtistId, DateTime FavoritedAt);
```

Design calls settled by building it:

| Call | Decision | Why |
|------|----------|-----|
| Where do type nodes come from? | **hand-authored, structural** (like `Block`/`Var`/`Lit`) — *not* snippet-generated | a type declaration is the *container*; snippets model the constructs that go *inside* a body. The two-tier split stays clean. |
| What's the gate? | **compile + reflect-shape**, not `Run()` | a bare entity has nothing to run; correctness = it compiles and the loaded `Type` has the expected kind + members. |
| One node or four? | **four distinct nodes** (`RecordNode`/`ClassNode`/`StructNode`/`EnumNode`) under `abstract record TypeDecl` | makes illegal states unrepresentable — an enum can't carry typed fields. `record`/`class`/`struct` currently share `(Name, Field[])`; the `TypeEmitter` switch is the only place they converge. **Collapse-watch:** if they never diverge they fold to one node with a kind; they will diverge (class gains methods, record value-eq, struct value semantics), so kept apart. |
| The new primitive | `TypeRef` (a named type, implicit from `string`) | member/return/base types all *reference* a type; the int-only dodge ends here. |

Deferred from this step (YAGNI until a component forces them): `using`-derivation (hardcoded
`using System;` for now), modifiers/base lists, members beyond fields (methods/ctors — that's where
the body tier re-enters), enum underlying type + explicit values.

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
