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

The honest gaps this sketch exposes (and the idea doc's open questions name): `PortType` is a toy enum — the real composition contract needs `ClrType`-aware matching; `Placement` hand-waves *where in the file* a fragment lands (real merge needs an AST edit, not string replace); and `add-field` (schema change) vs the runtime ops sit at the two different granularities insight #1 says to make nested.

---

*See [`golden-paths-compilation-pipeline.md`](golden-paths-compilation-pipeline.md) for the idea in prose and the open design questions, and [`golden-paths-prior-art.md`](golden-paths-prior-art.md) for who builds each of these pieces today.*
