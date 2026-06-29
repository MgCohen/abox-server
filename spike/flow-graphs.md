# Flow Graphs — architecture-first extraction (RiverBooks)

> **Why this doc.** A different angle on the rebuild: instead of starting from
> templates/recipes, start from **architecture**. Take a real, well-structured
> reference codebase, draw the *logic flows* of a handful of feature slices the way
> we'd sketch them on a whiteboard — services calling services, things passing
> through containers, reactions and dispatches — and only *then* look for the
> patterns that repeat. Grounding against our own tech comes later, deliberately.
>
> **Reference:** [`ardalis/RiverBooks`](https://github.com/ardalis/RiverBooks) — a
> .NET modular monolith (vertical slices, FastEndpoints, MediatR, EF Core, a Mongo
> email outbox, a Redis address cache, a Dapper reporting read-model). Modules:
> **Books, Users, OrderProcessing, EmailSending, Reporting**, glued by a
> `SharedKernel` and per-module `*.Contracts` assemblies.
>
> **Status:** Iteration 1 = raw flows + summary. Iteration 2 = normalization pass
> (appended below). All file:line refs are into the cloned RiverBooks tree, not ours.

## Legend (edge vocabulary)

| Label | Meaning |
|---|---|
| `HTTP` | inbound request / outbound response |
| `call` | direct in-process method call (DI-resolved collaborator) |
| `send` | MediatR **request/response** — 1:1, returns a `Result<T>` |
| `publish` | MediatR **notification** — 1:N, fire-to-all-subscribers |
| `validate` | FluentValidation runs before the handler |
| `query` / `save` | EF Core read / `SaveChangesAsync` |
| `raise` | aggregate registers a domain event on itself |
| `dispatch` | DbContext dispatches collected domain events **after** save |
| `bridge→IE` | an in-module domain-event handler republishes as an **integration event** |
| `write`/`read` | non-EF store I/O (Mongo, Redis) |
| `poll` | background service timer tick |
| `smtp` | external SMTP send |

Node shape convention: `([actor])` external/client · `[component]` code · `[(store)]`
persistence · `{{event}}` message/event.

---

# Iteration 1 — the flows

## Summary

| # | Flow | Trigger | Modules | Shape in one line |
|---|------|---------|---------|-------------------|
| 1 | List Books | `GET /books` | Books | endpoint → service → repo → DB → DTO (no MediatR) |
| 2 | Create Book | `POST /books` | Books | validate → endpoint → service → aggregate(guards) → repo → save (no MediatR) |
| 3 | Add Item to Cart | `POST /cart` | Users → **Books** | handler **sends** a cross-module *query* to enrich, then mutates its aggregate |
| 4 | Checkout Cart | `POST /cart/checkout` | Users → **OrderProcessing**, **EmailSending** | handler **sends** cross-module *commands* (create order, send email), then clears cart |
| 5 | Add Address → cache | `POST /users/addresses` | Users → **OrderProcessing** | aggregate raises domain event → bridged to integration event → other module updates a cache |
| 6 | Order created → fan-out | order saved | OrderProcessing → **Reporting**, **EmailSending**, **Books** | one domain event → many subscribers; one bridged to integration event; email via async outbox |

Two things already jump out and are worth holding onto for Iteration 2:
- **Books doesn't use MediatR at all** (direct `Service → Repository`), while Users /
  OrderProcessing route everything through MediatR commands/queries. Same repo, two
  styles — an un-normalized seam.
- **Email gets emitted two different ways** — directly from the Checkout handler
  (flow 4, with a code TODO admitting it *should* be an event) and from an
  OrderCreated domain-event handler (flow 6). Two paths to the same side-effect.

---

## 1. List Books — the read baseline

```mermaid
flowchart LR
  C([Client]) -->|"HTTP GET /books"| EP["List endpoint<br/>(FastEndpoints)"]
  EP -->|call| SVC[BookService]
  SVC -->|call| REPO["EfBookRepository"]
  REPO -->|query| DB[(Books DB)]
  DB -.->|"Book[]"| REPO
  REPO -.-> SVC
  SVC -.->|"map → BookDto[]"| EP
  EP -->|"HTTP 200 JSON"| C
```

**Reading.** The plain read spine. No command/query object, no MediatR — the
endpoint holds an `IBookService` directly. Mapping to DTO happens in the service.
This is the *minimal* pipeline every other flow is an elaboration of.
`BookEndpoints/List.cs`, `BookService.cs:52`, `EfBookRepository.cs:31`.

## 2. Create Book — the write baseline

```mermaid
flowchart LR
  C([Client]) -->|"HTTP POST /books"| EP["Create endpoint"]
  EP -->|validate| V["CreateBookRequestValidator<br/>(FluentValidation)"]
  V -.->|ok| EP
  EP -->|call| SVC[BookService]
  SVC -->|"new Book(...)"| AGG["Book aggregate<br/>(Guard clauses)"]
  AGG -.->|valid| SVC
  SVC -->|call add| REPO["EfBookRepository"]
  SVC -->|save| REPO
  REPO -->|save| DB[(Books DB)]
  EP -->|"HTTP 201 + Location"| C
```

**Reading.** Write baseline. Two validation layers: **request** validation
(FluentValidation, outside the aggregate) and **invariant** validation (Guard
clauses *inside* the `Book` constructor). Still no MediatR. `BookEndpoints/Create.cs`,
`Create.CreateBookRequestValidator.cs`, `Book.cs:12`.

## 3. Add Item to Cart — cross-module *query* to enrich

```mermaid
flowchart LR
  C([Client]) -->|"HTTP POST /cart"| EP["AddItem endpoint"]
  EP -->|"send (cmd)"| H["AddItemToCartHandler"]
  H -.- V["AddItemToCartCommandValidator"]
  subgraph Users
    H -->|"query user (EF)"| UREPO["EfApplicationUserRepository"]
    UREPO --> H
    H -->|"call AddItemToCart"| UAGG["ApplicationUser aggregate"]
    H -->|save| UREPO
  end
  H ==>|"send BookDetailsQuery<br/>(Books.Contracts)"| BH
  subgraph Books
    BH["BookDetailsQueryHandler"] -->|call| BSVC[BookService] -->|query| BDB[(Books DB)]
  end
  BH ==>|"Result&lt;BookDetailsResponse&gt;"| H
  EP -->|"HTTP 200"| C
```

**Reading.** First cross-module hop (bold edges). Users needs book price/title to
build a `CartItem`, but has **no reference to Books** — it `send`s a
`BookDetailsQuery` *defined in `Books.Contracts`*, and MediatR routes it to the
handler living in Books. A cross-module **read**: pull data, then keep working.
`AddItemToCartHandler.cs:30`, `Books.Contracts/BookDetailsQuery.cs`, `Books/Integrations/BookDetailsQueryHandler.cs`.

## 4. Checkout Cart — cross-module *commands* (orchestration)

```mermaid
flowchart TB
  C([Client]) -->|"HTTP POST /cart/checkout"| EP["Checkout endpoint"]
  EP -->|"send (cmd)"| H["CheckoutCartHandler"]
  subgraph Users
    H -->|"query user+cart"| UREPO["EfApplicationUserRepository"]
    H -->|"call ClearCart"| UAGG["ApplicationUser"]
    H -->|save| UREPO
  end
  H ==>|"send CreateOrderCommand<br/>(OrderProcessing.Contracts)"| OH
  subgraph OrderProcessing
    OH["CreateOrderCommandHandler"] -->|"call cache"| CACHE[("Address cache (Redis)")]
    OH -->|"Order.Factory.Create"| OAGG["Order aggregate<br/>raises OrderCreatedEvent"]
    OH -->|save| OREPO["EfOrderRepository"]
  end
  OH ==>|"Result&lt;OrderId&gt;"| H
  H ==>|"send SendEmailCommand<br/>(EmailSending.Contracts)"| EM["EmailSending<br/>(see flow 6 outbox)"]
  EP -->|"HTTP 200 OrderId"| C
```

**Reading.** The orchestrator. Two cross-module **commands** (cause effects, vs
flow 3's query): `CreateOrderCommand` into OrderProcessing, `SendEmailCommand` into
EmailSending. Note the ordering invariant — cart is cleared *only after* the order
succeeds. The created `Order` **raises a domain event** (`OrderCreatedEvent`) whose
consequences are flow 6. Code TODO here: the inline `SendEmailCommand` "should move
to an event handler" — i.e. this direct send is a known wart.
`CheckoutCartHandler.cs`, `OrderProcessing.Contracts/CreateOrderCommand.cs`, `OrderProcessing/Integrations/CreateOrderCommandHandler.cs`.

## 5. Add Address → address-cache replication (event-driven)

```mermaid
flowchart TB
  C([Client]) -->|"HTTP POST /users/addresses"| EP["AddAddress endpoint"]
  EP -->|"send (cmd)"| H["AddAddressToUserHandler"]
  subgraph Users
    H -->|"call AddAddress"| AGG["ApplicationUser"]
    AGG -->|raise| DE{{"AddressAddedEvent<br/>(domain event)"}}
    H -->|save| REPO["EfApplicationUserRepository"]
    REPO -->|"SaveChangesAsync"| DBC["UsersDbContext"]
    DBC -->|"dispatch (post-save)"| DISP["MediatRDomainEventDispatcher"]
    DISP -->|publish| DE
    DE -->|handled by| LOG["LogNewAddressesHandler<br/>(in-module)"]
    DE -->|handled by| BR["UserAddressIntegrationEventDispatcherHandler<br/>(bridge)"]
    BR -->|"bridge→IE"| IE{{"NewUserAddressAddedIntegrationEvent<br/>(Users.Contracts)"}}
  end
  IE ==>|publish| OH
  subgraph OrderProcessing
    OH["AddressCacheUpdatingNewUserAddressHandler"] -->|"write"| RC[("Redis address cache")]
  end
```

**Reading.** The decoupling pattern. The aggregate **raises** a domain event onto
itself; `UsersDbContext.SaveChangesAsync` **dispatches** it *after* the DB commit
(scanning `ChangeTracker` for `IHaveDomainEvents`). One domain event fans to two
in-module handlers; one of them is a **bridge** that republishes a *Contracts*
integration event, which OrderProcessing consumes to keep a **denormalized Redis
cache** of addresses it needs at order time (so flow 4 can read it synchronously).
Domain event = in-module; integration event = cross-module; the bridge is the seam.
`ApplicationUser.cs:42`, `UsersDbContext.cs:42`, `SharedKernel/MediatRDomainEventDispatcher.cs`, `Users/Integrations/UserAddressIntegrationEventDispatcherHandler.cs`, `OrderProcessing/Integrations/AddressCacheUpdatingNewUserAddressHandler.cs`.

## 6. Order created → fan-out (the richest flow)

```mermaid
flowchart TB
  START["CreateOrderCommandHandler<br/>(from flow 4)"] -->|save| DBC["OrderProcessingDbContext"]
  OAGG["Order aggregate"] -->|raise| DE{{"OrderCreatedEvent<br/>(domain event)"}}
  DBC -->|"dispatch (post-save)"| DISP["MediatRDomainEventDispatcher"]
  DISP -->|publish| DE
  DE -->|handled by| H1["SendConfirmationEmailOrderCreatedEventHandler<br/>(in-module)"]
  DE -->|handled by| H2["PublishCreatedOrderIntegrationEventHandler<br/>(bridge)"]
  H2 -->|"bridge→IE"| IE{{"OrderCreatedIntegrationEvent<br/>(OrderProcessing.Contracts)"}}

  H1 ==>|"send SendEmailCommand"| SEH
  subgraph EmailSending
    SEH["SendEmailCommandHandler"] -->|"write"| OUT[("Mongo email outbox")]
    BG["EmailSendingBackgroundService"] -->|"poll 30s"| PROC["MongoDbEmailOutboxProcessor"]
    PROC -->|read| OUT
    PROC -->|smtp| SMTP([SMTP server])
    PROC -->|"write processed"| OUT
  end

  IE ==>|publish| RH
  subgraph Reporting
    RH["NewOrderCreatedIngestionHandler"] ==>|"send BookDetailsQuery"| BKS([Books module])
    RH -->|call| OIS["OrderIngestionService"]
    OIS -->|"upsert (Dapper)"| RDB[(MonthlyBookSales)]
  end
```

**Reading.** One `raise` → many reactions. The domain event has **two in-module
subscribers**: one queues a confirmation email (cross-module `send` into
EmailSending), one **bridges** to an integration event consumed by **Reporting**.
Two distinct async/decoupling devices appear:
- **Outbox** (EmailSending): the command only *writes a row* to Mongo; a
  `BackgroundService` polls every 30s and does the actual SMTP send, then marks the
  row processed — the HTTP request never waits on email, and a crash just retries.
- **Read-model ingestion** (Reporting): the integration handler enriches via a
  cross-module `BookDetailsQuery` (back into Books) and Dapper-upserts a
  denormalized `MonthlyBookSales` table — its own query-optimized store.

`Order.cs`, `OrderProcessingDbContext.cs:53`, `PublishCreatedOrderIntegrationEventHandler.cs`, `Reporting/Integrations/NewOrderCreatedIngestionHandler.cs`, `EmailSending/SendQueuedEmail/*`.

---

# Iteration 2 — normalization pass

> Reading the six graphs side by side, the **same handful of node kinds and edge
> kinds** recur, and they assemble out of a small set of repeating **motifs**. This
> pass names them. Still no grounding on our tech — this is purely "what shapes does
> a well-built feature decompose into."

## 2.1 Normalized node kinds

Every box across all six flows collapses to one of these roles:

| Kind | What it is | Seen as |
|---|---|---|
| **Edge / Endpoint** | the inbound boundary (HTTP today) | `List`, `Create`, `AddItem`, `Checkout`, `AddAddress` |
| **Message** | a typed request the system acts on — *command* (effect) or *query* (data) | `AddItemToCartCommand`, `BookDetailsQuery`, `CreateOrderCommand` |
| **Gate** | a pre-condition check on a message | `*Validator` (request), Guard clauses (invariant) |
| **Handler** | the unit of work for one message; orchestrates, returns a `Result` | every `*Handler` |
| **Service** | stateless behavior a handler/endpoint calls directly | `BookService`, `OrderIngestionService` |
| **Aggregate** | the consistency-owning domain object; exposes intent methods, holds invariants, **emits events** | `Book`, `ApplicationUser`, `Order` |
| **Port + Adapter** | an interface (`I*Repository`, `IOrderAddressCache`, `ISendEmail`) and its impl (`Ef*`, `Redis*`, `MimeKit*`) | repos, caches, senders |
| **Store** | where state lives | SQL per module, Mongo outbox, Redis cache, Dapper read-model |
| **Event** | something that *happened* — **domain** (in-module) or **integration** (cross-module, lives in `*.Contracts`) | `OrderCreatedEvent` / `OrderCreatedIntegrationEvent` |
| **Reactor** | a handler subscribed to an event (incl. the **bridge** reactor that re-emits) | `*EventHandler`, the two bridge handlers |
| **Worker** | a timer-driven background processor | `EmailSendingBackgroundService` |
| **External** | outside the process | SMTP server |

## 2.2 Normalized edge kinds — and the one distinction that matters most

Collapse the legend further and there are really **two transport families**, and
the whole architecture's character comes from where each is used:

| Family | Edges | Cardinality | Coupling | "Who knows whom" |
|---|---|---|---|---|
| **Directed** (ask) | `call`, `send`, `query`/`save` | 1→1 | caller names the message/port | imperative: *do this and tell me the result* |
| **Broadcast** (announce) | `raise` → `dispatch` → `publish` | 1→N | emitter knows nothing of reactors | reactive: *this happened; whoever cares, react* |

The seam between them is the recurring architectural decision. **Inside a slice**
everything is *directed*. **Across slices** there are exactly two sanctioned doors:
a *directed* `send` of a `*.Contracts` message (flows 3, 4 — synchronous, you want
an answer or an ordered effect), or a *broadcast* integration event (flows 5, 6 —
fire-and-forget, the emitter must not wait or care).

## 2.3 The recurring motifs (the reusable shapes)

Six flows, seven motifs. Every flow is a composition of these:

| Motif | Shape | Appears in |
|---|---|---|
| **M1 · Request pipeline** | `Endpoint → [Gate] → Message → Handler → Result → Endpoint` | all |
| **M2 · Aggregate mutation** | `Handler → (load via Port) → Aggregate.intent() [guards (+raise)] → save` | 2,3,4,5,6 |
| **M3 · Cross-module ask** | `Handler → send(Contracts msg) → foreign Handler → Result` | 3 (query), 4 (command) |
| **M4 · Post-save dispatch** | `Aggregate.raise → DbContext.save → dispatch → publish → Reactor*` | 5,6 |
| **M5 · Domain→Integration bridge** | `Reactor → bridge→IE → foreign Reactor*` | 5,6 |
| **M6 · Async outbox** | `Handler → write(outbox); Worker → poll → read → External → mark` | 6 |
| **M7 · Read-model / cache replication** | `Reactor → upsert(own denormalized Store)` | 5 (Redis), 6 (Dapper) |

Notice the **layering**: M4 feeds M5 feeds (M6 ∥ M7). The right edge of one motif is
the left edge of the next — they chain at typed seams (a `Result`, an event, a
`*.Contracts` type). That chaining-at-seams is the thing to carry forward.

## 2.4 The canonical composite

Collapsing all six onto the normalized vocabulary, **one feature** looks like this —
M1 is the spine; M3 branches sideways (sync); M4→M5→{M6,M7} hangs off the bottom
(async):

```mermaid
flowchart TB
  EDGE([Edge / Endpoint]) -->|"HTTP"| MSG["Message (command/query)"]
  MSG -->|validate| GATE[Gate]
  GATE --> HANDLER[Handler]

  HANDLER -->|"M3: send (Contracts)"| FOREIGN["Foreign Handler<br/>(other module)"]
  FOREIGN -.->|Result| HANDLER

  HANDLER -->|"M2: load / save"| PORT["Port → Adapter → Store"]
  HANDLER -->|"intent()"| AGG["Aggregate<br/>(guards + raise)"]
  AGG -.-> PORT

  PORT -->|"M4: save then dispatch"| EV{{"Domain event"}}
  EV -->|publish 1→N| RX["Reactor(s)"]
  RX -->|"M5: bridge→IE"| IE{{"Integration event (Contracts)"}}
  IE -->|publish 1→N| FRX["Foreign reactor(s)"]

  FRX -->|"M7: upsert"| RM[("Read-model / cache")]
  FRX -->|"M6: enqueue"| OB[("Outbox")]
  WK["Worker (poll)"] --> OB
  WK -->|external| EXT([External system])

  HANDLER -->|"Result"| EDGE
```

## 2.5 What's *not* normalized (the signal)

Where the reference itself is inconsistent is exactly where a normalizing system
earns its keep:

1. **Two pipeline dialects.** Books = `Endpoint → Service → Repo` (no message bus);
   Users/OrderProcessing = `Endpoint → Message → Handler`. Same M1 intent, two
   spellings. A normalized model would pick one (or treat "Service" as a degenerate
   handler).
2. **Two emit-paths for one side-effect.** Email is triggered both by a direct
   `send` from Checkout (flow 4) *and* by an OrderCreated reactor (flow 6) — the
   code even TODOs the first toward the second. The normalized rule is latent:
   *side-effects of a state change belong on M4 (the event), not inline in the
   originating handler.*
3. **Gate placement varies.** Request validation (FluentValidation) vs invariant
   validation (Guard clauses in the ctor) are both "Gate" but live at different
   altitudes. Worth a single model with two tiers rather than two mechanisms.
4. **Cross-module door choice is by convention, not by type.** "Use `send` for a
   needed answer, an integration event for fire-and-forget" is a rule in people's
   heads — nothing structural enforces which door a given interaction should take.

## 2.6 Carry-forward (for the later grounding pass — not done here)

- The unit of reuse is the **motif** (M1–M7), not the file. A feature is *a
  composition of motifs chained at typed seams*.
- The two transport families (§2.2) and the two cross-module doors (§2.3/M3,M5) are
  the **load-bearing decisions** — any architecture model we build should make those
  choices explicit and, ideally, type-enforced rather than conventional.
- The inconsistencies in §2.5 are candidate **invariants to enforce**, not bugs to
  copy.

> Next pass (separate): hold these motifs against our own composition/template model
> and see which fall out for free, which need a new mechanism, and which of the §2.5
> inconsistencies our type system could make unrepresentable.
