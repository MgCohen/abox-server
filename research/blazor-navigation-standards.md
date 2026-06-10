# Blazor screen-navigation standards (.NET 10) — what to adopt for this UI

**Date:** 2026-06-10
**Question:** What are the *standard* Blazor mechanisms for moving between screens,
and which of them should be the convention for the orchestrator UI — given a
~30-screen, entity-first product (`product-ui-spec.md`), a Blazor WASM front-end
reachable from a phone over Tailscale (`csharp-orchestrator-ui.md`), and the
in-house **Morph** transition library that already owns *how* a swap animates
(`animation-ui-foundation.md`)?

This is a standards/convention scan, not a build plan. It answers "when you add a
screen, how does the user get to it, and how is that wired" so every screen is
built the same way and Morph stays the only place animation lives.

---

## TL;DR recommendation

**Real, deep-linkable routes are the navigation spine. In-page state is the
exception, used only *inside* a screen.** Concretely:

1. **One screen = one `@page` route.** Every destination in the nav map
   (`product-ui-spec.md` §2) gets an `@page` template and is reached by URL.
   Deep-linking is a hard requirement here: the owner runs this from a phone,
   shares/bookmarks a running flow, and gets push/notification deep links
   (`unityagents://run/<id>`, `csharp-orchestrator-ui.md` C5). A URL per screen
   is what makes back/forward, refresh, and those links work for free.
2. **Typed route parameters with constraints**, not hand-parsed strings —
   `/runs/{Id:guid}` (already done in `RunView.razor`). The constraint is free
   validation: a malformed id never reaches your code.
3. **Query string for *view state inside* a screen** — filters, tabs, sort,
   selected sub-item. `[SupplyParameterFromQuery]` + `GetUriWithQueryParameter`.
   This keeps "which tab of the Project Control Center am I on" bookmarkable
   without minting a route per tab.
4. **In-page state swap (`MorphStage<TKey>`) only for non-addressable sub-views** —
   a wizard's steps, a master/detail flip where the detail isn't worth a URL, an
   inbox triage pane. If a human would ever want to link to it, it's a route, not
   a `TKey`.
5. **Morph wraps the standard primitives; it does not replace them.**
   `MorphRouteStage` already rides `RegisterLocationChangingHandler` +
   `LocationChanged` — the framework-blessed navigation events. Routing decisions
   stay 100% standard Blazor; Morph only animates the gap between exit and enter.
6. **Adopt the .NET 10 not-found pattern now.** The `<NotFound>` render fragment
   is superseded by `Router.NotFoundPage` + `NavigationManager.NotFound()`. The
   current `App.razor` still uses the legacy fragment — migrate it (one small
   change, see §6).

Nothing here needs a new abstraction. It's the stock Blazor router used the way
the framework intends, plus a single rule (route-by-default, state-by-exception)
to keep 30 screens consistent.

---

## The standard toolkit (and the one-line verdict on each)

Everything below is in-box `Microsoft.AspNetCore.Components.*` — no package.

| Mechanism | What it is | Use it for | Verdict for us |
|---|---|---|---|
| `@page "/template"` | Declares a routable component. Multiple `@page` lines = aliases. | Every screen. | ✅ The spine. One per nav-map destination. |
| Route params `{Id}` | Bind a URL segment to a `[Parameter]`. Case-insensitive. | Entity ids, sub-resource keys. | ✅ Default for "show *this* run/project/agent". |
| Route constraints `{Id:guid}` | Type-gate a segment (`bool/datetime/decimal/double/float/guid/int/long`, `:nonfile`, optional `?`). | Validate ids/numbers at the route. | ✅ Always constrain. Free input validation. |
| Catch-all `{*path}` | Capture a multi-segment tail as `string`. | File-browser paths, nested doc routes. | ◑ Only Files / Living-Docs tree screens. |
| `NavLink` + `NavLinkMatch` | `<a>` that toggles `active` by URL match (`All` vs `Prefix`). | Sidebar + in-page links. | ✅ Sidebar nav. `Match.All` only for "/". |
| `NavigationManager.NavigateTo` | Programmatic nav; `NavigationOptions { ForceLoad, ReplaceHistoryEntry, HistoryEntryState }`. | Post-action redirects (start run → `/runs/{id}`). | ✅ Already used in `Home`/`RunHistory`. |
| `[SupplyParameterFromQuery]` | Bind a query param to a parameter (scalars, nullable, arrays; `Name=` to alias). | Filters, tab, sort, paging. | ✅ The standard for in-screen view state. |
| `GetUriWithQueryParameter(s)` | Build a URL with one/many query params added/changed/removed. | Update filters without a string concat. | ✅ Pair with the above. |
| `RegisterLocationChangingHandler` | Async nav interceptor; `LocationChangingContext.PreventNavigation()`. App-wide, registered in `OnAfterRender`. | Unsaved-changes guards; Morph's exit trigger. | ✅ Morph uses it. Also: builder/editor "discard?" guards. |
| `NavigationLock` | Component-scoped nav interception; `ConfirmExternalNavigation`, `OnBeforeInternalNavigation`. | Guard tied to a component's lifetime. | ✅ Preferred over the manual handler for editor screens (auto-unhooks). |
| `Router.NotFoundPage` + `NavigationManager.NotFound()` | .NET 10 not-found: assign a component; call `NotFound()` from code (e.g. unknown run id). | 404 + "resource gone" states. | ✅ Adopt; replaces `<NotFound>` fragment. |
| `FocusOnNavigate Selector="h1"` | Moves focus to the new page heading after nav (a11y / screen-reader). | Every routed layout. | ✅ Already in `App.razor`. Keep. |
| `OnNavigateAsync` (Router) | Async hook per navigation, with a `CancellationToken`. | WASM lazy-load assemblies; prefetch. | ◑ Only if the WASM bundle grows enough to split. |
| `<Navigating>` (Router) | Content shown during slow nav (lazy-load / slow link). | Global "loading page" affordance. | ◑ Pairs with lazy-load; otherwise Morph's loader covers it. |
| Enhanced navigation | Server-side fetch-and-patch, preserves scroll. **Blazor Web App / SSR only.** | — | ❌ N/A: we ship standalone WASM (`csharp-orchestrator-ui.md` C4). |

---

## The one rule that keeps 30 screens consistent: route vs. in-page state

The only recurring judgment call is "new route, or swap a `TKey` inside the
current screen?" Decide it with one question — **would a human ever want to
land here directly** (bookmark, share, refresh, deep-link, back-button)?

| If… | Mechanism | Why |
|---|---|---|
| It's a destination in the nav map | **`@page` route** | Addressable, back/forward, deep-linkable. |
| It identifies a specific entity | **route param + constraint** `/runs/{Id:guid}` | The id *is* the address. |
| It's a filter / tab / sort / paging *within* a screen | **query string** `?phase=running&tab=ops` | Bookmarkable view-state without route explosion. |
| It's an ephemeral sub-view nobody links to | **`MorphStage<TKey>`** in-page swap | No URL cost; animation handled; e.g. wizard steps. |
| It's a transient overlay (modal/slide-over) | component state, **not** navigation | A modal isn't a location (SQ-H2 in the UI spec). |

Worked examples against the nav map:

- **Flow Control Center** (`/runs/{id}`) — a route (already built). Its
  operations/terminal/diff tabs — query string (`?tab=terminal`), not sub-routes.
- **Attention Inbox** (`/inbox`) — a route. Triaging between queued items in the
  pane — `MorphStage<TKey>` in-page swap; the *selected* item can also be a query
  param (`/inbox?item=...`) so a notification can deep-link straight to it.
- **Project Control Center** (`/projects/{name}`) — a route; Files/Git/Tasks are
  tabs → query string. A file path inside Files → catch-all (`/projects/{name}/files/{*path}`).
- **Flow Builder wizard** — steps are `MorphStage<TKey>` (nobody deep-links "step 3
  of an unsaved draft"); the draft *id*, once saved, is a route.

This rule is the whole standard. It's deliberately conservative toward routes:
when unsure, make it a route — you can always collapse to state later, but you
can't retro-fit a deep link onto a `TKey` someone now relies on.

---

## How this layers with Morph (no conflict, clean seam)

Morph is *orthogonal* to routing — it owns the exit→load→enter choreography, not
the decision of where to go. The two triggers map exactly onto the two
navigation mechanisms above:

- **Routes → `MorphRouteStage`.** Sits in `MainLayout` wrapping `@Body`. It hooks
  the standard `RegisterLocationChangingHandler` (melt the old `@Body` before the
  URL commits) and `LocationChanged` (extrude the new `@Body`). Routing is 100%
  stock Blazor; Morph only fills the visual gap. *Today `MainLayout.razor` renders
  `@Body` bare — wrapping it in `MorphRouteStage` is the adoption step
  (`animation-ui-foundation.md` Phase 6).*
- **In-page state → `MorphStage<TKey>`.** The `TKey` swap *is* the in-page
  navigation; `@key` forces the remount so enter replays.

So "navigation standards" and "Morph" answer different questions: the router
decides *which screen*, Morph decides *how the swap looks*. Keeping them separate
is what lets the motion core stay domain-agnostic (its stated scope).

One caveat to honor (already encoded in `MorphRouteStage`): a re-entrancy guard
(`Phase != Idle`) and a same-URL no-op. With standalone WASM there's no enhanced
navigation to complicate handler invocation, so the location-changing handler
fires for every internal nav — exactly what Morph wants.

---

## .NET 10 specifics worth knowing

- **`<NotFound>` fragment is superseded.** Use a `NotFound.razor` component +
  `Router.NotFoundPage="typeof(...)"`, and call `NavigationManager.NotFound()`
  from code when a looked-up resource is missing (e.g. `RunView` for an unknown
  id — today it silently redirects to `/runs`; `NotFound()` is the honest signal).
- **`BlazorDisableThrowNavigationException`.** Historically `NavigateTo` threw a
  `NavigationException` to unwind. In .NET 10 you can set the MSBuild property
  `<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>`
  to opt out of the throw — cleaner control flow after a redirect. Recommended on.
- **`:nonfile` constraint** — guards a top-level optional param from eating
  `*.styles.css` / `favicon.ico`. Relevant if any screen uses a root-level
  `/{slug?}` style route.
- **`HistoryEntryState`** on `NavigateTo` — carry small state across a nav without
  a query param; useful for "came from the Inbox" back-context.
- Enhanced navigation, static-vs-interactive routing, `AddAdditionalAssemblies`
  for SSR — all **Blazor Web App** concepts. We're standalone WASM, so they don't
  apply; don't cargo-cult them in from tutorials.

---

## Findings against the current code (`src/RemoteAgents.Web`)

Mostly already idiomatic — the gaps are small and additive:

| File | State | Action |
|---|---|---|
| `Pages/RunView.razor` | `@page "/runs/{Id:guid}"`, typed param ✅; redirects to `/runs` on missing id | Switch the missing-id path to `NavigationManager.NotFound()` for a true 404. |
| `Pages/RunHistory.razor` | Row click via `Nav.NavigateTo($"runs/{r.Id}")` ✅ | Fine. Consider an `<a>`/`NavLink` wrapper for middle-click/open-in-new-tab + keyboard. |
| `Layout/MainLayout.razor` | `NavLink` sidebar ✅; `@Body` rendered bare | Wrap `@Body` in `MorphRouteStage` at Morph adoption (Phase 6). |
| `App.razor` | Legacy `<NotFound>` fragment | Migrate to `Router.NotFoundPage="typeof(Pages.NotFoundPage)"`; keep `FocusOnNavigate`. |
| filters (e.g. future Runs filter by phase) | none yet | Use `[SupplyParameterFromQuery]` + `GetUriWithQueryParameter` from day one. |

None of these are behavioral changes the user would notice today; they're the
conventions to lock in *before* the screen count multiplies.

---

## Recommendation (the standard to write down)

Adopt these as the repo's Blazor navigation conventions, consistent with the
YAGNI / least-mechanism code standards (no new abstractions — stock router):

1. **Route-by-default.** Every nav-map screen is an `@page`. Reach for in-page
   state only when no one would ever link to the sub-view.
2. **Always constrain route params** (`{Id:guid}`, `{n:int}`). The constraint is
   validation.
3. **Query string for in-screen view state** (tab/filter/sort/selection) via
   `[SupplyParameterFromQuery]` + `GetUriWithQueryParameter`.
4. **`MorphStage<TKey>` is for non-addressable sub-views only;**
   `MorphRouteStage` wraps `@Body` for real routes. Routing stays stock; Morph
   stays motion-only.
5. **`.NET 10` not-found:** `Router.NotFoundPage` + `NavigationManager.NotFound()`;
   retire the `<NotFound>` fragment.
6. **Guard editors with `NavigationLock`** (component-scoped, auto-unhooks) rather
   than a hand-managed `RegisterLocationChangingHandler`, unless the guard must
   outlive the component.
7. **Keep `FocusOnNavigate Selector="h1"`** on the router — a11y baseline, and the
   phone is a primary surface.
8. Set `<BlazorDisableThrowNavigationException>true</…>` for clean post-redirect
   control flow.

**Why not something fancier (a nav service, a route registry, a state-machine
router):** none of the 30 screens need it. The framework already gives typed
routes, constraints, query binding, nav guards, and not-found handling. The only
thing missing was a *convention* for when to use which — that's the route-vs-state
rule in §3, and it costs zero new code.

---

## Sources

- [ASP.NET Core Blazor routing (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0) — Router, `@page`, route params/constraints, catch-all, `FocusOnNavigate`, `NotFoundPage`, `OnNavigateAsync`, `AdditionalAssemblies`.
- [ASP.NET Core Blazor navigation (.NET 10)](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0) — `NavigationManager`, `NavigateTo`/`NavigationOptions`, `LocationChanged`, `RegisterLocationChangingHandler`, `NavigationLock`, `NotFound()`, query strings, `GetUriWithQueryParameter`, enhanced navigation, `BlazorDisableThrowNavigationException`.
- [Routing management and NotFound pages in Blazor (Telerik)](https://www.telerik.com/blogs/routing-management-creating-notfound-pages-blazor) — .NET 10 NotFound migration walkthrough.
- Internal: `PLANS/product-ui-spec.md` (nav map / IA), `PLANS/csharp-orchestrator-ui.md` (WASM + phone/Tailscale + deep links), `PLANS/animation-ui-foundation.md` + `src/Morph/` (the two transition triggers).
