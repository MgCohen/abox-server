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
| `Pages/RunView.razor` | loads snapshot + opens SSE in `OnInitializedAsync` ⚠️ | **Latent bug** (deep pass §2.1): `OnInitialized` won't re-fire on a run→run nav. Move id-driven load to `OnParametersSetAsync` with a prev-id guard, or `@key="Id"`. |
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

---

## Deep pass (multi-source, verified)

A 5-angle fan-out (component-library conventions · state-location frameworks ·
history/lifecycle bugs · route organization & abstractions · .NET 10 / a11y /
transitions) across ~50 sources. Method: each angle returned falsifiable claims
with confidence + source; the load-bearing and contradictory ones were
re-checked against the .NET 10 Learn docs. The conclusions above held — the deep
pass mostly *strengthened* them and surfaced a few high-value deltas, one of
which is a latent bug in our current code.

### What it confirmed (high confidence, official docs)

- **Route-by-default + query-string-for-view-state is the documented mainstream.**
  `[SupplyParameterFromQuery]` (types: `bool/DateTime/decimal/double/float/Guid/int/long/string`,
  their nullables, and arrays via repeated keys `?x=1&x=2`) + `GetUriWithQueryParameter(s)`
  is *the* Blazor deep-linking mechanism; the stated benefit is shareable links
  that restore selections and survive back/forward. (Learn navigation; Jon Hilton,
  *Blazor deep linking*.)
- **The cross-framework consensus matches our rule.** React Router's official
  state-management guidance buckets state identically: resource identity → route
  segment; shareable UI state → query string; transient UI (modals, hover) →
  component state; and argues URL-state "leads to less code … no state
  synchronization bugs." TanStack frames search params as first-class state. So
  "route/query by default, component-state by exception" is the prevailing SPA
  position, not a house quirk. (reactrouter.com/explanation/state-management;
  tanstack.com/blog/search-params-are-state.)
- **Standalone WASM is the *easy* mode for nav guards and transitions.**
  `RegisterLocationChangingHandler` fires reliably for `NavigateTo`, internal link
  clicks, and back/forward in standalone WASM. Under **Blazor Web App + enhanced
  navigation** handlers are *best-effort* (link clicks may not invoke them) — the
  docs say "app logic must not depend on the handler running." We ship standalone
  WASM, so Morph's `LocationChanging`-based exit trigger sits on the reliable side
  of that line. (Learn routing, *enhanced navigation*; aspnetcore #44365/#49950.)

### What it changed / added (the deltas worth acting on)

1. **⚠️ Latent bug in `RunView.razor` — lifecycle, not routing.** `OnInitialized{Async}`
   runs **once per component instance** and does **not** re-fire when you navigate
   within the *same* `@page` to a different param (`/runs/{A}` → `/runs/{B}`):
   Blazor reuses the instance and only re-runs `OnParametersSet`. `RunView` loads
   its snapshot *and* opens the SSE stream in `OnInitializedAsync`, so a direct
   run→run navigation would keep showing run A. Fix: move the id-driven load to
   `OnParametersSetAsync` with a previous-id guard (`if (Id != _loadedId)`), or put
   `@key="Id"` on the route content to force a remount. `OnParametersSet`+guard is
   the lighter, recommended default; `@key` is the heavier hammer (full re-init,
   drops state). *Today this is masked because the UI only reaches a run via the
   list, but it's a real correctness gap to close when standardizing.*
   (Jon Hilton, *navigating to the same page*; Learn lifecycle.)

2. **Modal-as-route: nuance the draft under-stated.** "A modal isn't a location"
   is the right *default* — Blazored.Modal and MudBlazor both model dialogs as
   component/service state, and **Blazor has no equivalent to Next.js
   intercepting/parallel routes**, so there's no clean native "routed modal."
   *But* route/query-encode a dialog when deep-link / refresh-survival /
   shareability genuinely matter — e.g. an Attention-Inbox item opened from a phone
   notification is better as `/inbox?item={id}` (a modal-over-list whose open state
   is in the URL) than pure component state. Rule: **modal = component state, unless
   someone must be able to link straight to it.** (React Router "background
   location"; Next.js intercepting+parallel routes as the gold standard we *can't*
   replicate; Blazored/Modal.)

3. **Shared RCL is *the* architecture for WASM + MAUI Hybrid — promote it.**
   Microsoft's recommended pattern (and the .NET 9+ template) puts all routable
   components, layouts, and the NavMenu in a **host-agnostic Razor Class Library**;
   WASM and MAUI are thin shells that route into it via the Router's
   `AdditionalAssemblies`. This is exactly the old `UI.Components` split in
   `csharp-orchestrator-ui.md` — the deep pass says keep that as the standard, not
   per-host page copies. MAUI-specific: a link is "internal" only when host+scheme
   match; control external links via the `UrlLoading` event; `BlazorWebView.StartPath`
   sets the initial route; OS deep links (Android App Links / Apple Universal Links,
   and the service-worker `clients.openWindow` for PWA push) route *into* Blazor —
   they aren't a Blazor-router feature. (Learn hybrid/class-libraries-best-practices;
   Learn hybrid/routing; BethMassi/HybridSharedUI.)

4. **Layout conventions for 30 screens (concrete, official).** Set the app default
   via `Router`/`RouteView` `DefaultLayout` (Microsoft: "most general and flexible"),
   not per page. Give an entity area its own layout by putting `@layout X` in that
   folder's `_Imports.razor` (cascades to the subtree) — the idiomatic fit for an
   entity-first sidebar with per-entity sub-nav. **Never** put `@layout` in the
   *root* `_Imports.razor` (infinite layout loop); keep layout components in their
   own folder. For page→shell content injection (per-screen title/toolbar/actions
   into the top bar), use `SectionOutlet`/`SectionContent` (prefer the `SectionId`
   static-object form over string `SectionName` to avoid collisions). (Learn
   layouts; Learn sections; blazor-university nested layouts.)

5. **Don't wrap `NavigationManager` — YAGNI wins this one with evidence.** The
   usual pro-abstraction argument is testability, but bUnit ships a built-in
   `FakeNavigationManager` (captures navigations, exposes a `History` stack to
   assert against), so you can inject the real `NavigationManager` and still test.
   A custom `INavigationService` only earns its place if a UI-agnostic MVVM
   ViewModel layer needs navigation under test — which we don't have. Likewise,
   typed-route **source generators** exist (PodNet.Blazor.TypedRoutes, safe-routing)
   but carry caveats (regex scanning, no `@attribute [Route]` support, untested
   lazy-load); a **hand-written route-constants class** is the lower-mechanism
   option until the route count earns the generator. (bUnit docs; PodNet README.)

6. **Morph vs. the "keep both routes mounted" school — a real design note.**
   Morph drives the exit animation from `LocationChanging` (delay the old `@Body`'s
   melt *before* the URL commits). Documented caveats of that approach: handlers
   run **in parallel app-wide**, navigation is **reverted-then-replayed**,
   overlapping navigations **collapse (last-wins)**, and a **same-URL `NavigateTo`
   is a no-op**. Morph already guards the two that bite (re-entrancy `Phase != Idle`,
   same-URL skip) and lives on the reliable standalone-WASM side. The main prior-art
   alternative, **`BlazorTransitionableRoute`**, sidesteps the revert/replay
   machinery by keeping previous+current route **both mounted** and animating on
   `LocationChanged` (after the fact), which also preserves the outgoing page's
   scroll/instance. Not a reason to change Morph (it's spike-validated and owns the
   neumorphic box-shadow effect that library can't do — see `transition-prior-art.md`),
   but the two-routes-mounted model is the pattern to reach for *if* the
   revert/replay timing ever proves fragile under rapid navigation. (Learn
   navigation async model; JByfordRew/BlazorTransitionableRoute.)

### Version-sensitive & honestly-contested points

- **`<NotFound>` fragment → `NotFoundPage`.** In the .NET 10 Learn docs the legacy
  `<NotFound>` section is gated to ≤9.0; .NET 10 documents only `Router.NotFoundPage`
  + `NavigationManager.NotFound()` (resolution precedence:
  `OnNotFound`→`NotFoundPage`→status-code re-execution middleware). Treat the
  fragment as **superseded**; migrate. (Residual uncertainty: how completely the
  fragment still *functions* in *standalone WASM* specifically vs. just being
  discouraged — verify at migration. This is the report's lowest-confidence claim.)
- **`BlazorDisableThrowNavigationException` is opt-in *for upgraded apps*.** On by
  default only in fresh .NET 10 templates; an upgraded project adds the MSBuild
  property manually. Watch: once enabled, test harnesses that `catch
  (NavigationException)` silently stop catching. (Mostly an SSR concern; lower
  stakes for pure WASM, but set it for clean post-redirect flow.)
- **Scroll restoration is a .NET 10 fix.** "Scroll to top on cross-page nav,
  preserve on same-page, restore on back/forward" for enhanced navigation was only
  fixed in .NET 10 (aspnetcore #51646). We're standalone WASM (no enhanced nav), so
  this is the standard SPA history model for us — relevant mainly to the PWA/MAUI
  surfaces. (aspnetcore #51338/#51646.)
- **How aggressively to push state into the URL is genuinely debated.** Maximalists
  (TanStack, React Router) push most shareable/persistent state to URL/cookies;
  minimalists (LogRocket/freeCodeCamp) warn it's global state with sync cost,
  string-only serialization, ~2–4k URL limits, and **never secrets**. Both agree on
  the floor (filters/sort/paging/tab → URL) and ceiling (secrets, bulky/nested →
  not URL); they split on the middle (modals, form drafts). Our rule lands in that
  agreed band.
- **`FocusOnNavigate` is necessary but not sufficient for a11y.** It moves focus to
  `<h1>` but does **not** update the document `<title>` or use an ARIA live region;
  the strongest screen-reader experience pairs it with a title update / live-region
  announcement (WCAG 2.4.3). Worth adding given the phone-first audience. (Learn
  routing; VA/Deque SPA focus-management prior art.)
- **`NavigationLock ConfirmExternalNavigation` only fires after first user
  interaction** (browser `beforeunload` security) — an immediate close/refresh isn't
  intercepted. Fine for editor "discard?" guards; don't rely on it as a hard lock.

### Net effect on the recommendation

The standard above stands. The deep pass adds five concrete to-dos when
standardizing: (1) fix the `RunView` `OnInitialized`→`OnParametersSet` lifecycle
gap; (2) allow URL-encoded modals for the notification-deep-link cases (Inbox);
(3) make the shared host-agnostic RCL explicit as the WASM+MAUI architecture;
(4) adopt the layout conventions (`DefaultLayout`, per-folder `_Imports @layout`,
`SectionOutlet` for the top bar); (5) skip a `NavigationManager` wrapper and a
typed-route generator until a second real use appears. None require new
abstractions; all are stock-Blazor conventions.

---

## Sources

**Official (Microsoft Learn, .NET 10 / GA-era):**
- [Blazor routing](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0) — Router, `@page`, route params/constraints, catch-all, `FocusOnNavigate`, `Router.NotFoundPage`, `OnNavigateAsync`, `AdditionalAssemblies`, enhanced-navigation handler caveats.
- [Blazor navigation](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/navigation?view=aspnetcore-10.0) — `NavigationManager`, `NavigateTo`/`NavigationOptions` (`ForceLoad`/`ReplaceHistoryEntry`/`HistoryEntryState`), `LocationChanged`, `RegisterLocationChangingHandler`, `NavigationLock`, `NotFound()`, query strings, `GetUriWithQueryParameter(s)`, `BlazorDisableThrowNavigationException`.
- [Blazor layouts](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/layouts?view=aspnetcore-10.0) · [sections](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/sections?view=aspnetcore-10.0) · [lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle?view=aspnetcore-8.0) · [project structure](https://learn.microsoft.com/en-us/aspnet/core/blazor/project-structure?view=aspnetcore-10.0) · [class libraries](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/class-libraries?view=aspnetcore-8.0)
- Hybrid: [shared-UI / class-library best practices](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/class-libraries-best-practices?view=aspnetcore-10.0) · [hybrid routing](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/routing?view=aspnetcore-10.0) · [PWA push](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app/push-notifications?view=aspnetcore-10.0) · [server state management](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/server?view=aspnetcore-10.0)

**Cross-framework prior art (state location, modals, history, a11y):**
- [React Router — state management](https://reactrouter.com/explanation/state-management) · [TanStack — search params are state](https://tanstack.com/blog/search-params-are-state)
- [Next.js intercepting routes](https://nextjs.org/docs/app/api-reference/file-conventions/intercepting-routes) · [parallel routes](https://nextjs.org/docs/app/api-reference/file-conventions/parallel-routes) (the routed-modal gold standard Blazor can't replicate)
- [Correct modal navigation with React Router](https://markandruth.co.uk/2019/08/12/implementing-correct-modal-navigation-with-react-router) · [LogRocket — URL as state container](https://blog.logrocket.com/query-strings-underrated-using-url-apps-state-container/)
- [Accessible route-change focus (React)](https://jshakespeare.com/accessible-route-change-react-router-autofocus-heading/) · [VA focus management](https://design.va.gov/accessibility/focus-management) · [Deque — SPA a11y](https://www.deque.com/blog/accessibility-tips-in-single-page-applications/)

**Practitioner / libraries:**
- Jon Hilton: [deep linking](https://jonhilton.net/blazor-deep-linking/), [navigating to the same page](https://jonhilton.net/blazor-navigation-same-page/), [focus](https://jonhilton.net/focus-blazor/), [folder structure](https://jonhilton.net/blazor-component-folder-structure/)
- Component libs: [MudBlazor drawer/appbar](https://deepwiki.com/MudBlazor/MudBlazor/6.4-drawer-and-appbar) · [Radzen layout](https://blazor.radzen.com/layout) · [FluentUI NavMenu](https://fluentui-blazor.azurewebsites.net/NavMenu) · [Telerik — .NET 10 Blazor changes](https://www.telerik.com/blogs/net-10-has-arrived-heres-whats-changed-blazor)
- Nav / testing / routes: [bUnit FakeNavigationManager](https://bunit.dev/docs/test-doubles/navigation-manager.html) · [PodNet.Blazor.TypedRoutes](https://github.com/podNET-Hungary/PodNet.Blazor.TypedRoutes) · [Blazing.Mvvm](https://github.com/gragra33/Blazing.Mvvm)
- Transitions: [BlazorTransitionableRoute](https://github.com/JByfordRew/BlazorTransitionableRoute) · [Toolbelt.Blazor.ViewTransition](https://github.com/jsakamoto/Toolbelt.Blazor.ViewTransition)
- Modals: [Blazored.Modal](https://github.com/Blazored/Modal)
- Framework history (version-sensitive claims): aspnetcore [#51646](https://github.com/dotnet/aspnetcore/issues/51646) (scroll), [#42902](https://github.com/dotnet/aspnetcore/issues/42902) (Navigating remount), [#44365](https://github.com/dotnet/aspnetcore/issues/44365)/[#49950](https://github.com/dotnet/aspnetcore/issues/49950) (handler invocation)
- Hybrid shared-UI sample: [BethMassi/HybridSharedUI](https://github.com/BethMassi/HybridSharedUI)

**Internal:** `PLANS/product-ui-spec.md` (nav map / IA) · `PLANS/csharp-orchestrator-ui.md` (WASM + MAUI + phone/Tailscale + deep links) · `PLANS/animation-ui-foundation.md` + `src/Morph/` (transition triggers) · `research/transition-prior-art.md` (why Morph is custom).
