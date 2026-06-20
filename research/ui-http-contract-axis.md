---
type: research
status: proposal
tags: [#architecture, #vsa, #contracts, #http-boundary, #detached-ui, #fastendpoints]
source: https://github.com/ardalis/RiverBooks/tree/main/src
related: [[riverbooks-module-sharing]]
---

# A second contract axis: serving a detached UI without exposing the module

> **Why this exists.** We have a separate-repo client (`abox-client`) that talks
> to this server over HTTP. RiverBooks — our modular-monolith reference — is
> **headless**, so it never had to answer "what does an external client bind
> to?" This doc records what RiverBooks actually does at its boundaries, proves
> the split we want already exists there (so we are not inventing a new
> duplication), and proposes the concrete change for our repo.

---

## 1. The finding: RiverBooks already has two strictly separate boundaries

There is no UI in RiverBooks. `RiverBooks.Web` is a pure API host (no `wwwroot`,
no Pages/Components, just `Program.cs` + middleware + a `.http` scratch file +
Swagger). Every cross-module call goes over MediatR in-process. So RiverBooks
runs **two contract axes**, and keeps them physically distinct:

| Axis | What crosses | Mechanism | Lives in |
|---|---|---|---|
| **Sideways** (module ↔ module) | `BookDetailsQuery` → `BookDetailsResponse`, `CreateOrderCommand` | MediatR message | `*.Contracts` (leaf assembly) |
| **Outward** (HTTP edge) | `GetByIdRequest` → `BookDto`, `CheckoutRequest` | FastEndpoints | endpoint folder, **module-internal** |

The route exists only on the outward axis, inside the endpoint's `Configure()`
(`Post("/cart/checkout")`), never in `*.Contracts`.

## 2. The proof we are not killing a use case

Two questions, both answered empirically against `ardalis/RiverBooks@main`:

**Q: Is any request object reused across endpoints?** No. Every endpoint has its
own dedicated request: `CreateBookRequest`, `GetByIdRequest`, `DeleteBookRequest`,
`UpdateBookPriceRequest`, `CreateUserRequest`, `UserLoginRequest`,
`AddAddressRequest`, `CheckoutRequest`. One request, one endpoint.

**Q: Is any type/endpoint used both internally and externally?** No. The "get a
book" read path is the clincher — Books exposes the same data **twice, as two
different types, via two different paths**:

```csharp
// EXTERNAL (HTTP):  Books/BookEndpoints/GetById.cs
internal class GetById : Endpoint<GetByIdRequest, BookDto>   // injects IBookService directly
//                                                            // no MediatR, no BookDetailsQuery

// INTERNAL (cross-module):  Books/Integrations/BookDetailsQueryHandler.cs
//   BookDetailsQuery (Contracts)  →  BookDetailsResponse (Contracts)
```

Corroborating paths:

- **Order creation** has *no* HTTP endpoint — reachable only internally via
  `CreateOrderCommand`. A `*.Contracts` command is never an HTTP request.
- **ListOrdersForUser** (HTTP) dispatches `ListOrdersForUserQuery` — an internal
  `UseCases` query, not a Contracts type — and returns an endpoint-folder
  response. HTTP-only, never crosses modules.

`BookDto` and `BookDetailsResponse` are near-identical and **two types on
purpose**: different consumers (browser vs. peer module), different trust
boundary, independent evolution. RiverBooks pays that duplication deliberately.

> **Caveat (cruft, not pattern):** `Endpoints/ListOrdersForUser.cs` carries a
> stray `using RiverBooks.Users.CartEndpoints;` and a duplicate `OrderSummary` —
> the half-built dead-import noted in `riverbooks-module-sharing.md` §1.5. Not a
> cross-axis reuse; do not model on it.

## 3. Where we differ from RiverBooks

We have a real detached client. So the outward axis cannot stay
**module-internal** the way RiverBooks leaves it — the client needs to bind
those request/response types. Today our wire DTOs already live in `*.Contracts`
(e.g. `CreateProjectRequest`, `ProjectDto`), which conflates the two axes that
RiverBooks keeps apart. The proposal separates them.

## 4. Proposal: name the outward axis, give it its own leaf

Adopt a **three-assembly** taxonomy per feature, splitting today's overloaded
`*.Contracts`:

```
contracts  →  cross-feature surface   (in-process message DTOs, sideways)   [leaf]
api        →  public HTTP surface     (request/response DTOs + route)        [leaf]
<feature>  →  the real business       (domain calls, endpoints w/ logic)   [internal]
```

Dependency DAG (acyclic; `api` and `contracts` are both leaves):

```
<feature> ──► api          (endpoint binds route from request, returns response DTO)
<feature> ──► contracts    (and peers' contracts)
api  ──► (nothing)         ◄── abox-client references ONLY this
contracts ──► (nothing)
abox-client ──► api        (never the feature assembly, never contracts)
```

Security property, enforced by the reference graph (not discipline): the client
references only `api` — flat DTOs with zero dependencies — so it is
**compile-time incapable of naming a domain type** (`Project`, `Agent`,
`Session` stay `internal` to their assemblies and unreferenced).

### 4.1 The route problem, and the fix

The route lives in the endpoint's `Configure()`, which stays with the logic —
so we cannot ship it to the client without shipping the logic. Rather than
source-gen a client or duplicate route strings, **put the route on the request
type** (which is already in `api`, since the client constructs it). The endpoint
reads it; the client reads it; single source of truth, no reflection:

```csharp
// in api
public interface IHttpRequest<TResponse>
{
    static abstract string Route { get; }
    static abstract string Method { get; }
}

public sealed record CreateProjectRequest(string Name, string Path)
    : IHttpRequest<ProjectDto>
{
    public static string Route  => "/projects";
    public static string Method => "POST";
}
```

```csharp
// base — endpoint keeps its logic; a small ApiEndpoint<,> base binds the route
internal sealed class AddProjectEndpoint : ApiEndpoint<CreateProjectRequest, ProjectDto>
{
    // Configure(): Verbs(TReq.Method); Routes(TReq.Route); + auth/claims as needed
    // HandleAsync(): the real business — never leaves this assembly
}

// abox-client — references only `api`
Task<TRes> Send<TReq, TRes>(TReq req) where TReq : IHttpRequest<TRes>
    => _http.SendJson(TReq.Method, TReq.Route, req);
```

Only the **route template + verb** move to the request. Auth, claims,
versioning, rate limits stay in `Configure()` in the feature assembly.

### 4.2 Things to decide / watch

- **Cross-repo DLL sharing = packaging.** `api` must be published to a NuGet
  feed for `abox-client` to consume; that also makes versioning the wire surface
  a first-class concern.
- **Route parameters (`/projects/{id}`).** The template lives on the request, but
  the client's generic `Send` must expand `{id}` from a request property — the
  one place the pattern needs a few extra lines.
- **`static abstract` vs `[Route]` attribute.** Static-abstract is compile-time
  and reflection-free (preferred); an attribute is more familiar but both sides
  must reflect.
- **Per-feature `api` vs one aggregate `ABox.PublicApi`.** Per-feature matches
  our existing Contracts grain; an optional meta-package eases client consumption.
- **Keep the axes apart.** Route-on-request is an `api`-only concept; never
  retrofit it onto the in-process `contracts` messages.

## 5. Bottom line

RiverBooks already separates the outward (HTTP) and sideways (in-process)
contract surfaces — it just leaves the outward one module-internal because it is
headless. Our detached client forces the outward surface into a referenceable
leaf. Splitting `contracts` from `api` therefore **formalizes a separation the
reference already maintains**, rather than inventing a new duplication; the
near-identical-looking DTOs across the two axes are deliberate, not waste. Open
question for ratification: whether this lands as an ADR amending 0009/0011, and
the packaging/versioning story for the `api` leaf.
