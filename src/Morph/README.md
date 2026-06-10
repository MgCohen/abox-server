# Morph

A theme-agnostic Blazor WASM animation/transition library. One phase engine
(`exit → await load → enter`, driven by the browser's `animationend`), two
triggers on it (`MorphStage<TKey>` for in-page swaps, `MorphRouteStage` for
routes), and **one component per visual style** so callers never write animation
or type a raw style class.

Layout mirrors that split:

```
Engine/   the phase machine (MorphStageBase + the two triggers), MorphShape,
          interop, options, the TransitionDefinition record
Surface/  MorphStyle (the closed enum) + MorphSurface (type-safe dispatch)
Styles/   one folder per style: <X>/Morph<X>.razor + <X>Style.cs (values + AddX)
wwwroot/  theme.css + morph.css (engine) + one stylesheet per style
```

## Setup

```csharp
builder.Services.AddMorph(o => o.LoadTimeout = 10_000);   // composes raised + inset + cutout

var host = builder.Build();
await host.Services.DetectReducedMotionAsync();           // one matchMedia read; see "Reduced motion"
await host.RunAsync();
```

Reference the bundled CSS and register the completion event before Blazor starts:

```html
<link rel="stylesheet" href="_content/Morph/theme.css" />
<link rel="stylesheet" href="_content/Morph/raised.css" />
<link rel="stylesheet" href="_content/Morph/inset.css" />
<link rel="stylesheet" href="_content/Morph/cutout.css" />
<link rel="stylesheet" href="_content/Morph/morph.css" />

<script src="_framework/blazor.webassembly.js" autostart="false"></script>
<script type="module">
    import { registerMorphEvents } from "./_content/Morph/morph.js";
    registerMorphEvents();   // see "How a phase completes"
    Blazor.start();
</script>
```

`theme.css` is one theme (design tokens + the `@property --lift` registration);
`morph.css` is the engine (stage layout); each **style** ships its own stylesheet
(class + keyframes + the phase rules that key off `data-phase`). Swap the theme
without touching the engine.

## Using a style

Every style is a thin component over `MorphShape`; the call site is uniform no
matter how gnarly the style's internals are. Nest freely — each renders a
`.morph-item` and joins the depth-staggered ripple:

```razor
<MorphRaised Class="card">
    <MorphInset Class="inner">…</MorphInset>
</MorphRaised>

<MorphCutout Class="panel">…</MorphCutout>   @* a hole that opens from a point *@
```

`Class` is a **size/role modifier** (`card`, `tile`, `wide`) — never a `neu-*`
identity class, which the component owns. Each style brings its own motion
(raised rises from flat, inset presses in, cut-out opens from a point); they are
independent, not poles of one transition.

### Variable style — `MorphSurface`

When a single spot needs a *changeable* look, `MorphSurface` maps a `MorphStyle`
to the right component via a compile-checked switch expression (not
`DynamicComponent` — the set is closed and homogeneous):

```razor
<MorphSurface Style="@_style" Class="card">…</MorphSurface>
```

## Adding a style

A new style is one self-contained folder under `Styles/` plus two one-line edits:

1. `Styles/<X>/Morph<X>.razor` — wrap `MorphShape` with the style's class (+ any
   scaffold divs the look needs; see the cut-out).
2. `wwwroot/<x>.css` — the `.neu-<x>` recipe, its `@keyframes`, and the phase
   rules: `.morph-stage[data-phase="exit"|"enter"] .neu-<x> { animation: … }`.
3. `Styles/<X>/<X>Style.cs` — a `TransitionDefinition` (timing/easing only; emits
   namespaced CSS vars like `--<x>-exit-dur`) and an `AddX()` extension that calls
   `AddTransition(...)`.
4. Add the enum value to `MorphStyle` and a switch arm to `MorphSurface`; compose
   `AddX()` into `AddMorph`.

The stage writes **every** registered style's vars onto itself (`Options.AllVars`)
and only flips `data-phase` — motion lives entirely in each style's CSS.

## How a phase completes

A phase ends when every animated layer has finished — driven by real
`animationend` events, not a timer. Each `MorphShape` renders a `.morph-item`; a
style's CSS animates the item under a transitioning stage, so the stage waits for
exactly one `animationend` **per `.morph-item`**. The count is read once per phase
from the DOM (`countItems`), and bubbling events tick it down — which is what lets
an outer `MorphRouteStage` wait for items owned by inner stages.

Three pieces make this robust and are easy to miss:

- **Target filtering.** The stage counts only events whose original target *is* a
  `.morph-item`, ignoring animations on descendant layers and on screen content.
  Blazor's `[EventHandler]` for `animationend` yields a bare `EventArgs` (no
  target), so `morph.js#registerMorphEvents` registers a custom `morphend` event
  (→ `animationend`) whose `createEventArgs` reports `isItem`. The stage binds
  `@onmorphend`; `EventHandlers.cs` (named exactly that, for the Razor compiler)
  declares it. This is why setup calls `registerMorphEvents()` before
  `Blazor.start()`.
- **The sentinel pattern.** A style whose visible motion runs on *inner* layers
  (the cut-out animates a clip-masked floor + a depth ring, because `clip-path`
  would clip a pseudo-element) gives its `.morph-item` one duration-matched no-op
  animation (`cutout-life`), so it still emits exactly one counting `animationend`
  while the inner layers' events are filtered out.
- **No fallback timer.** If the gate could never be satisfied the phase would
  hang, so the no-animation cases short-circuit explicitly: reduced motion (below)
  and a zero-item count complete immediately.

## Composing stages

`MorphStage<TKey>` takes `ChildContent` (the screen), `LoadingContent` (overlay
held during an async gate), and `ErrorContent` (a `RenderFragment<Exception>`
shown when a load times out):

```razor
<MorphStage Screen="_id" Load="LoadAsync">
    <ChildContent Context="id"><RunDetail Id="id" /></ChildContent>
    <LoadingContent><Spinner /></LoadingContent>
    <ErrorContent Context="ex"><div class="error">@ex.Message</div></ErrorContent>
</MorphStage>
```

For a genuinely new trigger (neither param-watch nor router), subclass
`MorphStageBase` and drive `RunPhaseAsync` / `TransitionAsync` yourself — the same
engine the two built-in triggers use.

## Reduced motion

Each style's CSS zeroes its animation under `prefers-reduced-motion: reduce`, so
no `animationend` ever fires. `DetectReducedMotionAsync` reads the media query
once at startup and sets `MorphOptions.ReducedMotion`, which makes the engine skip
its await entirely — CSS and engine stay in agreement instead of waiting for
events that never arrive. Call it once after `Build()`. This and the per-phase
item count are the library's only JS interop, both through `morph.js` via
`MorphInterop`.
