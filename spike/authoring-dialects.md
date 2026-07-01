# Authoring dialects — 4 patterns × 5 dialects

> **Active thread** under `README.md` → *Invariants*. This is the authoring-surface exploration
> that serves them; it supersedes `BUILDING-STYLE.md`'s authoring pass. Read the invariants first —
> the goal here is the *most regular, least ambiguous* wiring an agent can emit (minimal dialect),
> not a surface for humans to enjoy. The recipe is a tool; the generated output is the owned artifact.

Brainstorm / fictional style. Goal: find an authoring surface that stays terse as the
*pattern* changes — not just as the class names change. We take four RiverBooks features
that are structurally unlike each other and write each one five ways.

## The five dialects

| # | Name | Idea |
|---|---|---|
| **D1** | Signature injection | Dependencies are lambda parameters; the generator reads the parameter list. Most explicit. |
| **D2** | Ambient capability verbs | `Load / Ask / Save / Emit / Query / Insert` are the marker grammar; no scope object. |
| **D3** | Return-as-effect | Body is (mostly) pure; the value/event/entity you *return* is what the runtime performs. |
| **D4** | Per-wrapper sugar | The wrapper ships a one-liner for its motif (`.Mutates` / `.Projects` / `.Republishes` / `.Ingests`). |
| **D5** | Data-flow builder | Declare stages (`.Load().Ask().Do()`); the generator threads outputs → inputs. |

Wrapper family (the trigger picks the wrapper): `Endpoint` (HTTP), `Handler` (in-process
send), `OnEvent` (event), `Worker` (timer). Each wrapper knows its own motif.

---

## Pattern A — Aggregate mutation

**What it is.** HTTP command → load an aggregate by key → run a domain method on it → save.
Reads from a cross-module module mid-flow. Events are raised *inside* the aggregate, not here.

**Final code (RiverBooks `AddItemToCartHandler`):**
```csharp
public async ValueTask<Result> Handle(AddItemToCartCommand request, CancellationToken ct)
{
  var user = await _userRepository.GetUserWithCartByEmailAsync(request.EmailAddress);
  if (user is null) return Result.Unauthorized();

  var result = await _mediator.Send(new BookDetailsQuery(request.BookId));
  var bookDetails = result.Value;

  var newCartItem = new CartItem(request.BookId, request.Quantity, bookDetails.Price,
    $"{bookDetails.Title} by {bookDetails.Author}");
  user.AddItemToCart(newCartItem);

  await _userRepository.SaveChangesAsync();
  return Result.Success();
}
```

**Five dialects:**
```csharp
// D1 — signature injection
Endpoint((AddItem c, Repo<User> users, Ask<BookDetails> ask) => {
  var u = users.Get(c.Email);
  var b = ask(new BookDetailsQuery(c.BookId));
  u.AddItemToCart(new CartItem(c.BookId, c.Qty, b.Price, b.Label));
  users.Save();
});

// D2 — ambient verbs
Endpoint<AddItem>(c => {
  var u = Load<User>(c.Email);
  var b = Ask(new BookDetailsQuery(c.BookId));
  u.AddItemToCart(new CartItem(c.BookId, c.Qty, b.Price, b.Label));
  Save(u);
});

// D3 — return-as-effect (save + event-dispatch inferred from the returned dirty aggregate)
Endpoint<AddItem>(c =>
  from u in Load<User>(c.Email)
  from b in Ask(new BookDetailsQuery(c.BookId))
  select u.AddItemToCart(new CartItem(c.BookId, c.Qty, b.Price, b.Label)));

// D4 — per-wrapper sugar (.Mutates = load-by-key + save + dispatch)
Endpoint<AddItem>.Mutates<User>((u, c) =>
  u.AddItemToCart(new CartItem(c.BookId, c.Qty, Ask(new BookDetailsQuery(c.BookId)))));

// D5 — data-flow builder
Endpoint<AddItem>
  .Load<User>(c => c.Email)
  .Ask<BookDetails>(c => new BookDetailsQuery(c.BookId))
  .Do((u, b, c) => u.AddItemToCart(new CartItem(c.BookId, c.Qty, b.Price, b.Label)));
```

---

## Pattern B — Projection read

**What it is.** HTTP query → raw parametrized SQL against a read-only connection → map rows
→ return. No repository, no aggregate, no domain, no save, no emit. The "logic" is a query shape.

**Final code (RiverBooks `DefaultSalesReportService`, called by `TopSalesByMonth` endpoint):**
```csharp
public async Task<TopBooksByMonthReport> GetTopBooksByMonthReport(int month, int year)
{
  string sql = @"select BookId, Title, Author, UnitsSold as Units, TotalSales as Sales
                 from Reporting.MonthlyBookSales
                 where Month = @month and Year = @year
                 ORDER BY TotalSales DESC";
  using var conn = new SqlConnection(_connString);
  var results = (await conn.QueryAsync<BookSalesResult>(sql, new { month, year })).ToList();
  return new TopBooksByMonthReport { Year = year, Month = month, Results = results };
}
```

**Five dialects:**
```csharp
// D1 — signature injection (the dep is a read-store, not a Repo)
Endpoint((TopSalesByMonth q, Read db) =>
  db.Query<BookSalesResult>(Sql.TopBooks, new { q.Month, q.Year }));

// D2 — ambient verbs (a NEW verb surfaces: Query)
Endpoint<TopSalesByMonth>(q =>
  Query<BookSalesResult>(Sql.TopBooks, new { q.Month, q.Year }));

// D3 — return-as-effect: no effects to thread, so it COLLAPSES to D2 (a plain function)
Endpoint<TopSalesByMonth>(q =>
  Query<BookSalesResult>(Sql.TopBooks, new { q.Month, q.Year }));

// D4 — .Mutates is MEANINGLESS here (no aggregate, no save). Needs a different sugar:
Endpoint<TopSalesByMonth>.Projects<BookSalesResult>(q => (Sql.TopBooks, new { q.Month, q.Year }));

// D5 — one stage, so the builder is ceremony around a single Query
Endpoint<TopSalesByMonth>.Query<BookSalesResult>(q => (Sql.TopBooks, new { q.Month, q.Year }));
```

---

## Pattern C — Event reactor / bridge

**What it is.** A domain event fires → reshape its payload → publish an *integration* event for
other modules. No HTTP, no return, no repo, no save. The emit **is** the work (the domain→integration seam).

**Final code (RiverBooks `UserAddressIntegrationEventDispatcherHandler`):**
```csharp
public async ValueTask Handle(AddressAddedEvent notification, CancellationToken ct)
{
  Guid userId = Guid.Parse(notification.NewAddress.UserId);
  var addressDetails = new UserAddressDetails(userId,
    notification.NewAddress.Id,
    notification.NewAddress.StreetAddress.Street1,
    notification.NewAddress.StreetAddress.Street2,
    notification.NewAddress.StreetAddress.City,
    notification.NewAddress.StreetAddress.State,
    notification.NewAddress.StreetAddress.PostalCode,
    notification.NewAddress.StreetAddress.Country);

  await _mediator.Publish(new NewUserAddressAddedIntegrationEvent(addressDetails));
}
```

**Five dialects:**
```csharp
// D1 — signature injection (the dep is the publish capability)
OnEvent((AddressAdded e, Emit emit) =>
  emit(new NewUserAddressAdded(e.ToDetails())));

// D2 — ambient verbs (Emit)
OnEvent<AddressAdded>(e =>
  Emit(new NewUserAddressAdded(e.ToDetails())));

// D3 — return-as-effect: pure event → event. The runtime publishes the return. BEST showing.
OnEvent<AddressAdded>(e => new NewUserAddressAdded(e.ToDetails()));

// D4 — .Mutates meaningless again. Needs .Republishes:
OnEvent<AddressAdded>.Republishes(e => new NewUserAddressAdded(e.ToDetails()));

// D5 — ceremony around a one-line transform
OnEvent<AddressAdded>.Map(e => new NewUserAddressAdded(e.ToDetails())).Emit();
```

---

## Pattern D — Outbox / ingest write

**What it is.** In-process command → build a brand-new record → insert it into a *side store*
(Mongo outbox) → return the generated id. No aggregate to load, no domain, no emit; a create-and-stash.

**Final code (RiverBooks `SendEmailCommandHandler`):**
```csharp
public async ValueTask<Result<Guid>> Handle(SendEmailCommand request, CancellationToken ct)
{
  var id = Guid.NewGuid();
  var emailEntity = new EmailOutboxEntity {
    Id = id, To = request.To, From = request.From,
    Subject = request.Subject, Body = request.Body
  };
  await _emailEntityCollection.InsertOneAsync(emailEntity);
  return id;
}
```

**Five dialects:**
```csharp
// D1 — signature injection (the dep is a write-store collection)
Handler((SendEmail c, Store<EmailOutbox> outbox) => {
  var e = new EmailOutbox(NewId(), c.To, c.From, c.Subject, c.Body);
  outbox.Insert(e);
  return e.Id;
});

// D2 — ambient verbs (Insert)
Handler<SendEmail>(c => {
  var e = new EmailOutbox(NewId(), c.To, c.From, c.Subject, c.Body);
  Insert(e);
  return e.Id;
});

// D3 — STRAINS: the response (id) ≠ the persisted thing (entity), so return-as-effect is ambiguous
Handler<SendEmail>(c => Insert(new EmailOutbox(NewId(), c.To, c.From, c.Subject, c.Body)).Id);

// D4 — .Mutates meaningless. Needs .Ingests:
Handler<SendEmail>.Ingests<EmailOutbox>(c =>
  new EmailOutbox(NewId(), c.To, c.From, c.Subject, c.Body));   // inserts, returns Id

// D5 — single stage, ceremony
Handler<SendEmail>.New<EmailOutbox>(c => …).Insert().ReturnId();
```

---

## Scorecard — what scales when the *pattern* changes

| Pattern ↓ \ Dialect → | D1 sig | D2 verbs | D3 return | D4 sugar | D5 builder |
|---|---|---|---|---|---|
| **A** mutation | ✅ verbose | ✅ | ⚠️ save/emit inferred (magic) | ✅ `.Mutates` | ✅ multi-stage pays off |
| **B** projection | ✅ (+`Read`) | ✅ (+`Query`) | ✅ collapses to pure fn | ❌ → `.Projects` | ⚠️ single stage = ceremony |
| **C** reactor | ✅ (+`Emit`) | ✅ (+`Emit`) | ✅✅ pure event→event | ❌ → `.Republishes` | ⚠️ ceremony |
| **D** ingest | ✅ (+`Store`) | ✅ (+`Insert`) | ⚠️ response ≠ effect | ❌ → `.Ingests` | ⚠️ ceremony |

**Reading it:**

1. **D4 / D5 are motif-bound — they don't survive a pattern change.** `.Mutates` literally breaks
   on B/C/D; you must invent a new method per motif (`.Projects`, `.Republishes`, `.Ingests`). The
   builder only earns its staging when there are ≥2 stages (only A). These are *sugar*, not a universal dialect.
2. **D2 generalizes — but its verb set grows per pattern** (`Load/Save` → `Query` → `Emit` → `Insert`).
   Coherent: each pattern surfaces *its* verb.
3. **D3 is best at the pure edges, wobbliest in the stateful middle.** B collapses to a plain function;
   C becomes a gorgeous `event → event`. But A's save/emit inference is real magic, and D breaks when the
   response (`id`) differs from the persisted entity.
4. **D1 always works, always verbose** — the floor, never the daily driver.

## Conclusion

> **Status: exploration — the dialect is NOT chosen.** This section is a *menu*, not a verdict. No
> project code commits to any of it until we decide. Before it can become real it must reconcile with
> the **"minimal dialect — in the wiring"** invariant (`README.md`): D2 (the verb core) *is* that
> minimal wiring; D1/D3/D4 are sugar/escape *above* it. "No dialect choice" governs the **wiring
> layer**; whether the sugar tiers stay, collapse, or are dropped is the open question this doc leaves
> for that decision — it is not a license for an author-picks-altitude free-for-all.

Terseness can't be one universal dialect. The shape that scales is **layered**:

- **Shared verb core = D2** — `Load / Ask / Save / Emit / Query / Insert`. This *is* the marker
  grammar the generator keys on. Rarely written directly.
- **Per-wrapper sugar = D4** — each wrapper ships the one-liner for its own motif
  (`Endpoint.Mutates` / `Endpoint.Projects` / `OnEvent.Republishes` / `Handler.Ingests`). Motif-specific
  *on purpose* — the wrapper already knows its motif.
- **Return-as-effect = D3** — a runtime affordance; returning a value/event/entity gets it performed.
  This is what makes reads and bridges collapse to a single pure line for free.
- **Signature form = D1** — the escape hatch when a feature diverges from its wrapper's motif.

Daily experience: canonical feature = one sugared line; step outside the motif = drop to verbs;
fully bespoke = signature form. **Verbosity becomes proportional to divergence** — the property we've
wanted since the first "features don't all follow the same pattern" finding.
