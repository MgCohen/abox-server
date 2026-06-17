# Inbox · Decision · Notification — design plan

> **Status:** Exploration / **not** part of the locked rebuild. This is the
> **standalone design** that [`the-box.md`](the-box.md) §16 defers to ("the full
> design of the parallel Inbox/Decision system … gets its own doc, authored as
> the first task of the S1 track"). It is the **how/what** for build **S1** in
> [`PLANS/the-box-implementation.md`](../PLANS/the-box-implementation.md). The Box
> is **one producer**, not the owner — nothing here is Box-specific. **Cold-readable**
> (assumes no prior context). Where this doc and `the-box.md` §2.2/§5 differ on the
> Inbox/Decision/Notification *model*, **this doc governs**; `the-box.md` keeps only
> the Box-facing seam.

## 1. One-liner

Three small, independent systems that meet at adapters:

- **Inbox** — the single surface that holds everything the human receives. A base
  item interface over heterogeneous shapes; you query, filter, and stream it.
- **Notification** — delivering news to the human (FYI). Stands on its own; it
  *falls into* the inbox through an adapter, and may carry a quick-action that
  promotes it into a Decision.
- **Decision** — a unit of work that **gates a producer**: someone raised it,
  blocks on it, and a human resolves it. Comes in many shapes with different
  interactions and resolutions (PR-approval is one shape). It is **not inherently**
  a Notification or an Inbox item — adapters bridge it to both.

> **Two aims, carried from `the-box.md`:** *wrap determinism* into structured
> units (an item, a decision, a swipe) that enforce behavior; keep the pieces
> **composable** — the three concepts compose through adapters, so a new item
> shape or a new producer drops in without reshaping the others.

## 2. Three concepts, bridged by adapters

The load-bearing modelling choice: **composition, not inheritance.** A Decision is
not a kind-of InboxItem and a Notification is not a kind-of InboxItem. Each is its
own thing; **adapters** wrap a source *as* an inbox item. This is a deliberate
divergence from `the-box.md` §2.2's `InboxItem { Notification | Decision }`
sketch — it keeps Decision and Notification ignorant of the Inbox, so each is
independently testable and reusable.

```
 Domain/Decisions            Domain/Notifications          Domain/Inbox
 ────────────────            ────────────────────          ────────────
 Decision                    Notification                  InboxItem (abstraction
   Subtype                     Scope: box | global                     over a source)
   Prompt / Options?           Subject / Body                Kind, Critical, Tags,
   Critical                    QuickAction?  ──promotes──►   BoxId?, CreatedAt, read?
   state: pending|resolved        a Decision               IInbox
   Outcome = Swipe            INotifier.Raise()               Publish / Query / Stream
 IDecisions                                                 Adapters (the bridges):
   Raise → awaitable                                          Notification ⇒ InboxItem
   Resolve(id, Swipe)                                         Decision     ⇒ InboxItem
   Query / List
 Swipe { Direction(approve|deny), Note?, ScopeHint? }     ← deny REQUIRES Note (§5)
```

The dependency arrows point **into** Inbox: `Domain/Inbox` references the
Decision/Notification concepts to adapt them; neither of those references Inbox.

## 3. The Decision system *(the novel core)*

A **Decision** is the generalization of today's agent decision (`Domain/Agents/
PendingDecision` + `PendingDecisions` + `InteractiveResolver`, §7). It is a unit
of work that a producer raises and **blocks on** until a human resolves it.

- **Shape varies by subtype.** Subtypes carry different payloads and admit
  different interactions/resolutions:
  - **PR-approval** — the stack-review card: approve/deny, forced note on deny,
    optional scope hint (the worked example we model first).
  - **binary** — yes/no.
  - **choice** — pick A / B / none (e.g. speculative-path fork, `the-box.md` §10).
  - **critical-confirm** — high-stakes; extra friction, structured "what you'd
    inspect" view (`the-box.md` §6).
- **Resolution is a `Swipe`** — `{ Direction(approve | deny), Note?, ScopeHint? }`.
  The **deny-note invariant** (§5) is enforced *here*, in the domain, not in the UI.
- **The registry is the awaitable seam.** `IDecisions.Raise` parks the decision
  and returns a task that completes when a human `Resolve`s it (or the producer's
  token trips ⇒ null ⇒ the producer treats it as unresolved). This is exactly
  today's `PendingDecisions.Register` mechanism, lifted to the general Decision.

```csharp
public enum DecisionSubtype { PrApproval, Binary, Choice, CriticalConfirm }

public enum SwipeDirection { Approve, Deny }

public sealed record Swipe(SwipeDirection Direction, string? Note, string? ScopeHint);

public sealed record Decision(
    Guid Id,
    DecisionSubtype Subtype,
    string Prompt,
    IReadOnlyList<string> Options,
    bool Critical,
    DateTimeOffset CreatedAt);

public interface IDecisions
{
    Task<Swipe?> Raise(Decision decision, CancellationToken ct);
    bool Resolve(Guid id, Swipe swipe);
    IReadOnlyList<Decision> List();
}
```

`Resolve` rejects a `Deny` whose `Note` is null/blank — a denied decision without
a reason never round-trips. (Approve needs no note.)

## 4. The Notification system

A **Notification** delivers information to the human. It **gates nothing** — it is
ack/dismiss only. It stands alone (it could be delivered by push, mail, or the
inbox); it *falls into* the inbox via the §6 adapter.

- **Scope** — `box` ("phase 3 failed CI") or `global` ("token expiring").
- **Quick-action** — an optional action that **promotes the notification into a
  Decision** (e.g. *"CI failed on phase 3 — [open as fix decision]"*). This is the
  one place Notification reaches toward Decision, and it does so by *raising* one
  through `IDecisions`, not by being one.

```csharp
public enum NotificationScope { Box, Global }

public sealed record Notification(
    Guid Id,
    NotificationScope Scope,
    string Subject,
    string Body,
    DateTimeOffset CreatedAt);

public interface INotifier
{
    void Raise(Notification notification);
}
```

## 5. The Inbox surface

The **Inbox** is the single stream of everything the human receives, across every
producer. It holds **InboxItems** — the base abstraction over a source.

- **One stream, heterogeneous items**, both kinds interleaved.
- **Flat chronological** by default, with **filters** — by Box, by kind
  (notification | decision), criticals-only (`the-box.md` §5). No priority engine:
  the producer keeps working other Boxes while an item waits.
- **`Publish` / `Query` / `Stream`.** `Stream` is the change feed S5 will deliver
  over SSE; in S1 it is consumed in-process by tests.

```csharp
public enum ItemKind { Notification, Decision }

public sealed record InboxItem(
    Guid Id,
    ItemKind Kind,
    Guid SourceId,
    string Title,
    bool Critical,
    Guid? BoxId,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    bool Read);

public sealed record InboxFilter(Guid? BoxId, ItemKind? Kind, bool CriticalsOnly);

public interface IInbox
{
    void Publish(InboxItem item);
    IReadOnlyList<InboxItem> Query(InboxFilter filter);
    IAsyncEnumerable<InboxItem> Stream(CancellationToken ct);
}
```

### The invariant that lives here

- **Deny carries a reason.** Enforced in `Domain/Decisions` (§3), surfaced as the
  guarantee an inbox client relies on: a denied decision always has a note.

## 6. The adapters *(the bridges)*

The adapters are the whole point of the composition model — each turns a source
into an `InboxItem`, and a raised Decision additionally emits a Notification so the
human is *told* about it. A producer does one thing (`Raise` a decision); handlers
fan it out:

```
producer.Raise(decision) ──► IDecisions parks it (awaitable)
                          ├─► DecisionInboxAdapter:  Decision     ⇒ InboxItem ─► IInbox.Publish
                          └─► (decision raised)      Notification ⇒ InboxItem ─► IInbox.Publish
                                                       "phase 3 needs you"
notifier.Raise(note)      ──► NotificationInboxAdapter: Notification ⇒ InboxItem ─► IInbox.Publish
```

So: **Decision ⇒ InboxItem**, **Notification ⇒ InboxItem**, and *raising a
decision* also *raises a notification*. Decision and Notification never reference
`IInbox`; the adapters (in `Domain/Inbox`) own the wiring. The human swipes the
decision item → `IDecisions.Resolve(id, swipe)` → the producer's `Raise` task
completes.

## 7. What we extend, what we leave *(decision: standalone + bridge later)*

S1 builds `Domain/Decisions` / `Domain/Notifications` / `Domain/Inbox` **fresh**
and **leaves `Domain/Agents`' `PendingDecisions` untouched** for now. The agent's
decision flow keeps working exactly as it does today; a thin adapter that lets the
agent raise into the general `IDecisions` is a **later** task (when a second real
producer — the Box — exists to justify the merge). This keeps S1's blast radius to
new folders only.

The existing model is still the **template** we generalize from, 1:1:

| Existing (`Domain/Agents`) | Generalizes to (`Domain/Decisions`) |
|---|---|
| `PendingDecision(Id, Kind, Prompt, CreatedAt)` | `Decision(Id, Subtype, Prompt, Options, Critical, CreatedAt)` |
| `DecisionKind { Permission, Question }` | folded into `DecisionSubtype` |
| `PendingDecisions` (ConcurrentDictionary + TCS) | `IDecisions` registry (same park-and-await mechanism) |
| `Resolution { Auto, Llm, Deny, Human }` | unchanged — *who* resolved, distinct from the human's `Swipe` |
| answer is a bare `string?` | answer is a `Swipe` (Direction + Note + ScopeHint), deny-note enforced |

> The two registries coexist until the bridge lands. That is the accepted cost of
> "standalone + bridge later" — chosen to keep S1 independent.

## 8. Scope — domain-only S1 *(decision)*

S1 is **domain + an in-process test producer**. **No HTTP, no SSE delivery, no
identity** — those are **S5** (Transport + identity + client). The `Features/Inbox`
slice (endpoints, contracts, the SSE `Watch`, approve-as-owner) lands in S5 on top
of this domain. S1 proves the *behavior* in-process so S5 only adds the pipe.

**Persistence is in-memory** for S1 (as today's `PendingDecisions` is). The durable
store is B2's decision (`the-box.md` §12) — not made here.

### Placement (repo pattern)

Three domain concepts, **folders not assemblies** (YAGNI / least mechanism):

```
src/Domain/Decisions/      Decision, DecisionSubtype, Swipe, IDecisions, Decisions (registry)
src/Domain/Notifications/  Notification, NotificationScope, INotifier, Notifier
src/Domain/Inbox/          InboxItem, ItemKind, InboxFilter, IInbox, Inbox,
                           DecisionInboxAdapter, NotificationInboxAdapter
```

`Domain/Inbox` references the other two (to adapt them); they reference neither.

## 9. Build order (S1 tasks)

| # | Task | Done-when |
|---|---|---|
| **T1** | This design doc | the model + decisions are written down (here) |
| **T2** | `Domain/Decisions` | registry parks/awaits/resolves; deny-note invariant enforced; subtypes modelled — unit-tested, warning-free |
| **T3** | `Domain/Notifications` | notifications raised; quick-action promotes to a Decision — unit-tested |
| **T4** | `Domain/Inbox` | `InboxItem` + the two adapters + query/filter + stream — unit-tested aggregator |
| **T5** | Test producer + E2E thread | a test producer raises notifications **and** decisions; they're queried/filtered; a swipe round-trips and unblocks the producer; deny-without-note is rejected |

Each task ships behind its seam (port + fake) so the next can start, and is **run,
not just compiled** (T5 exercises the whole thread in-process). One coherent commit
per task; nullable on, warnings-as-errors, file-scoped namespaces, net10.0 — the
same bar as the rebuild.

## 10. Done-when (the S1 gate, from the plan)

> *A **test producer** raises notifications/decisions; they're queried/filtered;
> swipes round-trip with the deny-note rule enforced. (Delivery + client are S5.)*

T5 **is** that gate. When it's green, S1 is done and S5 (transport) can compose the
HTTP/SSE surface on top.

## 11. Open decisions / recommendations

Leans, not locks:

- **Quick-action shape** (§4): is a notification's promote-action a typed payload
  (a pre-built `Decision` to raise) or a producer callback? *Lean:* a pre-built
  `Decision` so the adapter stays dumb. Revisit on the first real quick-action.
- **ScopeHint vocabulary** (§3): the gesture/scope-hint enum (e.g. *local-only* vs
  *breaks-downstream*) that biases the reject classifier (`the-box.md` §8). *Lean:*
  free `string?` in S1; enumerate when B3's classifier consumes it.
- **Read/ack model** (§5): S1 carries a `Read` flag and ack/dismiss for
  notifications; the full unread-count / per-device read state waits for S5's client.
- **Agent bridge** (§7): when the Box becomes a second producer, adapt
  `InteractiveResolver` to raise into `IDecisions` and retire `PendingDecisions`.
- **Critical-confirm friction** (§3): the structured "file types you'd normally
  inspect" view is an `INodeProjector` (S4) concern; Decision only carries the
  `Critical` flag and subtype here.
