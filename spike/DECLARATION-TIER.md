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
