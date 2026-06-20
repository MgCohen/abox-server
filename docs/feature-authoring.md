# Authoring a feature

The single front door for adding a vertical slice to `/src`. It fixes the three
things that keep going wrong: the word **"feature"** means three different things,
**what goes in which folder** is scattered across four docs, and the **csproj
shape** drifts. Read this before you touch `src/Features`.

It is the *what* and the *how*. The *why* lives in the ADRs — this cites them, it
does not re-argue them. Canonical worked example: [`src/Features/Projects`](../src/Features/Projects).
Placement rules in full: [`PLANS/structure.md`](../PLANS/structure.md). Shape decision:
[ADR 0011](../design/adr/0011-canonical-feature-slice-shape.md).

---

## 1. Terminology — one word per concept

The repo overloaded "feature" three ways (ADR 0011 opens on exactly this). Use
these words, and only these, from here on. When you mean the concept, say
**capability**; when you mean the code, say **slice**.

| Word | Means | Is NOT | In code |
|---|---|---|---|
| **Capability** | A product thing the system does ("manage projects", "launch a flow"). The PRD/feature-map sense. | a folder, an assembly | `PLANS/rebuild/01-feature-map.md` |
| **Slice** | The code that implements one capability end to end: HTTP → logic → persistence. **One slice = one capability = one `src/Features/<F>` folder = one implementation assembly.** | a verb, a DTO | `src/Features/Projects/` |
| **Verb** (use case) | One operation within a slice — Add, Get, List, Update, Delete. A **folder** inside the slice holding exactly one endpoint. | its own assembly, its own slice | `src/Features/Projects/Add/` |
| **Endpoint** | The `internal sealed : Endpoint<,>` class that is the HTTP boundary for one verb. Transport only. | the place for business rules | `AddProjectEndpoint` |
| **Module** | The slice's one public face: `EndpointsAssembly` (+ optional `AddX()` DI). The only type Host can name. | a place for logic | `ProjectsModule` |
| **Contracts** | The leaf assembly the client and peer slices bind: requests, responses, DTOs, events, published port interfaces. | a place for behavior or entities | `ProjectDto`, `CreateProjectRequest` |
| **Aggregate** | A `Domain/<X>` unit of state + invariants, shared because **two slices enforce the same rules** on it. | a per-slice data bag | `Domain/Projects/Project` |

> "Feature" remains fine in prose as a loose synonym for **capability**. In folder,
> assembly, and rule names it always resolves to **slice** — never a verb.

---

## 2. The shape — one slice, two assemblies

A slice is **exactly one implementation assembly + one Contracts leaf** (ADR 0011
D2). Verbs are folders inside the impl assembly; the Module is folded in. No
per-verb, per-Module, or `Shared` sub-assemblies — that is the drift the Structure
rule rejects.

```
src/Features/<F>/
  ABox.<F>.csproj            ← THE implementation assembly (the whole slice)
    <Verb>/<Verb>Endpoint.cs   one folder per verb, one internal-sealed endpoint each
    Module/<F>Module.cs        the one public type: EndpointsAssembly (+ AddX() DI seam)
  Contracts/
    ABox.<F>.Contracts.csproj ← leaf: requests, responses, DTOs, events, port interfaces
```

Reference: Projects is `ABox.Projects.csproj` + `ABox.Projects.Contracts.csproj`,
nothing else. Inbox matches it. Flows/Git/Tasks are mid-migration to this shape
(ADR 0011 Gate 5) and are the *only* features allowed to differ, by an explicit,
shrinking allow-list — do not add to it.

---

## 3. What goes in which folder

The decision your slices keep getting wrong. Match the thing you're adding to the
left column; put it where the right column says.

| You are adding… | It goes in | Because |
|---|---|---|
| The HTTP shape for one verb (route, bind, validate-inline, respond) | `Features/<F>/<Verb>/<Verb>Endpoint.cs` | The endpoint is the transport boundary, one per verb. |
| A request, response, DTO, or event the **client** or a **peer slice** reads | `Features/<F>/Contracts/` | Contracts is the published, bindable leaf — the only thing outside the slice may name. |
| A published **port interface** a peer binds (e.g. `IPullRequests`) | `Features/<F>/Contracts/` | The interface is the channel; the impl stays internal to the slice. |
| DI registration / the assembly-scan anchor | `Features/<F>/Module/<F>Module.cs` | One public face per slice; Host composes from it. |
| Business rules / entity state with invariants used by **this slice only** | a folder **inside** `Features/<F>/` (impl assembly), until a second slice needs it | YAGNI — promote on the *second* use (§4), not the first. |
| State + invariants **two slices** must enforce identically | `Domain/<Aggregate>/` | `Domain/` = shared **rules**, not shared data. Entry bar is shared invariants. |
| Business-agnostic plumbing (persistence, git, subprocess, `Result<T>`) | `Infrastructure/` | The floor: depends on nothing, anything may depend on it. |

**Never put in Contracts:** an endpoint, a domain entity, anything with behavior
beyond a record. Contracts is a leaf — it references nothing internal, so a domain
type cannot even compile there.

**Never put in `Domain/`:** a per-slice data bag, a `Common` junk drawer, or a type
only one slice touches. One real shared invariant is the entry fee.

**Never put in a verb folder:** a request/response/DTO — those live in `Contracts/`
so the client can bind them. The verb folder holds its endpoint.

---

## 4. The sharing ladder — promote on the second use

Don't reach for `Domain/` or `Infrastructure/` on the first use. Climb only as far
as a real second consumer forces you (structure.md Rule 5 / 11).

1. **A peer needs to trigger you or read a projection** → publish an op + flat DTO
   (or an event) in your `Contracts`. Your model stays owned and internal.
2. **You need a rich result back from a substrate to continue your own logic** → a
   downward **port** call (`IAgentRuntime.Run(req) → AgentResult`). Operations, not
   the model.
3. **Two slices must enforce the same invariants on a type** → only now promote it
   to a `Domain/<Aggregate>`.

Discriminator for 1 vs 2: *"I need a result back to continue"* → downward port
call; *"someone might react later"* → sideways event.

---

## 5. Add a slice — the checklist

Copy [`src/Features/Projects`](../src/Features/Projects) and adapt. For a slice `<F>`
with verbs `Add`/`Get`/`List`/…:

1. `Features/<F>/Contracts/ABox.<F>.Contracts.csproj` — leaf, references nothing
   internal. Put each request/response/DTO/event here as a `sealed record`.
2. `Features/<F>/ABox.<F>.csproj` — the impl assembly. References its own
   `Contracts`, the `Domain.<Aggregate>` it needs, `Infrastructure`, `FastEndpoints`.
3. One folder per verb, one `internal sealed class <Verb>Endpoint : Endpoint<Req,Res>`
   (or `EndpointWithoutRequest<Res>`). `Configure()` sets route+verb; `HandleAsync()`
   takes collaborators by constructor.
4. `Features/<F>/Module/<F>Module.cs` — `public static class <F>Module` exposing
   `public static Assembly EndpointsAssembly => typeof(<AnyEndpoint>).Assembly`. The
   only public type in the assembly.
5. Wire it: Host references `ABox.<F>.Module` and composes the FastEndpoints
   assembly list from each `Module.EndpointsAssembly` — no Host→verb edge.
6. Tests mirror `src/`: one `tests/.../Features/<F>.Tests` project, not one per verb.

Request validation is **provisionally** inline in `HandleAsync` (`AddError` →
`Send.ErrorsAsync(400)`, as in `AddProjectEndpoint`); it moves to a **Step**
(R-SPINE) later. FastEndpoints is the transport boundary only — no `Validator<T>`
(ADR 0009).

---

## 6. How this is enforced

Structure is walled in the agent's own compile/test loop, not by this prose —
prose is what agents drift from. The on-disk Structure rules (`tests/Tests/Structure`)
and the namespace Arch rules (`tests/Tests/Arch`) fire before review:

- **Each feature is one implementation project plus one Contracts leaf** — rejects a
  per-verb / `Shared` sub-project the moment it lands (ADR 0011 D2).
- **Each verb folder declares its endpoint** — every verb folder in a canonical
  slice carries a `*Endpoint`, so a folder with no endpoint (or a misplaced one) fails.
- **Requests, responses, and DTOs live in the Contracts leaf** — a `*Request` /
  `*Response` / `*Dto` / `*View` type outside a `Contracts/` folder fails, so DTOs
  can't hide in a verb folder where the client can't bind them.
- **Feature endpoints are internal sealed** / **the impl assembly exports only its
  Module** / **dependencies flow down only** / **features must not depend on each
  other** — the Arch reference-graph walls (ADR 0011 D3).

If one of these fails, fix the placement — do not widen the allow-list. The
allow-lists (`FeatureShape.PendingConsolidation`) exist only for the three
pre-migration laggards and are designed to shrink, never grow.
