# Morph

A theme-agnostic Blazor WASM animation/transition library. One phase engine
(`exit → await load → enter`, driven by the browser's `animationend`), two
triggers on it (`MorphStage<TKey>` for in-page swaps, `MorphRouteStage` for
routes), and one shape (`MorphShape`) that owns its transition so callers never
write animation.

## Setup

```csharp
builder.Services.AddMorph();                 // ships the built-in "morph" transition

var host = builder.Build();
await host.Services.DetectReducedMotionAsync();   // one matchMedia read; see "Reduced motion"
await host.RunAsync();
```

Reference the bundled CSS from `index.html`:

```html
<link rel="stylesheet" href="_content/Morph/theme.css" />
<link rel="stylesheet" href="_content/Morph/raised.css" />
<link rel="stylesheet" href="_content/Morph/inset.css" />
<link rel="stylesheet" href="_content/Morph/morph.css" />
```

`morph.css` is the engine (stage rules + reduced-motion + the `morph-melt`/
`morph-strude` keyframes that drive the `--lift` variable); `theme.css` is one
theme (tokens + the `@property --lift` registration); each style ships its own
lift-driven shadow recipe (`raised.css`, `inset.css`). Swap the theme without
touching the engine.

The built-in "morph" is a **melt & extrude**: a panel's fill matches the surface,
so its shadow is the only thing that makes it visible. Melt animates `--lift` → 0
(the panel sinks flush and dissolves into the clay); extrude animates `--lift`
0 → 1 with an overshoot ease (it squeezes back out). Every shadow offset and blur
is `calc(var(--lift) * …)`, so at `--lift: 0` the panel is genuinely shadowless.

## The four extension axes

Everything below is a **consumer** action — none of it edits this library.

### 1. Change configs — `AddMorph` options

`MorphOptions` is the DI singleton holding the registered transition set and the
global knobs:

```csharp
builder.Services.AddMorph(o =>
{
    o.Default     = "morph";   // transition used when a stage names none
    o.LoadTimeout = 10_000;    // ms async-gate budget → error path on expiry
});
```

### 2. Control transitions — `TransitionDefinition` + keyframe pair + `Transition="name"`

A transition *type* is data. Register it, ship its keyframe pair in **your** CSS,
and select it by name:

```csharp
//          name     exit-kf       enter-kf       exit  enter  layer  scatter  exit-ease                     enter-ease
builder.Services.AddMorph(o => o.Add(new TransitionDefinition(
    "slide", "slide-exit", "slide-enter", 300, 340, 60, 20,
    "cubic-bezier(0.4,0,0.2,1)", "cubic-bezier(0.4,0,0.2,1)")));
```

`layer` is the per-**depth** stagger step (ms): nested panels cascade layer by
layer — exit melts deepest-first (leaves sink before their container), enter
extrudes outermost-first (each layer surfaces in turn). `scatter` adds a small
stable per-container jitter (ms) so same-layer siblings aren't in lockstep. Exit
and enter take their own eases — the built-in morph pairs an ease-in collapse with
an overshoot extrude.

```css
/* in the consumer's CSS — animate transform/opacity, disjoint from a per-shape extra */
@keyframes slide-exit  { to   { transform: translateX(-40px); opacity: 0 } }
@keyframes slide-enter { from { transform: translateX(40px);  opacity: 0 } }
```

```razor
<MorphStage Screen="_view" Transition="slide" Context="v"> … </MorphStage>
<MorphRouteStage Body="@Body" Transition="slide" />
```

`Transition` is a normal parameter — bind it to switch at runtime. Adding a type
touches no library code and adds no branch.

### 3. Add shapes — `<MorphShape Class="…">` + CSS

`MorphShape` is the one container. A new variant is a CSS class; the component is
unchanged. Nest them freely — each renders a `.morph-item` and joins the ripple:

```razor
<MorphShape Class="neu-raised card">
    <MorphShape Class="neu-inset inner">…</MorphShape>
</MorphShape>
```

Each shape emits `--depth` (its nesting level) and `--rand` (a stable scatter
hash); the stage rule turns `--depth` (against the stage's `--max-depth`) into a
layer-by-layer `animation-delay`, with `--rand` adding the within-layer jitter —
so nested panels peel depth by depth rather than all at once.

A per-shape extra animation must use **disjoint** properties (`filter`, `color`,
…) from the base keyframes (`--lift`/`box-shadow`/`opacity`) — same-property
collisions resolve silently by last-declared-wins.

### 4. Add components — RenderFragments into a stage, or subclass `MorphStageBase`

`MorphStage<TKey>` takes `ChildContent` (the screen), `LoadingContent` (held-melted
overlay), and `ErrorContent` (a `RenderFragment<Exception>` shown when a load
times out). Pour any component in:

```razor
<MorphStage Screen="_id" Load="LoadAsync">
    <ChildContent Context="id"><RunDetail Id="id" /></ChildContent>
    <LoadingContent><Spinner /></LoadingContent>
    <ErrorContent Context="ex"><div class="error">@ex.Message</div></ErrorContent>
</MorphStage>
```

For a genuinely new trigger (neither param-watch nor router), subclass
`MorphStageBase` and drive `RunPhaseAsync` / `TransitionAsync` yourself — the same
engine `MorphStage` and `MorphRouteStage` use.

## How a phase completes

A phase ends when every animated layer has finished — driven by real
`animationend` events, not a timer. Each `MorphShape` renders a `.morph-item`;
the CSS animates **every** item under a transitioning stage (descendant selector),
so the stage waits for exactly one `animationend` per item. The count is read once
per phase from the DOM (`countItems`), and the bubbling events tick it down — which
is what lets an outer `MorphRouteStage` wait for items owned by inner stages.

Two pieces make this work and are easy to miss:

- `EventHandlers.cs` registers `onanimationend` via `[EventHandler]`. Animation and
  transition events are **not** in Blazor's built-in set, so without this class the
  `@onanimationend` binding is silently inert — the handler never fires. The class
  must be named exactly `EventHandlers` for the Razor compiler to find it.
- No fallback timer. If the gate could never be satisfied the phase would hang, so
  the two no-animation cases short-circuit explicitly: reduced motion (below) and a
  zero-item count complete immediately.

## Reduced motion

CSS zeroes the animation under `prefers-reduced-motion: reduce`, so no
`animationend` ever fires. `DetectReducedMotionAsync` reads the media query once at
startup and sets `MorphOptions.ReducedMotion`, which makes the engine skip its await
entirely — CSS and engine stay in agreement instead of waiting for events that will
never arrive. Call it once after `Build()`. This and the per-phase item count are
the library's only JS interop, both through `morph.js` via `MorphInterop`.
