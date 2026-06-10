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
<link rel="stylesheet" href="_content/Morph/neu.css" />
<link rel="stylesheet" href="_content/Morph/morph.css" />
```

`morph.css` is the engine (stage rules + reduced-motion); `neu.css` is one theme
(tokens + the `morph-exit`/`morph-enter` keyframes). Swap the theme without
touching the engine.

## The four extension axes

Everything below is a **consumer** action — none of it edits this library.

### 1. Change configs — `AddMorph` options

`MorphOptions` is the DI singleton holding the registered transition set and the
global knobs:

```csharp
builder.Services.AddMorph(o =>
{
    o.Default     = "morph";   // transition used when a stage names none
    o.Ceiling     = 1200;      // ms safety cap per phase if an animationend is missed
    o.LoadTimeout = 10_000;    // ms async-gate budget → error path on expiry
});
```

### 2. Control transitions — `TransitionDefinition` + keyframe pair + `Transition="name"`

A transition *type* is data. Register it, ship its keyframe pair in **your** CSS,
and select it by name:

```csharp
builder.Services.AddMorph(o => o.Add(new TransitionDefinition(
    "slide", "slide-exit", "slide-enter", 300, 340, 90, "cubic-bezier(0.4,0,0.2,1)")));
```

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
unchanged. Nesting is the depth model — children animate one band deeper:

```razor
<MorphShape Class="neu-raised card">
    <MorphShape Class="neu-inset inner">…</MorphShape>
</MorphShape>
```

A per-shape extra animation must use **disjoint** properties (`filter`, `color`,
…) from the base keyframes (`transform`/`opacity`/`box-shadow`) — same-property
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

## Reduced motion

CSS zeroes the animation under `prefers-reduced-motion: reduce`, so no
`animationend` ever fires. `DetectReducedMotionAsync` reads the same media query
once at startup (the library's only JS interop) and sets `MorphOptions.ReducedMotion`,
which makes the engine skip its await — CSS and engine stay in agreement instead
of stalling for the full `Ceiling` per phase. Call it once after `Build()`.
