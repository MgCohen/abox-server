# Spike: Deterministic, Type-Safe Code Composition

> A spike to prove out a **deterministic, data-driven, type-safe** way to compose
> code from reusable fragments — the opposite of "ask an LLM to glue snippets
> together." Everything for this spike (this doc, code, generated output) lives in
> this folder.

This document is written to be **cold-readable**: you should be able to understand
the whole idea, why it's shaped the way it is, and what we're building, without
having seen the conversation that produced it.

> **Status — Steps 1 & 2 built and passing.**
> - **Step 1** (`spike/src/`): `dotnet run` generates `spike/out/ScriptData.cs`,
>   compiles it in-memory, and runs it → returns `10`. Editing a snippet flows
>   through; a mistyped recipe fails to compile (the 3 done-when criteria, §7.5).
> - **Step 2** (`spike/gen/`): the recipe nodes are now **source-generated** from
>   the `[Snippet]` methods into `spike/src/Nodes.Generated.cs` (standalone emit,
>   decoupled fill recognizers). The hand-written nodes are gone. Adding a
>   `[Snippet]` makes its node appear with no other edit; a **regression net**
>   (`spike/tests/`, 3 tests) pins the output across the refactor — all green.
>
> Spike divergences from the ideal design: **int-specialized** (no generic `<T>`
> yet) and **inline-only** (no `Call` mode). The emit is a console tool now (A2);
> swapping to an in-build source generator (A1) later is just a host change.
>
> **What building taught us:** the base interface (`IExpr<T>` vs `IStmt`) is
> decided by the snippet's **body kind** — expression-bodied (`=> a + b`) produces
> a value, block-bodied (`{ return value; }`) produces statements — **not** by the
> return type. `int Return(int value) { return value; }` is a statement snippet.

---

## 1. The problem

We want a system where:

- A catalog of **snippets** exists — e.g. one that "knows how to do a loop", one
  that "knows how to add two ints".
- Something (a human, or an **agent**) declares a **combination** of those
  snippets.
- The system **merges them into a single, coherent piece of real C# code** —
  e.g. *a loop that sums the indices*.

Hard constraints (these are the whole point — drop any one and the design changes):

| Constraint | Meaning |
|---|---|
| **Deterministic** | Same input → same output, byte for byte. No model in the loop. |
| **Data-driven** | The combination is *declared as data*, not hand-written each time. |
| **Type-safe** | Illegal compositions fail to compile — not at runtime, not "looks wrong". |
| **Agent-first** | An LLM agent authors the combination; the structure must constrain it and validate it *for free*. |
| **Owned output** | The generated code is a normal `.cs` file you can read, commit, and hand-edit. Not invisible compiler magic. |

Non-goal: a general "compile any C#" engine. We model the **vocabulary our agents
actually compose in**, and grow it deliberately.

---

## 2. Glossary

| Term | Meaning |
|---|---|
| **Snippet** | A reusable code fragment, authored as a **real, compiling C# method** with typed **fills**. The method body *is* the template. |
| **Fill** | A gap in a snippet, filled at composition time. Three forms: **param**, **marker**, **block** (see §4.2). |
| **Param** | A *value* fill — a by-value parameter, filled with an expression. |
| **Marker** | A *name* fill — an `@`-identifier (or `ref` param), filled with a name. |
| **Block** | A *region* fill — `Block.Of("id")`, filled with statements. |
| **Atom** | A primitive expression node too trivial to be a snippet (`Lit`, `Ref`). Hand-written. |
| **Recipe** | The **combination**, declared as a typed tree of records. This is what the agent writes. |
| **Node** | One entry in a recipe (e.g. `LoopNode`). **Generated from the snippet** (see §4.4). |
| **Generator (tool)** | A standalone one-shot program: recipe → `ScriptData.cs`. |

Two **separate** generation steps — do not conflate them:

1. **Source generation (in-build, always on):** snippets → typed recipe **nodes**
   (the *vocabulary* the agent writes against).
2. **The one-shot tool (run on demand):** a recipe instance → `ScriptData.cs`
   (the *artifact* you commit and own).

---

## 3. TL;DR of the design

- A **snippet is a real C# method** annotated `[Snippet]`. Its body is genuine,
  compiler-checked code with typed fills. You can author it, rename its variables,
  and get type errors — like any code.
- **Fills come in three forms** (param, marker, block), all visible in the method
  signature/body, all type-checked.
- The snippet's **signature is its contract** — exactly like `Func<>`/`Action<>`:
  the return type says what it produces, the parameters are its value fills (params).
- A **recipe** is a **typed tree of records** the agent writes. The record types
  are **source-generated from the snippets**, so they can't drift, and the agent
  gets IntelliSense + compile-time validation. The type system *is* the schema —
  no JSON, no hand-written validator.
- Recipe nodes share a normalized base — `IExpr<T>` (produces a value) /
  `IStmt` (produces statements) — so **composition is type-checked**: an `int`
  param rejects a `string`-producing node at authoring time.
- A **standalone tool** lowers a recipe to a plain `ScriptData.cs` you commit and
  may hand-edit. Roslyn is used to **parse snippet bodies and substitute fills at
  the node level** — never to hand-build syntax trees.
- **Type-safety is two-stage:** each snippet compiles in isolation (authoring); the
  assembled `ScriptData.cs` compiles (the composition gate).

---

## 4. The model

> **Note:** §4–§5 describe the *original* model and use its API (`string` markers, `Ref`,
> `IExpr<T>`/`IStmt`). The mechanism is unchanged, but the authoring surface has moved on —
> `Var<T>` handles, `Expr<T>`/`Stmt` record bases, generic `Lit<T>`, factories, operators, and
> collection-expression blocks. For the current API and authoring styles, see `BUILDING-STYLE.md`.

### 4.1 A snippet is a real compiling method

```csharp
[Snippet("define", Inline)]
void Define<T>(T value) { T @var = value; }
```

This **compiles**. `@var` is a legal identifier (the `@` escapes the `var`
keyword), so `T @var = value;` type-checks: rename, IntelliSense, and error
squiggles all work. The body is the template; the generator substitutes the fills.

> **The breakthrough.** Earlier we believed a *variable declaration* couldn't be a
> real snippet, because the declared **name** isn't an expression you can put a
> placeholder into. `@var` solves it: a placeholder *identifier* that compiles and
> is swapped at generation. This unified "trivial primitives" and "boilerplate"
> into **one model** (annotated methods with fills) instead of two.

### 4.2 The three fill forms

Fills are encoded so the snippet still compiles and the generator can find them:

| Form | How it's written | Filled by | Generated node field |
|---|---|---|---|
| **param** | a by-value parameter (`int value`, `int count`) | a rendered child **expression** | `IExpr<T>` |
| **marker — new** | an `@`-prefixed identifier *declared* in the body (`int @var = …`, `for (int @i …)`) | a name **string** from the recipe | `string` |
| **marker — existing** | an `@`-prefixed **`ref` parameter** (`ref int @target`) — a variable the snippet mutates/reads | a name **string** from the recipe | `string` |
| **block** | `Block.Of("id")` | a rendered child **block** (its statements) | `Block` |

Conventions the generator keys on:

- **`@`-prefixed identifier = marker.** Invisible to the C# compiler (it's a
  valid identifier, so the snippet compiles), meaningful to our generator. Used
  even when no keyword-escape is needed — `@` *is* our "this name is a placeholder" sigil.
- **by-value param = param fill**, **`ref` param = existing-name marker.** A `ref`
  param is exactly "a variable that already exists, which I mutate" — and it makes
  a mutation snippet like `Assign` compile *in isolation* (without it, `@target =
  value;` wouldn't compile because `@target` would be undeclared).
- **`Block.Of("id")` = block.** The id names the region; the generated node field
  takes its name (`"then"` → `Then`), and the recipe fills it with a `Block`.

### 4.3 Signature = contract (the Func/Action normalization)

We don't need a base class on the snippet methods — the **signature already is the
contract**, exactly like the BCL normalizes shapes into `Func<>`/`Action<>`:

```csharp
int  Add(int a, int b)   // == Func<int,int,int>  → produces an int
void Define<T>(T value)  // == Action<T>          → produces statements
```

The **return type** is what the snippet produces; the **parameters** are its value
fills (params). The generator reads this to decide whether the recipe node is an
`IExpr<T>` or an `IStmt`.

### 4.4 The recipe: a typed tree, generated from the snippets

The recipe nodes are **source-generated** from the `[Snippet]` methods — single
source of truth, no drift, full IntelliSense. The generator reads each snippet's
fills and emits a node implementing the normalized base:

```csharp
interface IStmt;            // produces statement(s)        — Action-like
interface IExpr<out T>;     // produces a value of type T   — Func-like
```

So `void Loop(int count) { for (int @i = 0; @i < count; @i++) { Block.Of("body"); } }`
mechanically generates:

```csharp
record LoopNode(IExpr<int> Count, string I, Block Body) : IStmt;
```

`Count` (param) → `IExpr<int>`; `@i` (new-name marker) → `string`;
`Block.Of("body")` (block) → `Block`. Because the node carries its produced
type, **composition is statically correct by construction**: an `int` param only
accepts `IExpr<int>`; plug in a `string` producer and it won't compile.

### 4.5 Generation of the artifact

A **standalone tool** (a `dotnet run` console program, *not* a Roslyn source
generator) lowers the recipe:

1. Walk the recipe tree.
2. For each node, find its snippet (by `[Snippet]` key / type map).
3. Parse the snippet body with Roslyn (`ParseStatement`/`ParseExpression`).
4. Substitute fills at the **node level**:
   - markers → swap the `@`-identifier / `ref`-param name for the recipe string,
   - params → swap the param reference for the recursively-rendered child,
   - blocks → replace the `Block.Of("id")` call with the rendered child block.
5. For `Inline` snippets, drop the method wrapper and keep the (substituted) body.
6. Assemble into one tree → `NormalizeWhitespace().ToFullString()` → write
   `ScriptData.cs`.

**We parse real C# and swap leaves — we never hand-build `BinaryExpression(...)`
trees.** That keeps the authoring visible (you read `a + b`, not factory calls)
while still getting typed nodes that compose and format cleanly.

`Inline` vs `Call` is a per-snippet mode (attribute flag). Inline splices the body
(only valid for single-expression / simple bodies); Call emits an invocation. The
spike does **Inline only**.

### 4.6 Type-safety is two-stage

| Stage | When | What it checks |
|---|---|---|
| **Snippet** | you author a `[Snippet]` method | the fragment is well-formed C#; fills are typed; rename works |
| **Recipe** | the agent writes the recipe | structure + `IExpr<T>`/`IStmt` wiring — illegal compositions don't compile |
| **Composition** | the generated `ScriptData.cs` builds | the assembled whole (scope, types across snippet boundaries) |

Nothing is validated "by convention" or by vibes. The C# compiler is the gate at
every stage.

---

## 5. Worked example: `loop + var + sum`

Goal: generate code that sums the loop indices `0..4` → `10`.

### 5.1 The snippets (authored, all compile in isolation)

```csharp
[Snippet("define", Inline)]
void Define<T>(T value) { T @var = value; }

[Snippet("assign", Inline)]
void Assign<T>(ref T @target, T value) { @target = value; }

[Snippet("add", Inline)]
int Add(int a, int b) => a + b;

[Snippet("loop", Inline)]
void Loop(int count) { for (int @i = 0; @i < count; @i++) { Block.Of("body"); } }

[Snippet("return", Inline)]
T Return<T>(T value) { return value; }
```

Plus two hand-written **atoms** (too trivial to be snippets):

```csharp
record Lit(int Value)   : IExpr<int>;   // 0, 5
record Ref(string Name) : IExpr<int>;   // acc, i
record Block(IReadOnlyList<IStmt> Statements);
```

### 5.2 The generated recipe nodes (from §4.4)

```csharp
record DefineNode<T>(string Var, IExpr<T> Value)           : IStmt;
record AssignNode<T>(string Target, IExpr<T> Value)        : IStmt;
record AddNode(IExpr<int> A, IExpr<int> B)                 : IExpr<int>;
record LoopNode(IExpr<int> Count, string I, Block Body)    : IStmt;
record ReturnNode<T>(IExpr<T> Value)                       : IStmt;
```

### 5.3 The recipe (what the agent writes)

```csharp
var recipe = new Block(new IStmt[]
{
    new DefineNode<int>(Var: "acc", Value: new Lit(0)),
    new LoopNode(
        Count: new Lit(5),
        I:     "i",
        Body:  new Block(new IStmt[]
        {
            new AssignNode<int>(
                Target: "acc",
                Value:  new AddNode(new Ref("acc"), new Ref("i")))
        })),
    new ReturnNode<int>(new Ref("acc")),
});
```

### 5.4 The generated `ScriptData.cs`

```csharp
int acc = 0;
for (int i = 0; i < 5; i++)
{
    acc = acc + i;
}
return acc;
```

### 5.5 How the wiring happens (the crucial bit)

Nothing magically knows "sum the index into the count." It is **stated** by the
recipe, against the contract each snippet publishes:

- `Define.@var` ← `"acc"`, `Loop.@i` ← `"i"`, `Assign.@target` ← `"acc"`.
- `Assign.value` ← `Add(Ref("acc"), Ref("i"))`, which renders `acc + i`.
- The snippets share variables **by name** — the recipe uses `"acc"` / `"i"`
  consistently. The loop only loops; the accumulator comes from a *separate*
  `Define`; the addition is a *separate* `Add`. Each snippet is ignorant of the
  others; the recipe is the wiring.

(This §4–§5 API — `string` markers, `Ref("acc")`, `IExpr<T>`/`IStmt` — is the **original** design.
It has since been superseded: names are instance-derived `Var<T>` handles (backlog #1, done), and the
bases are `Expr<T>`/`Stmt` records. The current authoring surface is in `BUILDING-STYLE.md`.)

---

## 6. Design decisions & rejected alternatives

These are the insights — *why* the design is shaped this way, and what we tried and
discarded.

| Alternative | Why we rejected it |
|---|---|
| **Metalama** (C#-to-C# compile-time templates) | Excellent for type-safe templates **woven via attributes at compile time**. But our composition is driven by a **typed recipe** and we want an **owned source artifact**, not invisible weaving. Its grain is "composition expressed in C#," not "recipe data → file you commit." We'd end up using its underlying Roslyn anyway. |
| **Record IR for *content*** (encode the `for` loop as `new Loop(...)`, lower via `SyntaxFactory`) | The actual code (`for (...)`) then **lives nowhere readable or editable** — it's synthesized by factory calls. You lose authoring, rename, and type-checking of the boilerplate. **Content must live in real source.** |
| **`SyntaxFactory` tree-building as the renderer** | Verbose and **invisible**: you write `BinaryExpression(SyntaxKind.AddExpression, …)` and can't *see* `a + b` until it renders. Wrong tool for authoring. (We still use Roslyn — but to **parse** real C# and **substitute** leaves, not to build trees by hand.) |
| **JSON recipe** | Records give the same structure **plus the type system as a free schema** — no parser, no validator, and compile-time validation for the agent. JSON buys nothing here. |
| **Record carrying a `Code` string** (`override string Code => "T [name] = value"`) | Typed *inputs*, but the `Code` string is an **unvalidated** blob — you lose authoring-time validation of the *output*, the exact thing we want. A real method (`[Snippet]`) validates the output **and** co-locates the template. |
| **`Slot.Body(params object[])`** | A generic `Slot.Of<T>()` makes the slot's produced **type visible and checked at authoring**; `params object[]` tells you nothing. |
| **Two-grain split** (records for trivial leaves, snippets for boilerplate) | Made redundant by `@var`: once declarations can be real snippets, **everything is one model** — annotated methods with fills. Simpler. |

---

## 7. The spike plan

**Hypothesis to prove:** we can take authored `[Snippet]` methods + a typed recipe
and deterministically emit a plain, compiling `ScriptData.cs` — with the snippets
validated at authoring and the composition validated by the type system.

### 7.1 Folder layout

```
spike/
  README.md             ← this doc
  src/
    SnippetAttribute.cs ← the [Snippet] marker attribute
    Snippets.cs         ← the [Snippet] methods (§5.1)
    Nodes.cs            ← IExpr<T>, IStmt, atoms, and Block (with Block.Of marker)
    Nodes.Generated.cs  ← the recipe nodes, source-generated by spike/gen (Step 2)
    Generator.cs        ← recipe → C# (parse snippet bodies, substitute fills, assemble)
    Program.cs          ← Main: build the recipe, run the generator, write out/ScriptData.cs
  gen/                  ← Step 2: emits Nodes.Generated.cs from the [Snippet] methods
  tests/                ← the regression net
  out/
    ScriptData.cs       ← the generated artifact (committed, to show the output)
```

### 7.2 Step 1 — core merge (the real proof)

- Implement `Block.Of`, the five snippets, the atoms, and **hand-written** recipe nodes
  (defer source-gen — it's mechanical; the *merge* is the risk).
- Implement the generator: parse each snippet body, substitute the three fill forms,
  inline, assemble, format, write the file.
- **Inline mode only.**

### 7.3 Step 2 — source-gen the nodes (only if Step 1 proves out)

- Replace the hand-written recipe nodes with a Roslyn **source generator** that
  reads `[Snippet]` methods and emits `IExpr<T>`/`IStmt` nodes from the fills.

### 7.4 Out of scope (parked — see §8)

Source-gen (until Step 2), `Call` mode, implicit literal conversions,
instance-derived names, context-based variations, raw/custom code, interface
renames, snippet base-class.

### 7.5 Done-when

1. Running the tool on the `loop + var + sum` recipe emits a `ScriptData.cs` that
   **compiles** and, when executed, **returns `10`** (`0+1+2+3+4`). ✅
2. **Editing a snippet body** (e.g. the loop bound `<` → `<=`) **flows through** to
   the output — proving the generator reads live snippet source, not a hardcoded
   template. ✅ *(Note: the output variable **name** is recipe-controlled — it comes
   from `LoopNode.I` / `Ref("i")`, not the marker. The `@i` marker is the fill's
   identity, bound by convention to the node field `I`; in Step 2 the field is
   generated from the marker, so they always agree.)*
3. A deliberately wrong recipe (a `string` producer into an `int` param) **fails to
   compile** — `CS1503: cannot convert from 'string' to 'IExpr<int>'`. ✅

---

## 8. Backlog

**This is the canonical backlog — record deferred/parked items here**, not in `NORTH-STAR.md`
(which holds only the north-star vision + the milestone *roadmap*, and references items here by
number). Revisit each **against running code**. As the spike grew, some items (the declaration
tier, #7–9) moved **into** it and now carry a status; the rest stay parked.

1. ✅ **DONE — Variable names on the instance, not a recipe field** — shipped as `Var<T>`
   handles (Phase 2b); a declaration binds a handle, a use references it, the name is chosen once.
2. ✅ **DONE — Implicit conversion operators for literals** — shipped as lever D
   (`implicit operator Expr<T>(T)` + generic `Lit<T>`). The "does it hide a type error?" test is
   answered in `BUILDING-STYLE.md` (the generic-`Lit<T>` fix keeps it exactly typed). One known
   trap fell out: the `==` operator does record-equality — see that doc.
3. **Recipe variations by context** — same recipe → different output by context
   (target framework, flags, config). *Test:* where the branch lives — recipe, a
   context object, or composition.
4. **Custom code without a recipe** — an escape hatch (`Raw("…")` node / passthrough
   block) for code not modeled as snippets. *Test:* coexistence with typed nodes;
   effect on the final-compile gate.
5. **Rename the interfaces** — *partially overtaken*: `IExpr<T>`→`Expr<T>` and `IStmt`→`Stmt`
   (the interface→record flips), though not semantically renamed. The names are still opaque;
   find clearer, domain-fit ones. *Test:* what reads best in a real recipe.
6. **Snippet base class vs attribute** — *sharpened (see `BUILDING-STYLE.md`).* A snippet could be a
   **class** — `class Loop(int count) : Snippet { public override void Body() {…} }` — instead of an
   annotated method: fills as ctor params (still *inner*-typed, so the template in `Body()` still
   compiles and the gen tool still lifts them), metadata (`Name`, `Style`) as overrides, no attribute.
   It **keeps** the compiling-template breakthrough and does **not** unify snippet with node (the node
   still needs *outer* `Expr<T>` types to compose, so `LoopNode`/factory stay generated). Real wins:
   key derived from the class name (kills the `"loop"` duplication), produced type explicit via the
   base (`Snippet` vs `Snippet<int>`) instead of the body-kind gotcha, and `Style` as a typed override
   that scales as metadata grows. *Decision:* keep methods now (terser, tiny catalog, Inline-only);
   switch to class-based snippets when metadata grows past key+style, a base needs shared behavior, or
   the declaration tier (#7) brings structured method/type snippets.

### The declaration tier (items 7–9: grow *up* from statement bodies)

Everything so far composes the **body** of a fixed shell — the tool hardcodes
`public static class ScriptData { public static int Run() { <recipe> } }` and the
recipe only fills the `<recipe>` statements. Items 7–9 are the same modeling move
applied one level up: from "compose statements" to "compose declarations." They
share a spine — a **member region** is to a type what a `Block` is to a method.

> **Status — M1 shipped the type tier** (`DECLARATION-TIER.md` / `NORTH-STAR.md` M1): hand-authored
> `TypeNode` nodes (record/class/struct/enum) + a `TypeEmitter`, gated by compile + reflect. So **#8 is
> done** (via structural nodes, not a type-`[Snippet]` — a deliberate divergence), **#9 is answered**
> (members are a typed `Field[]`, *not* a `Block` — a field slot rejects a statement, by design), and
> **#7 / #11 are partial** (types are modeled; a `MethodNode` and a single root/result spine are not).

7. **What the generated element *is*** — the output shell (class? method? what
   name / return type / params?) is **not modeled** today; it's baked into the
   generator. Model a declaration tier: a `MethodNode` (name, return type, params,
   `Block` body) and the existing recipe becomes that method's body, not the whole
   artifact. *Test:* does today's `Block`/`IStmt` nest cleanly as a `MethodNode`'s
   body with zero change below it? Does the class shell stay hardcoded, or become a
   `ClassNode` whose members are themselves nodes?
8. **Defining new types** — a recipe can't introduce a type (`record Foo(int X)`,
   `class Bar { … }`). Express a type definition as a `[Snippet]` whose body is a
   type decl — `@`-marker for the type name, a region fill for its members.
   *Test:* do the existing fill forms reach a type name + member list, or does a
   member region need a new form distinct from `Block` (you can't drop a statement
   where a field goes)?
9. **Adding a field to a type** — once a type is a node, attaching a field/property
   (`int @name;`) should be the *same* operation as adding a statement to a block:
   fill a region. *Test:* is a member list just another `Block` (region fill), so
   field-add ≡ statement-add — or do members need their own region type so the
   compiler rejects a statement-in-a-field-slot?

### Freer code at a position (item 10)

10. **Host-language code at typed leaf positions** — some fills are tedious to model
    as node trees but trivial to write as plain C#: the `If` condition is the
    motivating case, authored as a lambda `() => i < 3` instead of
    `new LessThanNode(new Ref(i), new Lit(3))`. A lambda buys real C# type-checking
    and IntelliSense at the leaf while staying capturable. Overlaps the `Raw("…")`
    escape hatch (#4), but is **typed** where Raw is a string blob. *Test:* can a
    lambda body be lowered to source deterministically (`CallerArgumentExpression`
    on the param, or expression-tree → text)? Where's the line — raw/lambda allowed
    **only** at typed leaf-expression slots, never where a `Block` is expected? And
    note the tension with operator sugar (see `BUILDING-STYLE.md`): `acc + i` solves
    the same "don't hand-build expression nodes" itch while staying *inside* the
    type system, where a lambda steps outside it.

### The type spine (item 11)

11. **A "result" type above `Block`** — today `Block` is the de-facto root (the
    recipe *is* a `Block`) and the surrounding element is hardcoded by the tool, so
    nothing models the **end result** as a value. Question: do we want a type above
    `Block` that names what's being produced (a method? a class? a file?), and is
    the whole model one **containment spine** — `<result> → Block → … → Var<T>` — with
    each tier owning the next? Overlaps #7 (the declaration tier models the
    *member*); this asks the orthogonal question of whether there's a single root
    node and a coherent spine all the way down to the variable handle. *Test:* is the
    root just the top declaration node from #7, or a distinct `Result`/`Program`
    wrapper? Is the spine pure **containment** (a result *has* blocks, a block *has*
    statements, a statement *references* a `Var<T>`), or does any tier actually want
    **inheritance** — and if nothing does, say so, so we don't reach for a base type
    the structure doesn't ask for.

### Recipe model & emitter (items 12–19: parked from the M1/M2 build)

Terminology settled here: **a recipe is any node** — the whole typed tree, primitive
(`RecordNode`, `LoopNode`, `Var`) up to a composite that builds a subtree. A node is *already* a
parameterized recipe, so there is no wrapper "recipe class." These are what the type-tier build
deliberately left for later; bracketed tags map to `NORTH-STAR.md` milestones.

12. **Catalog surface / matcher seam** [M3] — a recipe needs a catalog **name + description +
    param schema** so an LLM can *select and parameterize* it; an `IRecipe` **marker** over
    `Stmt`/`Expr`/`TypeNode` joins when a consumer (the matcher half of the system) needs to handle
    recipes uniformly. Built once at M2 and **removed as premature** — no consumer yet. *Test:* the
    marker stays a marker; composition slots stay typed (`Block` holds `Stmt[]`), or type-safety is lost.
13. **Composite recipes (`Build()` → subtree)** — a recipe that expands to *many* nodes (a service:
    class + interface + repo + DI), the Flutter `StatelessWidget.build()` pattern, typed so a
    composite *is-a* the category it produces. Reserve until a genuinely multi-node recipe forces it
    (`ScaffoldService`); a 1:1 wrapper earns nothing. Overlaps #18.
14. **Inheritance — a `Model` base** — a generated type declaring a base (`record User : Model`);
    needs **base-list support** in the emitter, and `Model` becomes the shared base entities inherit.
    Makes the inheritance half of #11 concrete. [before/with M4]
15. **Output target: namespace + folder** [M4] — a type name alone doesn't say *where* the file
    lands or *what namespace* it declares. A **wrapper on the emitted type** (not a change to the
    node model), forced when recipes reference each other across files (a `using` needs a namespace).
16. **`using`-derivation** — the emitter hardcodes `using System;`. Derive the using set from the
    `TypeRef`s a type actually references.
17. **Generated-type representation / `TypeRef` validator** — the forward-reference finding: `<T>`
    type-checks only **real** types; a type a sibling recipe *generates* lives in another compilation,
    so it's named by `TypeRef` (value-level), not `<T>`. `TypeRef` is therefore the fundamental
    representation and `<T>` is sugar for the known subset. Cross-use safety for generated types
    defers to the compile gate — or a recipe-level `TypeRef` validator if that proves insufficient.
18. **Members beyond fields; modifiers; base/interface lists** — methods/ctors/properties on a type
    (where the **body tier re-enters** as a member's body), access modifiers, base + interface lists.
    Overlaps #7 (method-as-node) and #13 (composite).
19. **Enum underlying type + explicit values** — `enum X : byte { A = 1 }`. The current `EnumNode`
    carries bare names only.

---

## 9. Open questions / risks

- **`@`-marker discovery robustness** — scanning parsed identifier nodes for `@`
  is clean, but verify it never collides with a legitimately-escaped keyword
  identifier the author actually wants literal.
- **Inline limits** — inlining is safe for single-expression / simple bodies;
  multi-statement snippets with locals need `Call` (Step-2+). Don't over-reach.
- **Cross-snippet scope** — snippets compile in isolation, but a recipe can still
  wire an out-of-scope name; that error only surfaces at the composition gate
  (§4.6). Acceptable, but the error message quality matters for agent-first.
- **Name wiring is stringly-typed** for now (`"acc"`, `"i"`). Backlog #1 addresses
  this; until then, recipe authors must keep names consistent by hand.
