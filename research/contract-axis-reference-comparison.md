---
type: research
status: comparison
tags: [#architecture, #vsa, #contracts, #http-boundary, #detached-ui, #reference-survey]
related: [[ui-http-contract-axis]], [[riverbooks-module-sharing]], [[scaffolding-and-reference-wiring]]
sources:
  - https://github.com/ardalis/RiverBooks
  - https://github.com/jasontaylordev/CleanArchitecture
  - https://github.com/ardalis/CleanArchitecture
  - https://github.com/ardalis/modulith
  - https://github.com/abpframework/abp
  - https://github.com/dotnet-architecture/eShopOnWeb
  - https://github.com/dotnet/eShop
  - https://github.com/jbogard/ContosoUniversityDotNetCore-Pages
  - https://github.com/nadirbad/VerticalSliceArchitecture
---

# The contract/api split, judged against the field

> **Why this exists.** The proposal in [[ui-http-contract-axis]] splits today's
> overloaded `*.Contracts` into two leaves — `contracts` (in-process, sideways)
> and `api` (outward HTTP, referenced by our detached `abox-client`) — and puts
> the **route+verb on the request type** so client and server share one source of
> truth without a source-generated client. It is grounded in **one** reference
> (RiverBooks), which is *headless* and so never had to answer "what does an
> external client bind to?" This doc pressure-tests the proposal against the wider
> field of VSA / Clean / modular-monolith references we have on file, to see which
> of its bets are well-trodden and which are genuinely novel.

---

## 1. The proposal as three testable bets

Strip the proposal to what an external reference can confirm or refute:

- **BET-A — Two contract axes, physically split.** A `contracts` leaf for
  in-process peer messaging *and* a separate `api` leaf for the outward HTTP wire.
- **BET-B — The outward axis is a referenceable leaf** a detached client binds to
  directly (flat DTOs, zero deps) — not an OpenAPI spec, not a module-internal type.
- **BET-C — Route+verb live on the request type**, read by both the endpoint and a
  generic client `Send<TReq,TRes>` — no codegen, no duplicated route strings.

A fourth element — the **domain→DTO firewall** (rich entity stays internal; flat
DTO is a mapped projection) — is not really a bet: it is universal (§4.4), so the
survey treats it as table stakes, not a differentiator.

Our **current** shape (for reference): each feature has one `*.Contracts` leaf
holding *both* request and DTO (`CreateProjectRequest` + `ProjectDto`), and the
route lives in the endpoint's `Configure()` (`Post("/projects")`). That is
**exactly RiverBooks** — route in the endpoint, DTOs in a contracts leaf. The
proposal is a deliberate divergence *from* the baseline, so the field matters.

---

## 2. The scorecard

| Reference | BET-A two axes | BET-B outward leaf for a detached client | BET-C route on request | firewall |
|---|---|---|---|---|
| **RiverBooks** (baseline) | ✅ two axes — but outward stays **module-internal** (headless) | ❌ | ❌ `Configure()` | ✅ |
| **eShopOnWeb** | ❌ one shared model set | ✅ **`BlazorShared`** → Blazor-WASM client | ❌ inline URL strings in the client service | ✅ |
| **ABP** | ⚠️ **collapses** both into one shared **interface** | ✅ `*.Application.Contracts` (interface, not flat data) | ❌ convention (verb from method-name prefix) | ✅ |
| **Ardalis Modulith** | ⚠️ `*.Contracts` (in-proc, empty by default) + optional Blazor `*.HttpModels` | ⚠️ optional `*.HttpModels` (Blazor solutions only) | ❌ `Configure()` | ✅ |
| **Ardalis CleanArchitecture** | ❌ DTOs sit in the Web project | ❌ headless (Swagger/Scalar) | ⚠️ **`const string Route` on the request** (but bound in `Configure()`, not shipped) | ✅ |
| **Jason Taylor CleanArchitecture** | ❌ | ❌ **OpenAPI-generated** client | ❌ endpoint registration | ✅ |
| **eShop** (.NET Aspire) | ❌ | ❌ typed `HttpClient` + local DTOs / gRPC-gen | ❌ | ✅ |
| **Contoso / nadirbad VSA** | ❌ inline per-feature | ❌ no detached client | ❌ | ✅ |

Read top-to-bottom: **no single reference does all three.** The proposal is a
*recombination* — each bet has prior art somewhere, but the combination is its own.

---

## 3. The client-binding spectrum (the real axis everyone differs on)

BET-B and BET-C are two questions about one thing: *how does a detached .NET
client turn a method call into an HTTP request?* The field spreads across five
mechanisms — the proposal is a sixth point, adjacent to (4):

| # | Mechanism | What the client references | Route lives | Reference |
|---|---|---|---|---|
| 1 | **OpenAPI / NSwag generated client** | a spec-derived, parallel client | OpenAPI doc | Jason Taylor CA |
| 2 | **gRPC proto-generated stub** | `.proto`-generated client | proto service def | eShop (basket) |
| 3 | **Shared C# interface + proxy** | the *same* `IAppService` the server implements | convention / attrs | ABP |
| 4 | **Shared DTO leaf + hand-written typed `HttpClient`** | flat DTO assembly; routes are inline strings at the call site | call-site string literals | **eShopOnWeb**, eShop (catalog), Modulith `HttpModels` |
| 5 | **Inline per-feature request/response** (no client) | nothing — single app | endpoint / Razor Page | Contoso, nadirbad VSA |
| **6** | **Shared DTO leaf + route-on-request + generic `Send`** | flat DTO assembly; route is a static member of the DTO | **on the request type** | **THE PROPOSAL** |

The proposal is **(4) with the route lifted off the call site onto the DTO.**
That single move is its whole novelty: eShopOnWeb already ships a referenceable
DTO leaf (`BlazorShared`) to a real detached WASM client — but every route is an
inline string in `CatalogItemService` (`"catalog-items"`, `$"catalog-items/{id}"`).
The proposal removes exactly that duplication. Nobody in the field has done it.

---

## 4. Axis-by-axis read

### 4.1 BET-A — two contract axes (well-precedented, but check we *need* the second)

The split itself is real and reference-backed:

- **RiverBooks** runs both axes and keeps them physically apart — sideways =
  MediatR messages in `*.Contracts`; outward = FastEndpoints request/response in
  the endpoint folder. It just leaves the outward one module-internal because it
  is headless (`riverbooks-module-sharing.md` §2; `ui-http-contract-axis.md` §1).
- **Modulith** is the cleanest endorsement of *naming* both: a per-module
  `*.Contracts` leaf for in-process inter-module messages (internal module types,
  ArchUnit-enforced) **plus** an *optional* `*.HttpModels` leaf for the outward
  Blazor wire — two leaves, two axes, by name.

**The catch the field exposes:** the second axis only earns its keep when there is
real sideways traffic. RiverBooks and Modulith are **modular monoliths** — modules
call each other in-process, so `contracts` carries weight. **We are not there yet:**
today every `*.Contracts` type is an *outward* HTTP DTO consumed by the detached
client; there is no cross-feature in-process message in the tree. Per CLAUDE.md
YAGNI ("add the abstraction on the *second* real use"), the `api` leaf is the half
of BET-A we need **now**; the `contracts` (sideways) leaf is provisioning for a
peer-messaging axis that does not yet exist. Recommendation: land `api` first;
introduce `contracts` when the first genuine sideways message appears, not before.
(This also matches `riverbooks-module-sharing.md` §3: most "leans on" edges resolve
*down* to Domain/Runtime, not *sideways* — so the sideways axis may stay thin.)

### 4.2 BET-B — an outward referenceable leaf (strong prior art: eShopOnWeb)

This is the proposal's best-supported bet. **eShopOnWeb is the near-exact
precedent**: `BlazorShared/Models/` holds `CreateCatalogItemRequest`/`Response`
etc., referenced by both the `PublicApi` server and the `BlazorAdmin` WASM client —
a flat DTO leaf across a real network boundary, with its own duplicated
`CatalogItem` model distinct from the `ApplicationCore` entity. **ABP** reaches the
same goal differently — the shared leaf is `*.Application.Contracts`, but it ships
*interfaces* (`IAppService`) as well as DTOs (§5). **Modulith's** optional
`*.HttpModels` is the same idea, smaller. The leaf-for-the-client is mainstream;
the proposal is on safe ground here.

What the proposal must not lose from the field: **eShopOnWeb still duplicates the
domain model into the wire leaf on purpose** — the firewall holds across the
network boundary exactly as `ui-http-contract-axis.md` §3 demands.

### 4.3 BET-C — route on the request type (genuinely novel; weak prior art)

This is the proposal's exposed flank. **No reference ships the route to the client
on the request type.** The two closest are partial and instructive:

- **Ardalis CleanArchitecture** puts `public const string Route = "/Contributors"`
  *on the request* — but binds it server-side via `Post(CreateContributorRequest.Route)`
  in `Configure()`, and is headless, so the const never travels to a client. It
  proves route-on-request *compiles and reads cleanly server-side* — a real datapoint
  that the ergonomic half of BET-C is fine.
- **ABP** derives the route by **convention** (verb from method-name prefix, path
  from service name) — the opposite philosophy: no explicit route anywhere, magic at
  both ends.

So BET-C's server-side shape is lightly precedented (Ardalis CA), but the
client-also-reads-it half is unique to the proposal. The field's actual answers to
"how does the client know the route" are all *other* mechanisms (§3): generate it
(OpenAPI/gRPC), proxy it (ABP), or hardcode it at the call site (eShopOnWeb). The
proposal's bet is that lifting the string onto the DTO beats all three for a
hand-rolled client. Plausible, but **carry the open risks from `ui-http-contract-axis`
§4.2 as first-class**: `{id}` template expansion in the generic `Send`, and
`static abstract` interface members — neither is exercised by any reference, so they
are ours to validate in a spike, not borrow.

### 4.4 The firewall — table stakes, not a differentiator

Every reference keeps entities out of the wire and maps entity→DTO: JT CA and ABP
via AutoMapper/ObjectMapper, RiverBooks via hand-written handler projection,
eShopOnWeb via a duplicated `BlazorShared` model, Modulith/Ardalis CA via distinct
endpoint records. ABP states it as a rule ("do not reference entities" from
Contracts). The proposal's "you write the `Map`, that cost **is** the firewall"
(`ui-http-contract-axis.md` §6) is the consensus position — uncontroversial.

---

## 5. The strongest counter-design the field offers: ABP's shared interface + proxy

The proposal frames its alternatives as "source-gen a client" (rejected) vs
"route-on-request" (chosen). The field supplies a **third** design it does not
weigh, and it is the most serious competitor: **ABP shares a C# *interface*
(`IAppService`) and hides HTTP behind a proxy** — *dynamic* (built at runtime by
reflecting the interface; no codegen) or *static* (codegen). The client calls a
normal, domain-named method (`bookService.GetAsync(id)`); the proxy maps it to the
HTTP call. Crucially the **dynamic** variant is *not* the source-gen path the
proposal rejects — it has no generated client at all.

| | **ABP** (shared interface + proxy) | **Proposal** (shared DTO + route-on-request) |
|---|---|---|
| Unit of sharing | a service **interface** | a flat **request DTO** |
| Client call site | `service.GetAsync(id)` (domain-named) | `Send(new GetXRequest(id))` (transport-shaped) |
| Route | convention / attribute, **hidden** | explicit, **on the DTO**, inspectable |
| Hidden machinery | a reflection/codegen proxy you must trust | a one-line generic `Send`, nothing hidden |
| Optimizes for | "make a remote call feel local across many services" | "one HTTP surface as self-describing, generation-free data" |

The honest trade: ABP buys nicer client ergonomics (interface-shaped calls) at the
cost of a proxy layer and convention-magic routing split across both ends; the
proposal buys an explicit, inspectable, zero-magic wire at the cost of a
transport-shaped `Send(request)` call site. For an **agent-first** repo whose whole
thesis is *explicit and inspectable over convention* (`architecture-vsa.md` §7), the
proposal's bias is the consistent one — but the doc should **name ABP's dynamic
proxy and say why we don't take it**, rather than leaving "source-gen" as the only
foil. (Right now it under-sells its own case by arguing against the weaker
alternative.)

---

## 6. Findings → what to change in the proposal

1. **Keep BET-B (the `api` leaf) — it is the well-precedented, needed-now half.**
   eShopOnWeb's `BlazorShared` is direct prior art; cite it alongside RiverBooks so
   the proposal isn't resting on a single headless reference.
2. **Defer the `contracts` (sideways) half of BET-A until a real peer message
   exists.** Today's `*.Contracts` is 100% outward; a second leaf for in-process
   messaging is YAGNI until the first sideways case lands (and per
   `riverbooks-module-sharing.md` §3, most edges resolve *down*, not sideways, so it
   may stay small). Rename today's leaf to `api`; let `contracts` arrive on use #2.
3. **Flag BET-C (route-on-request) as the one unprecedented move and spike it.**
   The field validates the *server-side* const (Ardalis CA) and the *shared-DTO-leaf*
   (eShopOnWeb) independently, but never the union "client reads the route off the
   shipped DTO." Prove `{id}` expansion + `static abstract` in a throwaway before
   ratifying — these are ours, borrowed from no one.
4. **Add ABP's dynamic proxy to "alternatives considered."** It is the real
   counter-design, and "we prefer explicit data over a hidden proxy, consistent with
   agent-first §7" is a stronger justification than rejecting OpenAPI codegen alone.
5. **Firewall: no change.** It is consensus; keep the "you write the `Map`" framing.

---

## 7. Bottom line

The proposal is a **recombination, not an invention**: a referenceable outward DTO
leaf (eShopOnWeb-grade prior art) + the route lifted from the call site onto that
DTO (no prior art). The two-axis framing it inherits from RiverBooks is sound but
**over-provisioned for where we are** — we have a detached client (need the outward
leaf) but no in-process peers yet (don't need the sideways leaf). The single
genuinely new bet, route-on-request, is small, plausible, and unbacked by the
field; treat it as a spike, not a settled pattern, and weigh it explicitly against
ABP's shared-interface proxy — the one alternative design the references prove out
that the proposal currently leaves unaddressed.
