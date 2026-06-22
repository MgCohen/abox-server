# Prior-art scan: container-based, depth-layered, shadow-driven Blazor transitions

**Date:** 2026-06-09
**Question:** Does an existing framework/library already do the screen-to-screen
transition we're about to build, so we don't reinvent it?

## The exact thing we're matching against (4 core needs)

1. **Container/shape-based & reusable** — a small set of base "shape" components
   own the animation; callers pour in data, never write per-component animation.
2. **Depth-LAYERED stagger** — nested containers animate in layers *by nesting
   depth*: all depth-0 together → wait → depth-1 → wait → depth-2. A structural
   ripple, not a flat sibling stagger.
3. **Shadow-driven ("melt/extrude")** — animates a real CSS `box-shadow`
   (neumorphism: cards melt flat into a clay surface, then extrude back out) plus
   scale/opacity. This is *why* the browser View Transitions API was rejected.
4. **Across real, deep-linkable routes** — not just in-page swaps.

Bonus: extensible to other animation types; async-gated orchestration
(melt → await content load → extrude).

---

## TL;DR recommendation

**Build-our-own orchestration on top of an adopted animation engine — adopt
GSAP (with the free Flip plugin) via JS interop, and build needs (1), (2), (4)
ourselves.** No library does all four; in particular **nothing does need (2),
the depth-layered structural stagger** — that is genuinely novel orchestration
logic and is the part we must own regardless of which engine we pick. The engine
choice only buys us need (3) (real `box-shadow` keyframing) and saves us hand-
rolling a tween/RAF loop.

- **GSAP** is now **100% free incl. all plugins (Webflow, April 2025)**, animates
  `box-shadow` via `props`, has first-class `nested:true` + `stagger`, and is the
  most battle-tested. Slight weight; not MIT (custom "no-charge" license).
- **Motion (motion.dev)** is the MIT, lighter alternative — equally capable of
  `box-shadow` + stagger; pick it if license purity / bundle size matters more
  than GSAP's Flip/FLIP machinery.

Either way the **container component model, the depth-layer scheduler, and the
route-overlap plumbing are ours to build.**

---

## Ranked shortlist with one-line verdicts

| # | Option | Verdict |
|---|--------|---------|
| 1 | **GSAP + Flip plugin** (JS interop) | Best engine: real `box-shadow`, `nested`, `stagger`, now fully free. Still need our own container + depth-layer + route layers. |
| 2 | **Motion / Motion One (motion.dev)** (JS interop) | MIT, lighter, same `box-shadow`+`stagger` power. Same build-it-ourselves gaps as GSAP; no FLIP-grade layout helper in core. |
| 3 | **anime.js v4** (JS interop) | MIT, explicitly fixed `box-shadow` complex-value handling, has `stagger()`/timeline. Viable engine; same gaps. |
| 4 | **BlazorTransitionableRoute** | Solves *only* need (4): keeps old+new route alive so you can animate the swap. Provides hooks, **no animation**. Targets .NET 6. Useful pattern reference for the route-overlap seam. |
| 5 | **Framer Motion** (React — concept only) | Closest conceptual match to need (2) via `staggerChildren` variant inheritance, but **React-only** — unusable in Blazor. Mine it for the layered-stagger mental model. |
| 6 | **Toolbelt.Blazor.ViewTransition / browser View Transitions API** | Rejected correctly: snapshot/rasterized model can't grow a real `box-shadow`. Confirmed independently below. |
| 7 | **FormKit AutoAnimate** | Zero-config add/remove/move only; no `box-shadow`, no depth layers, no routes. Wrong tool. |
| 8 | **MudBlazor / FluentUI / Radzen built-in animations** | Component-internal show/hide easing only. No cross-route, no depth-layer, no custom `box-shadow` choreography. |
| 9 | **Telerik AnimationContainer / Blazor.Animate / BlazorAnimation (animate.css)** | Preset fade/slide/zoom on a single container. No shadow keyframing, no depth ripple, no route orchestration. |

---

## Scorecard against the 4 needs

Legend: ✅ does it · ⚠️ partial / with effort · ❌ no

| Option | (1) Container reuse | (2) Depth-layered stagger | (3) Real box-shadow anim | (4) Real routes | Blazor story |
|--------|:---:|:---:|:---:|:---:|---|
| GSAP + Flip | ❌ build it | ❌ build it | ✅ `props:"boxShadow"` | ❌ build it | JS interop wrapper |
| Motion (motion.dev) | ❌ build it | ❌ build it | ✅ animates box-shadow | ❌ build it | JS interop wrapper |
| anime.js v4 | ❌ build it | ❌ build it | ✅ (v4 fix) | ❌ build it | JS interop wrapper |
| BlazorTransitionableRoute | ❌ | ❌ | ❌ (no anim at all) | ✅ | Native Razor; .NET 6 |
| Framer Motion | ⚠️ (React) | ✅ (variant inheritance) | ✅ | ⚠️ (React routers) | ❌ React only |
| View Transitions API | ❌ | ❌ | ❌ (snapshot) | ✅ (cross-doc) | Toolbelt wrapper |
| FormKit AutoAnimate | ❌ | ❌ | ❌ | ❌ | interop possible |
| MudBlazor/Fluent/Radzen | ⚠️ per-component | ❌ | ❌ | ❌ | native |
| Telerik/Blazor.Animate | ⚠️ one container | ❌ | ❌ (presets) | ❌ | native |

The column that kills every off-the-shelf option is **(2)**: not one library
schedules animation by *nesting depth* of a component tree. The closest is Framer
Motion's variant inheritance (parent's `staggerChildren` cascades to children),
which is conceptually our ripple — but it's React and inheritance is per-parent,
not a global depth-bucketed timeline. We build (2) ourselves.

---

## Detail & evidence per option

### 1. GSAP + Flip plugin — best engine
- **box-shadow:** Flip records transforms/width/height/opacity by default but
  takes a `props` list of camelCased CSS props (e.g. `props:"boxShadow"`) to
  keyframe arbitrary properties.
  https://gsap.com/docs/v3/Plugins/Flip/
- **nested + stagger:** `nested:true` prevents parent/child offset compounding;
  one Flip can stagger multiple elements; full GSAP timeline control
  (`onComplete`, `add()`) — enough to gate melt→load→extrude.
  https://gsap.com/docs/v3/Plugins/Flip/ ·
  https://css-tricks.com/gsap-flip-plugin-for-animation/
- **License (decisive change):** As of **April 2025** Webflow made **all of GSAP,
  including previously paid Club plugins (Flip, SplitText, MorphSVG, etc.), 100%
  free for commercial use.** Not OSI-MIT but a no-charge standard license.
  https://webflow.com/blog/gsap-becomes-free ·
  https://css-tricks.com/gsap-is-now-completely-free-even-for-commercial-use/ ·
  https://gsap.com/pricing/
- **Gaps for us:** no Blazor wrapper, no container-component model, no
  depth-layer scheduler, no route-overlap. All ours.
- **Blazor interop is a known path** (community confirms GSAP-from-Blazor works;
  strongly-typed C# wrappers over `IJSRuntime` are the standard pattern).
  https://gsap.com/community/forums/topic/44038-blazor/ ·
  https://github.com/AdrienTorris/awesome-blazor/issues/448

### 2. Motion / Motion One (motion.dev) — MIT alternative
- **MIT, irrevocable**, hybrid engine (WAAPI + JS), used by Framer/Figma.
  https://motion.dev/ · https://github.com/motiondivision/motion
- **box-shadow:** animates complex multi-number/color strings like `box-shadow`
  (docs note `filter: drop-shadow(...)` is cheaper for perf, but box-shadow is
  supported). https://motion.dev/docs/animate
- **stagger:** flat distribution with `first/center/last`/index + easing — **no
  depth/hierarchy mode** (confirmed on the stagger page). So need (2) is still
  ours. https://motion.dev/docs/stagger
- Pick over GSAP if you want MIT + smaller bundle and don't need Flip's FLIP
  layout-diffing (we're driving box-shadow/scale, not arbitrary layout reflow).

### 3. anime.js v4 — MIT, viable
- MIT; v4 added timeline stagger and **fixed box-shadow complex-value handling**
  (was being mis-parsed as a color). Solid third option, fewer FLIP niceties.
  https://github.com/juliangarnier/anime/wiki/What's-new-in-Anime.js-V4 ·
  https://animejs.com/documentation/utilities/stagger/

### 4. BlazorTransitionableRoute — owns only need (4)
- Keeps the **previous and current route alive simultaneously** so you can
  transition out/in across a real navigation; exposes hooks (`Backwards`,
  `FirstRender`, `RouteData`/`SwitchedRouteData`, `TransitionType`) but
  **"does not provide animation styles."** Targets **.NET 6** (we're .NET 10).
  https://github.com/JByfordRew/BlazorTransitionableRoute ·
  https://www.nuget.org/packages/BlazorTransitionableRoute
- **Use as a reference** for how to keep two routes mounted during the swap; we
  likely re-author this seam in .NET 10 rather than depend on a .NET 6 package.

### 5. Framer Motion — concept only (React)
- `staggerChildren` + `delayChildren` on a parent variant cascade down nested
  `motion` children, children inherit variant names → layered reveal without
  per-child wiring. This is the *mental model* for our depth ripple. React-only,
  so not adoptable.
  https://www.framer.com/motion/stagger/ ·
  https://www.hemantasundaray.com/blog/staggerchildren-framer-motion

### 6. View Transitions API — independently confirmed unfit
- The API **rasterizes snapshots** of old/new states into a pseudo-element tree
  (`::view-transition-old/new/group`); animations apply to those **snapshots**
  (position/size/opacity), not to live CSS. Properties like `font-size` — and by
  the same mechanism **`box-shadow`** — can't truly animate because the element
  is a flat image during the transition. This matches our rejection rationale:
  it **cannot grow a real box-shadow**.
  https://developer.chrome.com/blog/view-transitions-in-2025 ·
  https://developer.mozilla.org/en-US/docs/Web/API/View_Transition_API/Using ·
  https://www.smashingmagazine.com/2023/12/view-transitions-api-ui-animations-part1/
- `Toolbelt.Blazor.ViewTransition` is just a Blazor router wrapper over this API,
  so it inherits the same limitation.
  https://github.com/jsakamoto/Toolbelt.Blazor.ViewTransition

### 7–9. AutoAnimate / component-lib transitions / preset containers
- **FormKit AutoAnimate:** auto-animates only add/remove/move of immediate
  children; no box-shadow, no depth, no routes.
  https://auto-animate.formkit.com/ · https://github.com/formkit/auto-animate
- **MudBlazor/FluentUI/Radzen:** component-internal enter/leave easing; not a
  cross-route shadow choreography engine.
  https://mudblazor.com/api/Animation
- **Telerik AnimationContainer / Blazor.Animate / BlazorAnimation:** single-
  container preset fade/slide/zoom (often animate.css). No shadow keyframing,
  depth ripple, or route orchestration.
  https://docs.telerik.com/blazor-ui/components/animationcontainer/overview ·
  https://github.com/mikoskinen/Blazor.Animate ·
  https://github.com/aboudoux/BlazorAnimation

---

## Final recommendation

**Build-our-own, engine-assisted.** Concretely:

- **Adopt an animation engine for need (3) only** — first choice **GSAP + Flip**
  (now free, richest box-shadow/nested/stagger primitives, proven Blazor interop);
  acceptable MIT alternative **Motion (motion.dev)** if license/bundle weight is
  the priority. Wrap it in a strongly-typed C# `IJSRuntime` service behind a seam,
  consistent with our DI-over-statics and "fakes are first-class" rules.
- **Build need (1)** — the base "shape" components (card, etc.) that own the
  animation contract so callers only pour in data.
- **Build need (2)** — the **depth-layer scheduler**: walk/annotate the container
  tree by nesting depth, bucket elements into depth bands, and drive a single
  timeline with per-band delays (melt all depth-0 → wait → depth-1 → …). Nobody
  ships this; it is the core IP. Mirror Framer Motion's variant-cascade model
  conceptually, implemented as our own depth-bucketed timeline.
- **Build/port need (4)** — keep previous+current route mounted across a real
  navigation. Reference `BlazorTransitionableRoute`'s two-route-alive technique,
  but re-author for .NET 10 rather than take the .NET 6 dependency.
- The async-gated orchestration (melt → await content load → extrude) falls out
  naturally from a GSAP/Motion timeline with an `await`-able promise between
  phases.

**Why not adopt something whole:** every candidate fails need (2), and most also
fail (1) or (3). The unique value — depth-layered structural ripple over reusable
shape containers across real routes — does not exist off the shelf. We adopt an
engine to avoid hand-writing a tween loop; we build the orchestration that makes
it *our* transition.
