# The A.Box Compilation Pipeline — Core Sketch

*Illustrative code sketch of the four moving parts at the heart of the idea: the **code templates**, the **LLM in the middle**, the **merging engine**, and the **final code state**. Everything here is **provisional pseudo-C#** — a thinking aid to make the abstract pipeline concrete, not settled design and not buildable as-is. For the prose treatment of the idea see [`golden-paths-compilation-pipeline.md`](golden-paths-compilation-pipeline.md); for the vendor/prior-art map see [`golden-paths-prior-art.md`](golden-paths-prior-art.md).*

---

## The running example

One intent, carried end to end:

> **"add a timestamp to the task object"**

Decomposes into five atomic operations — the same set the MTG-Arena analogy implies:

| # | operation    | what it contributes                          |
|---|--------------|----------------------------------------------|
| 1 | `get-timestamp` | a value (`DateTimeOffset.UtcNow`)         |
| 2 | `add-field`     | a structural mutation to `Task.cs`        |
| 3 | `load-instance` | fetch the entity from the repo            |
| 4 | `set-field`     | assign the value to the new field         |
| 5 | `save`          | persist the mutated entity                |

The whole pipeline is: **turn that sentence into a typed plan over these five, then render real code from pre-verified fragments.**

---

## Part 1 — Code templates

A template is **not** a code string. It is a typed fragment: named **input ports** and an **output port** (the composition contract from insight #3), plus a body with holes. The ports are what make illegal compositions fail to compose; the body is what gets rendered.

```csharp
// provisional — an operation template = typed ports + a fragment with {{holes}}
record Port(string Name, PortType Type);

record OperationTemplate(
    string                     Id,        // "add-field"
    IReadOnlyList<Port>        Inputs,    // typed input ports
    Port?                      Output,    // typed output port (null = pure side-effect)
    Placement                  Where,     // class-body? method-body? new-file?
    string                     Body);     // fragment, holes named after input ports

enum PortType { Timestamp, ClassRef, FieldName, ClrType, EntityRef, RepoRef, Value, Unit }
enum Placement { ClassBody, MethodBody, FileScope }
```

The five templates the example draws on. Note each declares its types — that is the whole point:

```csharp
// 1. get-timestamp :  () -> Timestamp
new OperationTemplate(
    Id: "get-timestamp",
    Inputs: [],
    Output: new Port("now", PortType.Timestamp),
    Where: Placement.MethodBody,
    Body: "DateTimeOffset.UtcNow");

// 2. add-field :  (ClassRef, FieldName, ClrType) -> ClassRef        [structural]
new OperationTemplate(
    Id: "add-field",
    Inputs: [ new("target", PortType.ClassRef),
              new("field",  PortType.FieldName),
              new("type",   PortType.ClrType) ],
    Output: new Port("target", PortType.ClassRef),
    Where: Placement.ClassBody,
    Body: "public {{type}} {{field}} { get; set; }");

// 3. load-instance :  (RepoRef, Value) -> EntityRef
new OperationTemplate(
    Id: "load-instance",
    Inputs: [ new("repo", PortType.RepoRef), new("id", PortType.Value) ],
    Output: new Port("entity", PortType.EntityRef),
    Where: Placement.MethodBody,
    Body: "var {{entity}} = {{repo}}.Get({{id}});");

// 4. set-field :  (EntityRef, FieldName, Value) -> EntityRef
new OperationTemplate(
    Id: "set-field",
    Inputs: [ new("entity", PortType.EntityRef),
              new("field",  PortType.FieldName),
              new("value",  PortType.Value) ],
    Output: new Port("entity", PortType.EntityRef),
    Where: Placement.MethodBody,
    Body: "{{entity}}.{{field}} = {{value}};");

// 5. save :  (RepoRef, EntityRef) -> Unit                           [side-effect]
new OperationTemplate(
    Id: "save",
    Inputs: [ new("repo", PortType.RepoRef), new("entity", PortType.EntityRef) ],
    Output: null,
    Where: Placement.MethodBody,
    Body: "{{repo}}.Save({{entity}});");
```

The **catalog** is just the set of these, indexed by `Id` — the moat and the bottleneck from the supporting insight.

---

## Part 2 — The LLM in the middle

The LLM does exactly **two bounded jobs**, and nothing else decides structure:

1. **Semantic-parse** the intent into a typed **operation plan** — picking only catalog `Id`s and binding every input port (insight #2: this is parsing-to-AST, not regex).
2. Flag anything it **cannot** match, rather than inventing code (the ~20% tail).

Its output is constrained to a schema — it returns *data*, never free-form source:

```csharp
// what the LLM emits — a plan, not code
record Binding;                                            // one of:
record Literal(object Value)            : Binding;         //   42, "Task", DateTimeOffset
record Ref(string FromResult)           : Binding;         //   another node's output
record Symbol(string RepoIdentifier)    : Binding;         //   _taskRepo, Task, taskId

record OpNode(
    string Template,                                       // must be a catalog Id
    IReadOnlyDictionary<string, Binding> Args,             // one per input port
    string Result);                                        // names this node's output

record OperationPlan(
    IReadOnlyList<OpNode> Nodes,
    IReadOnlyList<string> Unmatched);                      // the tail — empty if fully covered
```

The call itself is a structured-output request whose system prompt *is* the catalog:

```csharp
async Task<OperationPlan> ParseAsync(string intent, Catalog catalog) =>
    await _llm.CompleteJson<OperationPlan>(
        system: $"""
            Decompose the request into operations.
            You may ONLY use these templates and MUST bind every input port:
            {catalog.Signatures()}        // e.g. add-field(target:ClassRef, field:FieldName, type:ClrType) -> ClassRef
            Bind each input to a literal, a reference to an earlier Result, or a repo symbol.
            If an operation has no matching template, list it in Unmatched — never invent code.
            """,
        user: intent);
```

For the running example the LLM is expected to return roughly:

```csharp
new OperationPlan(
    Nodes: [
        new("get-timestamp", new() { }, Result: "now"),
        new("add-field",     new() { ["target"] = new Symbol("Task"),
                                      ["field"]  = new Literal("CreatedAt"),
                                      ["type"]   = new Literal("DateTimeOffset") }, Result: "Task"),
        new("load-instance", new() { ["repo"] = new Symbol("_taskRepo"),
                                      ["id"]   = new Symbol("taskId") },           Result: "task"),
        new("set-field",     new() { ["entity"] = new Ref("task"),
                                      ["field"]  = new Literal("CreatedAt"),
                                      ["value"]  = new Ref("now") },               Result: "task"),
        new("save",          new() { ["repo"]   = new Symbol("_taskRepo"),
                                      ["entity"] = new Ref("task") },              Result: "_"),
    ],
    Unmatched: []);            // fully covered — no tail this time
```

That object is the **boundary**: everything above it is non-deterministic, everything below it is deterministic. The LLM never touches the final source.

---

## Part 3 — The merging engine

Pure, deterministic, no LLM. It takes the plan + catalog and does five mechanical steps. The **type-check (step 2) is the load-bearing one** — it is what insight #3 ("AI merges it all together") actually cashes out to.

```csharp
// provisional — the deterministic core
class MergeEngine(Catalog catalog)
{
    public Unit Compose(OperationPlan plan)
    {
        // 1. resolve every node to its template; unknown Id => hard error
        var bound = plan.Nodes.Select(n => (n, tpl: catalog.Require(n.Template))).ToList();

        // 2. TYPE-CHECK the composition contract (insight #3):
        //    every Ref must point at an output whose PortType matches the consuming port.
        foreach (var (node, tpl) in bound)
            foreach (var port in tpl.Inputs)
                AssertTypeMatch(port, node.Args[port.Name], bound);   // illegal compositions fail HERE

        // 3. topological sort by Ref edges  (set-field after get-timestamp & load; save last)
        var ordered = TopoSort(bound);

        // 4. render each fragment, substituting holes from the bindings
        var rendered = ordered.Select(b => Render(b.tpl, b.node));

        // 5. route each rendered fragment to its Placement target
        return Route(rendered);          // ClassBody -> Task.cs ; MethodBody -> the service method
    }

    string Render(OperationTemplate tpl, OpNode node)
    {
        var code = tpl.Body;
        foreach (var port in tpl.Inputs)
            code = code.Replace($"{{{{{port.Name}}}}}", Resolve(node.Args[port.Name]));
        if (tpl.Output is { } o)
            code = code.Replace($"{{{{{o.Name}}}}}", node.Result);
        return code;
    }
}
```

`AssertTypeMatch` is the line that earns the whole "typed ports" claim: `set-field.value` is a `Value`, `get-timestamp` outputs a `Timestamp`, the binding is a `Ref("now")` → the engine confirms `Timestamp` is assignable to `Value` *before* rendering. Swap in an operation that outputs a `ClassRef` where a `Value` is wanted and it **doesn't compile the plan** — the failure is structural, not a runtime surprise.

### Where does `task.CreatedAt = now` actually come from?

This is the question the whole idea lives or dies on: the LLM emitted only the *data* `set-field(entity: Ref("task"), field: "CreatedAt", value: Ref("now"))` — so **who decided that "set a field" is spelled `object.x = y;` in C#?**

**A human did, once, when they authored the `set-field` template.** The C# *shape* is hand-written into the template body and never leaves it. Trace the single operation end to end:

```
(a) template body — authored ONCE by a human, lives in the catalog:
        "{{entity}}.{{field}} = {{value}};"          <-- the "x = y" knowledge is HERE

(b) LLM output — pure data, no syntax, picked from the catalog:
        set-field( entity: Ref("task"), field: Literal("CreatedAt"), value: Ref("now") )

(c) engine Render — three deterministic string substitutions, no model:
        "{{entity}}.{{field}} = {{value}};"
          ├─ {{entity}} ← Resolve(Ref("task"))      => "task"
          ├─ {{field}}  ← Resolve(Literal("CreatedAt")) => "CreatedAt"
          └─ {{value}}  ← Resolve(Ref("now"))        => "now"

(d) result line:
        task.CreatedAt = now;
```

So the generation step is **substitution, not authorship.** The LLM chose *which* template (`set-field`) and *what* to bind into its holes; it never produced the `=` or the `.` or the `;`. Pull the LLM out after step (b) and the exact same line still renders. That is the entire point of the architecture — and the test of whether a future change has quietly cheated: **if any C# token in the output was decided by the model rather than copied from a template body, the LLM has leaked back into codegen.**

The cost of this guarantee is exactly the moat/bottleneck the idea doc names: *every* surface form the system can emit must pre-exist as a hand-authored (or codebase-mined) template body. `set value → x = y` is in the catalog because someone put it there. An operation whose C# shape nobody authored is the ~20% tail — it lands in `Unmatched`, and the only honest options are *author a new template*, *mine one*, or *escalate* — **never** "let the model write that bit." (And `Render`'s naïve `string.Replace` is itself a sketch-grade stand-in: a real engine substitutes into a parsed syntax tree so placement, formatting, and conflicts are handled structurally — but that changes the *mechanism*, not the principle that the shape comes from the template, not the model.)

---

## Part 4 — The final code state

The engine routed two fragments to two destinations. **The plan is gone** — what lands in the repo is ordinary, diffable, convention-following source:

```csharp
// Task.cs  —  from the `add-field` fragment (Placement.ClassBody)
public sealed class Task
{
    public Guid   Id    { get; set; }
    public string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }     // <-- added
}
```

```csharp
// TaskService.cs  —  from load / set / save fragments (Placement.MethodBody), topo-ordered
public void Touch(Guid taskId)
{
    var task = _taskRepo.Get(taskId);
    task.CreatedAt = DateTimeOffset.UtcNow;
    _taskRepo.Save(task);
}
```

No engine ships with this code, no graph is interpreted at runtime, no platform dependency — exactly the "compiles the graph away" property from the idea doc. From here the two validation gates run:

- **Step 5 (structural, deterministic):** does each fragment still satisfy its template's invariants in context — did `add-field` produce a well-formed auto-property, is the field type the one the port promised?
- **Step 6 (intent, LLM-judge — the weak link):** does `Touch` actually mean *"a Task now carries a timestamp"*? Strengthen with an intent-derived test rather than a raw judge.

---

## What the sketch makes concrete

| Idea-doc claim | Where it becomes real here |
|---|---|
| "decompose into atomic operations" | `OperationPlan.Nodes` — the LLM's structured output |
| "match each to a template fragment" | `OpNode.Template` must be a catalog `Id`; `catalog.Require` |
| "typed composition contract" (insight #3) | `Port` + `PortType` + `AssertTypeMatch` — illegal plans fail before render |
| "semantic parse, not regex" (insight #2) | `ParseAsync` is a constrained LLM call returning a typed plan |
| "budget for the ~20% tail" (insight #2) | `OperationPlan.Unmatched` |
| "the LLM never decides the shape" | the engine is pure; the LLM emits data, not source |
| "compiles the graph away" (low-code comparison) | Part 4 — plain `Task.cs` / `TaskService.cs`, no runtime graph |

The honest gaps this sketch exposes (and the idea doc's open questions name): `PortType` is a toy enum — the real composition contract needs `ClrType`-aware matching; `Placement` hand-waves *where in the file* a fragment lands (real merge needs an AST edit, not string replace); and `add-field` (schema change) vs the runtime ops sit at the two different granularities insight #1 says to make nested. The next section retires the first two of those gaps.

---

## Variant: templates as type-checked C# (attributes + source generator)

The Parts 1–4 sketch has a load-bearing weakness: **the template body is a string, so nothing type-checks it.** `"{{entity}}.{{field}} = {{value}};"` is opaque to the compiler — a malformed fragment or a type mismatch between the bound value and the field surfaces only *after* the output is emitted and recompiled. That pushes correctness back toward "trust the model," which is what the whole architecture is trying to avoid.

The fix is to make a template a **real, compilable C# method** carrying a marker attribute, and **source-generate the descriptor (the "data packet") from its signature.** The template body is then checked by the C# compiler at authoring time, and — the headline — **insight #3's typed composition contract stops being the toy `PortType` enum and becomes the actual CLR type system.**

### Expression-shaped templates compile as-is

```csharp
[Template("get-timestamp", ApplyTo.Expression)]
static DateTimeOffset GetTimestamp() => DateTimeOffset.UtcNow;

[Template("sum", ApplyTo.Expression)]
static int Sum(int x, int y) => x + y;        // input ports = parameters; output port = return type
```

The generator harvests each `[Template]` into the descriptor Part 1 hand-wrote — but now derived, so the two **cannot drift**:

```csharp
// GENERATED from the [Template] above — no longer hand-maintained
static readonly OperationTemplate Sum = new(
    Id: "sum",
    Inputs: [ new("x", typeof(int)), new("y", typeof(int)) ],
    Output: new("result", typeof(int)),
    Body:   /* captured method-body syntax node */);
```

`AssertTypeMatch` now compares real `Type`s (`typeof(DateTimeOffset)` assignable to the consuming port) instead of enum tags — the contract is the language's, not a re-implementation of it.

### Structural templates need a placeholder type — the wall, and the way through

Expression shapes are the easy half. `set-field` is the hard half: **you cannot write `entity.{{field}} = value` as compilable C#**, because the member is dynamic. This is exactly the case string templates *hide* and real C# *exposes*.

The escape is a **placeholder record whose only job is to type-check and then be erased** — the "base struct/record mostly for token replacement":

```csharp
readonly record struct Field<TEntity, TValue>(string Name)
{
    public void Set(TEntity entity, TValue value) { }     // never executed — exists so the template compiles
}

[Template("set-field", ApplyTo.Statement)]
static void SetField<TEntity, TValue>(TEntity entity, Field<TEntity, TValue> field, TValue value)
    => field.Set(entity, value);                          // type-checks against the placeholder
```

`TValue` ties the field's declared type to the value's type, so binding a `DateTimeOffset` value into an `int` field is now a **compile error in the template instantiation** — the safety the string version structurally cannot give. At merge the engine **lowers** the placeholder call back to idiomatic code via a rule keyed on the placeholder type, and the `Field<,>` indirection never reaches the repo:

```
merge-time lowering (keyed on placeholder type):
    Field<,>.Set(e, v)  ->  e.<Field.Name> = v;       // task.CreatedAt = now;
    Field<,>.Get(e)     ->  e.<Field.Name>
```

This strengthens the leak-test from Part 3: the only C# tokens that exist were compiler-checked at authoring time; merge is mechanical lowering of type-checked syntax, not string assembly.

### This rides proven machinery, not a research project

| Precedent | What it already provides |
|---|---|
| `Expression<Func<…>>` (LINQ expression trees) | the .NET pattern for "a type-checked code fragment captured as data, not run" — the value/expression templates can *be* expression trees |
| Roslyn incremental source generators + marker attributes | the standard harvest-attributes → emit-descriptor pipeline (`[GeneratedRegex]`, `[LoggerMessage]`, JSON source-gen) |
| Typed quasiquotation (Template Haskell typed splices, MetaOCaml `.<e>.`, Scala quasiquotes) | the academic lineage of "code templates the compiler type-checks *before* splicing" |

### What this variant moves to the open list

- **Two template kinds, not one.** Expression-shaped templates compile directly; structural/metaprogramming ones (`set-field`, `add-field`) need placeholder types + lowering rules. The catalog now has two authoring modes.
- **Compose by inlining vs by calling.** Idiomatic git-native output (the whole point) wants the method *body* spliced in, not a call to a helper left behind — that means Roslyn body-inlining with capture/rename, real work beyond `string.Replace`.
- **Generic instantiation at merge.** The LLM binds `TValue = DateTimeOffset`; the engine must instantiate the generic template before lowering.
- **Declaration-shaped ops stretch the frame.** `add-field` emits a *member declaration*, not a statement — `ApplyTo.Member` over a property in a template class works, but it's no longer "just a method."

This variant retires the `PortType`-is-a-toy and string-replace gaps; it does **not** resolve insight #1's granularity question, which sits above the template-representation choice.

---

*See [`golden-paths-compilation-pipeline.md`](golden-paths-compilation-pipeline.md) for the idea in prose and the open design questions, and [`golden-paths-prior-art.md`](golden-paths-prior-art.md) for who builds each of these pieces today.*
