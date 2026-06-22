# Gate-5 feature migration — Flows, Git, Tasks → canonical FastEndpoints slice

Status: **planned (2026-06-15).** Self-contained — readable without prior context.
This is the **execution detail** that [`08-vsa-feature-template.md`](08-vsa-feature-template.md)
defers to its Gate 5. It ports the three remaining Minimal-API features
([Flows](../../../src/Features/Flows), [Git](../../../src/Features/Git),
[Tasks](../../../src/Features/Tasks)) to the canonical vertical-slice shape already
worked out by [`Features/Projects`](../../../src/Features/Projects). No new
capability — internals only.

## Goal + behavior lock

The rebuild's prime directive is **"the user can't tell the difference in what the
system does."** This is a framework migration ([ADR 0009](../../decisions/0009-fastendpoints-http-boundary.md))
plus a project consolidation ([ADR 0011](../../decisions/0011-canonical-feature-slice-shape.md)),
not a behavior change. Every route, status code, header, and response body stays
**byte-identical** across the port.

Each feature ends behind a **Wire-level behavior-parity gate**: a
`WebApplicationFactory<Program>` test ([`WireApp.cs`](../../../tests/Tests/Wire/Support/WireApp.cs)
boots the real `Program` over an in-memory `TestServer`) asserting the HTTP surface
is unchanged before/after. One such test already exists for the Flows SSE contract
([`WireTests.cs:211`](../../../tests/Tests/Wire/Tests/WireTests.cs) — `POST /flows`
then `GET /flows/{id}/events`), so the gate's hardest case is already covered.
**Halt condition:** Wire parity breaks → the migration changed behavior; stop.

## The canonical target + the standard transform

The target is exactly [`Features/Projects`](../../../src/Features/Projects): one impl
csproj `ABox.<F>.csproj` (verbs as folders, `Module` folded in), one
`Contracts/ABox.<F>.Contracts.csproj` leaf, every endpoint `internal sealed`,
discovery via `Module.EndpointsAssembly`. Per-endpoint, the transform is mechanical
and identical everywhere:

| Minimal API (today) | FastEndpoints (target) |
|---|---|
| `public static class XEndpoint` | `internal sealed class XEndpoint(deps) : Endpoint<TReq,TRes>` (or `EndpointWithoutRequest<TRes>`) |
| `static Map(grp) => grp.MapPost("/...", (req, dep) => …)` | `Configure() { Post("/..."); AllowAnonymous(); }` |
| route param + DI args in the lambda | ctor injection for deps; route/body bound to `TReq`; `Route<Guid>("id")` for path params |
| `Results.Ok(x)` / `NotFound()` / `BadRequest(e)` / `Accepted()` / `Created…` | `Send.OkAsync(x)` / `Send.NotFoundAsync()` / `AddError(…)+Send.ErrorsAsync(400)` / `Send.NoContentAsync` + status / `Send.CreatedAtAsync<…>()` |
| `MapGroup("/flows")` prefix in `Module` | the prefix is spelled out in each endpoint's `Configure()` route (FE has no group seam) |

The `AddError`→`Send.ErrorsAsync(400)` shape is exactly
[`AddProjectEndpoint.cs:22`](../../../src/Features/Projects/Add/AddProjectEndpoint.cs);
copy it.

**Per-feature wiring transform (do once per feature):**

1. Create `src/Features/<F>/ABox.<F>.csproj` with `<DefaultItemExcludes>…Contracts/**`,
   `FrameworkReference Microsoft.AspNetCore.App`, `PackageReference FastEndpoints 8.1.0`,
   and `ProjectReference` to its `Contracts`, the `Domain.*` it needs, and
   `Infrastructure` — mirror [`ABox.Projects.csproj`](../../../src/Features/Projects/ABox.Projects.csproj).
2. Move each verb's endpoint into `<F>/<Verb>/<Verb>Endpoint.cs`, rewritten per the
   table; keep the `Contracts` leaf untouched.
3. Fold `Module` into the feature assembly: `<F>Module` exposes
   `static Assembly EndpointsAssembly => typeof(SomeEndpoint).Assembly` and keeps
   `AddX()` (DI). Delete `MapX()`.
4. In [`Composition.cs:29`](../../../src/Host/Composition.cs), add the feature's
   `EndpointsAssembly` to `o.Assemblies = [...]`.
5. In [`Program.cs:17-19`](../../../src/Host/Program.cs), delete the feature's `MapX()`
   call.
6. In [`ABox.slnx`](../../../ABox.slnx), remove the now-deleted per-verb/`Module` csproj
   entries, add the single `ABox.<F>.csproj`.
7. Drop the feature from **both** Gate-3 allow-lists:
   [`EndpointConformance.PendingFastEndpointsMigration`](../../../tests/Tests/Arch/Support/EndpointConformance.cs)
   and [`FeatureShape.PendingConsolidation`](../../../tests/Tests/Structure/Support/FeatureShape.cs).

**The staleness guards make this atomic.** Both Gate-3 Rules carry a staleness check
that fails the build if a feature is *already* canonical but *still* listed
([`StructureTests.cs:64`](../../../tests/Tests/Structure/Tests/StructureTests.cs),
[`RuleTests.cs:76`](../../../tests/Tests/Arch/Tests/RuleTests.cs)). A half-migrated
feature (consolidated csprojs but endpoints not yet `internal sealed`, or vice
versa) fails one guard or the other. So each feature must migrate **all the way, in
one PR**.

**Done-when (per feature):** warning-free `dotnet build ABox.slnx`; `dotnet test`
green; the Wire-parity test green; and the two Gate-3 Rules now assert the feature
**positively** (its allow-list entry removed, staleness check satisfied).

---

## Flows — first, the representative spike

Flows proves the whole transform: it is the only feature exercising SSE, a `Module`
with real DI logic, intra-feature `Shared`/helper code, **and** the 9→2 csproj
collapse together.

### Current shape (9 csproj)

| csproj | holds | route |
|---|---|---|
| [`Catalog`](../../../src/Features/Flows/Catalog/CatalogEndpoint.cs) | `CatalogEndpoint` | `GET /catalog` (NOT under `/flows`) |
| [`Start`](../../../src/Features/Flows/Start/StartEndpoint.cs) | `StartEndpoint` + [`ProjectResolver`](../../../src/Features/Flows/Start/ProjectResolver.cs) | `POST /flows` |
| [`List`](../../../src/Features/Flows/List/ListEndpoint.cs) | `ListEndpoint` | `GET /flows` |
| [`Get`](../../../src/Features/Flows/Get/GetEndpoint.cs) | `GetEndpoint` (ETag/304) | `GET /flows/{id:guid}` |
| [`Cancel`](../../../src/Features/Flows/Cancel/CancelEndpoint.cs) | `CancelEndpoint` | `POST /flows/{id:guid}/cancel` |
| [`Watch`](../../../src/Features/Flows/Watch/WatchEndpoint.cs) | `WatchEndpoint` + [`Sse`](../../../src/Features/Flows/Watch/Sse.cs) | `GET /flows/{id:guid}/events` (SSE) |
| [`Shared`](../../../src/Features/Flows/Shared/FlowMapping.cs) | `FlowMapping.ToView()` extension | — |
| [`Module`](../../../src/Features/Flows/Module/FlowsModule.cs) | `FlowsModule` (`AddFlows`+`MapFlows`) + [`FlowFactory`](../../../src/Features/Flows/Module/FlowFactory.cs) | — |
| [`Contracts`](../../../src/Features/Flows/Contracts) | `FlowView`/`FlowInfo`/`StartRunRequest`/… | — |

Routing today: [`FlowsModule.cs:36`](../../../src/Features/Flows/Module/FlowsModule.cs)
maps `Catalog` at the app root, then `MapGroup("/flows")` for Start (at `/`), List,
Get, Cancel, Watch.

### Target shape (2 csproj)

`ABox.Flows.csproj` (folders `Catalog/ Start/ List/ Get/ Cancel/ Watch/ Module/`,
plus `ProjectResolver`/`FlowFactory`/`FlowMapping` folded in as plain files) +
`ABox.Flows.Contracts.csproj` (unchanged).

### Step list

1. New `ABox.Flows.csproj` referencing `Contracts`, `Domain.Flow`, `Infrastructure`,
   FastEndpoints; `DefaultItemExcludes` for `Contracts/**`.
2. Rewrite all six endpoints as `internal sealed : Endpoint<,>`. Each route is
   spelled out in full (`Post("/flows")`, `Get("/flows/{id}")`, `Get("/catalog")`,
   etc.) — there is no `MapGroup` in FE, so the `/flows` prefix moves into each
   `Configure()`.
3. Fold `FlowMapping`, `ProjectResolver`, `FlowFactory` into the assembly as files
   (delete `Shared/`, `Start/`, `Module/` *projects*; keep them as *folders*).
4. `FlowsModule.AddFlows` keeps its DI body verbatim; add `EndpointsAssembly`; delete
   `MapFlows`.
5. Composition: add `FlowsModule.EndpointsAssembly`; Program: delete `MapFlows()`.
6. slnx: remove the 7 sub-csprojs (`Catalog/Start/List/Get/Cancel/Watch/Shared/Module`),
   add `ABox.Flows.csproj`; keep `Contracts`.
7. Drop `Flows` from both allow-lists.

### Hard parts

- **(a) SSE `Watch`** — the one non-trivial port ([ADR 0009](../../decisions/0009-fastendpoints-http-boundary.md)
  flagged it explicitly). Today [`Sse.Stream`](../../../src/Features/Flows/Watch/Sse.cs)
  sets `Content-Type: text/event-stream` + `Cache-Control: no-cache` by hand and, for
  each `FlowSnapshot` off `FlowRegistry.Changes(id, ct)`, writes
  `data: {json}\n\n` and flushes. FastEndpoints' equivalent is `Send.EventStreamAsync`
  (it owns the `text/event-stream` headers and the `data:`/`\n\n` framing). **Risk:**
  FE's framing must reproduce today's bytes exactly — same `data: ` prefix, same blank-line
  terminator, the same per-snapshot `WireJson.Options` serialization, and the same flush
  cadence (the existing Wire test reads the full stream as a string and asserts the run id
  + `"Completed"` appear, so framing drift would surface there). If FE's helper can't match
  the wire byte-for-byte, fall back to writing `HttpContext.Response.Body` directly inside
  `HandleAsync` (FE exposes `HttpContext`), porting `Sse.Stream` near-verbatim — that keeps
  the bytes identical at the cost of not using the FE helper. **Decide which during the spike;
  this is the single highest-risk item in Gate 5.**
- **(b) `FlowsModule` builds the `FlowCatalog`.** Unlike Projects' 1-line `Module`,
  [`AddFlows`](../../../src/Features/Flows/Module/FlowsModule.cs) eagerly builds a
  `FlowCatalog` (fail-fast on a bad entry) and registers each flow type transient
  (ADR 0001). The canonical `Module` is a thin shim, but this is **legitimate DI
  registration, not routing** — it stays as the feature's `AddFlows()` body, exactly
  where it is. The canonical shape's "no Module-with-logic" concern is about
  *routing* logic (the retired `MapFlows`), not DI; catalog-build is DI and has a
  home. No relocation needed.
- **(c) helpers fold in.** `FlowMapping` (a `ToView()` extension on `FlowSnapshot`),
  `ProjectResolver` (used by `Start`), and `FlowFactory : IFlowFactory` (used by
  `AddFlows`) become plain files in the one assembly. They were only ever consumed
  inside Flows, so no contract changes.
- **(d) 9→2 collapse.** Merge/delete: `Catalog`, `Start`, `List`, `Get`, `Cancel`,
  `Watch`, `Shared`, `Module` → folders inside `ABox.Flows.csproj`. Keep: `Contracts`.
  Net: 8 csprojs deleted, 1 created, `Contracts` retained = 2.

### Done-when

Build warning-free; all tests green incl. the SSE Wire test; Flows endpoints are
`internal sealed` and Flows is one impl + one Contracts; both Gate-3 Rules now assert
Flows positively (removed from both allow-lists).

---

## Git — PR read/merge

### Current shape (4 csproj)

| csproj | holds | route |
|---|---|---|
| [`PrList`](../../../src/Features/Git/PrList/PrListEndpoint.cs) | `PrListEndpoint` | `GET /git/prs` (optional `?project=`) |
| [`PrOps`](../../../src/Features/Git/PrOps/PrMergeEndpoint.cs) | `PrMergeEndpoint` | `POST /git/prs/{number:int}/merge` |
| [`Module`](../../../src/Features/Git/Module/GitModule.cs) | `GitModule` (`AddGit`+`MapGit`) + [`StubPullRequests`](../../../src/Features/Git/Module/StubPullRequests.cs) | — |
| [`Contracts`](../../../src/Features/Git/Contracts) | `IPullRequests`/`PullRequestDto`/`MergeResult` | — |

`MapGit` uses `MapGroup("/git/prs")`.

### Target shape (2 csproj)

`ABox.Git.csproj` (folders `PrList/ PrOps/ Module/`, `StubPullRequests` folded in) +
`ABox.Git.Contracts.csproj` (unchanged).

### Step list

Standard transform: two endpoints → FE classes (`Get("/git/prs")`,
`Post("/git/prs/{number}/merge")` with `Route<int>("number")`); `PrList` binds the
optional `?project=` query into its request DTO or reads it via `Query<string?>`.
`AddGit` keeps its `StubPullRequests` registration + `EndpointsAssembly`; `MapGit`
deleted. Wire Composition/Program/slnx/allow-lists per the standard steps.

### Hard parts

- **`Domain/Git` is OUT OF SCOPE.** [`src/Domain/Git/ABox.Domain.Git.csproj`](../../../ABox.slnx)
  is a separate Domain actor (the real Git substrate), not part of this feature
  slice. Do not touch it. This migration is only the `Features/Git` HTTP slice, whose
  PR data is still the provisional [`StubPullRequests`](../../../src/Features/Git/Module/StubPullRequests.cs).
- `PrMergeEndpoint` returns 404 when the PR number is absent, 200 `MergeResult`
  otherwise — preserve both. `PrListEndpoint`'s `?project=` defaults to `"."`.

### Done-when

Standard: build warning-free, tests green, Wire-parity green (`GET /git/prs`,
`POST /git/prs/{n}/merge`), Git removed from both allow-lists.

---

## Tasks — smallest, the clean confirmation

Once Flows proves the transform, Tasks confirms it is mechanical.

### Current shape (3 csproj)

| csproj | holds | route |
|---|---|---|
| [`Create`](../../../src/Features/Tasks/Create/CreateTaskEndpoint.cs) | `CreateTaskEndpoint` | `POST /tasks` |
| [`Module`](../../../src/Features/Tasks/Module/TasksModule.cs) | `TasksModule` (`MapTasks` only — no `AddTasks`) | — |
| [`Contracts`](../../../src/Features/Tasks/Contracts) | `CreateTaskRequest`/`TaskDto` | — |

`MapTasks` uses `MapGroup("/tasks")`. Note: Tasks has **no `AddX()` DI seam** today
(its only dep, `IPullRequests`, is registered by `AddGit`); the new `TasksModule`
still needs `EndpointsAssembly`, and may add an empty/trivial `AddTasks()` only if a
DI registration is actually needed (none is today — YAGNI: skip it).

### Target shape (2 csproj)

`ABox.Tasks.csproj` (folders `Create/ Module/`) + `ABox.Tasks.Contracts.csproj`.

### Step list

`CreateTaskEndpoint` → `internal sealed : Endpoint<CreateTaskRequest, TaskDto>`,
`Post("/tasks")`, body → `Send.OkAsync(new TaskDto(...))`. `TasksModule` exposes
`EndpointsAssembly`; delete `MapTasks`. Standard wiring + allow-list drop.

### Hard parts

- **Cross-feature reference must survive.** [`CreateTaskEndpoint`](../../../src/Features/Tasks/Create/CreateTaskEndpoint.cs)
  reads open PRs through Git's published `IPullRequests` **contract** (the sanctioned
  decoupled mode), so `ABox.Tasks.csproj` keeps a `ProjectReference` to
  `ABox.Git.Contracts.csproj` (as `ABox.Tasks.Create.csproj` does today). This stays a
  contract-only edge — never Git's impl — so the cross-feature wall holds. Tasks can
  migrate before or after Git (it depends only on Git's *Contracts*, untouched by
  Git's migration); ordering it last keeps the dependency-target stable.

### Done-when

Standard: build warning-free, tests green, Wire-parity green (`POST /tasks`), Tasks
removed from both allow-lists.

---

## Cross-cutting notes

- **Validation stays provisional + inline.** Keep the `AddError`→`Send.ErrorsAsync(400)`
  pattern in `HandleAsync` (as [`AddProjectEndpoint`](../../../src/Features/Projects/Add/AddProjectEndpoint.cs)
  does). Do **not** define `Validator<T>` — FastEndpoints is the transport boundary
  only; validation-as-a-Step (R-SPINE) is deferred ([ADR 0009](../../decisions/0009-fastendpoints-http-boundary.md)).
- **One focused PR per feature**, in order **Flows → Git → Tasks**, each with its
  Wire-parity gate. The allow-lists then shrink one feature per PR; the staleness
  guards ([`RuleTests.cs:76`](../../../tests/Tests/Arch/Tests/RuleTests.cs),
  [`StructureTests.cs:64`](../../../tests/Tests/Structure/Tests/StructureTests.cs)) mean
  a half-migrated feature fails the build, so each feature migrates **atomically**.
- **Flows first** because it is the riskiest (SSE + catalog DI + 9→2). If its SSE
  port can't hold byte parity, that surfaces before Git/Tasks are touched.

## Risks / open questions

| Risk | Detail | Mitigation |
|---|---|---|
| **SSE framing in FastEndpoints** | `Send.EventStreamAsync` may not emit `data: {json}\n\n` with the same headers/flush cadence as [`Sse.Stream`](../../../src/Features/Flows/Watch/Sse.cs). | Spike the Watch port first; if FE's helper drifts, write `HttpContext.Response.Body` directly inside `HandleAsync` (port `Sse.Stream` near-verbatim). The existing Wire SSE test is the parity check. |
| **Route prefix loss** | FE has no `MapGroup`; each `/flows`, `/git/prs`, `/tasks` prefix must be re-typed per endpoint. A typo silently moves a route. | Wire-parity test pins every route+status; a moved route fails it. |
| **Query/optional binding** | `?project=` (Git) and ETag/`If-None-Match`→304 (Flows `Get`) must bind/behave identically under FE's binder. | Verify in the per-feature Wire test (304 path needs an explicit assertion — not currently covered). |
| **`CreatedAtAsync`/Location parity** | Projects uses `Send.CreatedAtAsync<GetProjectEndpoint>`; none of these three return 201 today (Start returns 200 `Ok`), so no `CreatedAt` is needed — confirm none is introduced by accident. | Keep Start's response `Send.OkAsync(StartRunResponse)`, not `CreatedAt`. |
| **Open question — extra Wire coverage** | The Flows SSE test exists, but `GET /flows/{id}` (304), `GET /catalog`, `POST /flows/{id}/cancel`, `GET /git/prs`, `POST .../merge`, `POST /tasks` have no Wire test today. | Each feature's parity PR should add the missing Wire assertions for the routes it ports, paired with a `### ` Rule via the `test-rulebook` skill. |
