---
type: spike-findings
status: done
tags: [#ui, #blazor, #wasm, #neumorphism, #transitions]
---

# UI Shell Spike — Findings

Throwaway spike to validate front-end tech for the eventual product UI
([`PLANS/product-ui-spec.md`](../../PLANS/product-ui-spec.md)) **before** building
the real `RemoteAgents.Web`. Not wired into `RemoteAgents.slnx`. Goal: prove the
shell setup, the neumorphic style, and a unified screen-to-screen transition —
all on static stubs, no domain.

## What was built

A standalone **Blazor WebAssembly** app (`spikes/ui-shell/`) rendering two
neumorphic screens with stub data and an animated swap between them:

- **Home / Command Center** (dashboard) — topbar, 4 KPI tiles, Needs-attention
  queue, Recent activity, two active-run cards, Quick actions.
- **Run detail** — run header, Steps, Live log. Reached by clicking the
  "Build Deck Shuffler" card; **← Back** returns.
- **Melt / extrude transition** between them (the deliverable that took the most
  iteration — see below).

Key files:
- `Pages/Home.razor` — app shell + the two-phase transition orchestrator.
- `Components/DashboardView.razor`, `Components/RunDetailView.razor` — the screens.
- `StubData.cs` — all canned content as typed records (one swap-point for a real client).
- `wwwroot/css/app.css` — the whole neumorphic system + transition keyframes.

## Tech + decisions

- **Blazor WASM standalone**, net10.0. Matches the committed `RemoteAgents.Web`
  assembly (WASM → Contracts). Chosen over Server/MAUI/Blazor-Web-App.
- **Plain hand-written CSS, no component library.** Neumorphism is a custom
  shadow style; Material/Fluent libraries fight it. The system is two reusable
  primitives — `.neu-raised` / `.neu-inset` — plus CSS variables for the whole
  palette. Change one variable, the theme moves.
- **System font stack** (Segoe UI) + `Cascadia Code`/`Consolas` mono — no web-font
  fetch at runtime.
- **Light-only by design.** Neumorphism needs a second shadow set for dark mode;
  out of scope.

## The transition: two approaches, one kept

### Approach A — CSS View Transitions API (built, then abandoned)

First attempt used `document.startViewTransition` for a unified swap with
shared-element morphing (persistent elements FLIP only when they move). It
produced a working cross-fade + slide + morph, but was the **wrong tool** for the
target look and carried sharp edges:

- **Animates flat rasterized snapshots.** It can transform/fade/blur/clip the
  snapshot images but **cannot grow a real `box-shadow`** — which is the entire
  point of a neumorphic "rise out of the surface" effect.
- **Suppressed on hidden documents.** The preview tool's Chromium runs
  permanently `visibilityState: "hidden"`, and the API is a no-op there. This
  made the whole feature invisible to in-tool verification (see Observability).
- Bugs surfaced (below) before it was dropped.

### Approach B — CSS melt/extrude on real elements (kept)

The reference video showed a **screen swap**: outgoing cards **melt** (shadow
collapses flat, content fades, card sinks into the surface), the screen empties,
then incoming cards **extrude** (shadow grows flat→raised, content fades, card
rises out). A bottom pill narrates `melting…` / `extruding…`. This is
**shadow-driven**, so View Transitions cannot express it.

Final mechanism — **no View Transitions, no JS interop**:

- **One shared pair of CSS keyframes** (`melt`, `extrude`) on every `.morph-item`,
  animating `box-shadow` (flat ↔ raised) + `transform` (scale/translate) + opacity.
- **Two-phase orchestration in Blazor** (`Home.razor`): set `.melting` → `await`
  → swap the view → set `.extruding` → `await` → idle. Pure `Task.Delay` timing.
- **Per-component stagger** via a `--i` index → top-to-bottom ripple.
- **Persistent chrome** = anything not a `.morph-item` (the topbar never melts).
- **Configurable** via presets (Snappy / Smooth / Bouncy) feeding `--melt-dur`,
  `--extrude-dur`, `--stagger`, `--motion-ease`. Honors `prefers-reduced-motion`.
- **Fixed panel height** (`min-height: max(840px, calc(100vh - 76px))`) so the
  container doesn't resize between screens — focus stays on the content.

Bonus: because it's plain CSS animation (not visibility-gated), it is fully
observable in headless capture.

## Problems found and fixed

1. **Transition silently "skipped" — swap with no animation.** Root cause was a
   chain of two bugs, both *invisible* until the rejected `transition.ready`
   promise was logged in a **visible** browser:
   - **`NullReferenceException` in the render barrier.** `StateHasChanged()`
     re-entered `OnAfterRender` *synchronously*, nulling the `TaskCompletionSource`
     before the next line read it. The thrown exception rejected the
     `startViewTransition` callback → the browser discarded the transition.
     *(Fixed by holding the TCS in a local.)*
   - **`requestAnimationFrame` deadlock.** With the NRE gone, an rAF placed
     *inside* the update callback never fired (the browser doesn't service rAF
     mid-callback), tripping the API's ~4s "timeout in DOM update". *(Fixed by
     removing the rAF — the `OnAfterRender` barrier already guarantees commit.)*
   - Both had been masked by a `.catch(() => {})` swallowing the skip reason. The
     lesson: **surface the skip reason, never swallow it.** (Moot once we dropped
     the API, but it's why the wrong tool cost so much time.)

2. **Blazor render not committed before the snapshot.** The "after" snapshot was
   captured before Blazor flushed the new DOM, so it animated old→old (looked
   frozen) then swapped instantly. Required a deterministic render barrier
   (`TaskCompletionSource` completed in `OnAfterRender`), not `Task.Yield` + rAF.

3. **The headless preview can't show View Transitions at all** (hidden document).
   This made the tool report "working" when only navigation worked — the original
   wrong call. Resolved by the Playwright harness below.

4. **Panel resized between screens.** The dashboard (~799px) and run-detail had
   different natural heights. Fixed with a `min-height` floor above the taller
   screen so both render at 840px.

5. **Stale `UiShell.styles.css` 404.** Deleting the template's scoped `.razor.css`
   files stopped Blazor emitting the scoped-CSS bundle, but `index.html` still
   linked it. Removed the link.

## Observability capability (built during debugging)

The preview tool is blind to anything a hidden document suppresses. To see
motion, a **Playwright harness** in `%TEMP%/vt-debug/` drives the app in system
Chrome (which reports `visible`):

- `capture2.js` — triggers the swap and screenshots mid-flight frames; tiled into
  a contact sheet with ffmpeg to inspect the whole arc at once.
- `measure.js` — reads `.app-shell` height on both screens (used for the
  equal-height fix).
- `record.js` — records the page to `.webm` (Playwright video), then ffmpeg →
  GIF + MP4 to share a moving clip.

Setup: `npm i playwright` with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` (uses system
Chrome via `channel: 'chrome'`), plus `npx playwright install ffmpeg` for video.
This is the only reliable way to *see* CSS animations / View Transitions from
here.

## Carry-forward to the real `RemoteAgents.Web`

- **Lift the melt/extrude system** (`app.css` keyframes + the `Home.razor`
  two-phase orchestrator + `.morph-item`/`--i` convention). It's domain-agnostic.
- **Neumorphic primitives** (`.neu-raised`/`.neu-inset` + palette variables) port
  verbatim; `StubData` is replaced by the Contracts-backed client.
- **Use `dotnet watch -c Release`** for iteration — Debug WASM adds interop
  latency; Release is crisp. (Mattered more for the abandoned VT path; the CSS
  approach is light.)
- **Keep the Playwright capture/record harness** as the visual-check tool for the
  real UI — it's the workaround for the preview's hidden-document blind spot.
- **Decide later:** dark-mode shadow set; per-component vs whole-screen stagger
  defaults; whether any screen needs true shared-element morph (would reintroduce
  View Transitions *alongside* the melt/extrude, for move-only cases).
