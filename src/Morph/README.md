# Morph

A theme-agnostic Blazor WASM animation/transition library. One phase engine
(`exit → await load → enter`), two triggers on it (`MorphStage<TKey>` for in-page
swaps, `MorphRouteStage` for routes), and **one component per visual style** so
callers never write animation or type a raw style class.

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
builder.Services.AddMorph(o =>
{
    o.LoadTimeout = 10_000;   // async-gate budget (ms) before a load is treated as failed
    o.SwapDelay = 0;          // optional hold (ms) at the empty midpoint, between exit and enter
});   // composes raised + inset + cutout

var host = builder.Build();
await host.Services.DetectReducedMotionAsync();           // one matchMedia read; see "Reduced motion"
await host.RunAsync();
```

Reference the bundled CSS from `index.html` (no JS wiring needed):

```html
<link rel="stylesheet" href="_content/Morph/theme.css" />
<link rel="stylesheet" href="_content/Morph/raised.css" />
<link rel="stylesheet" href="_content/Morph/inset.css" />
<link rel="stylesheet" href="_content/Morph/cutout.css" />
<link rel="stylesheet" href="_content/Morph/morph.css" />
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
   scaffold divs the look needs; see the cut-out), and project its timing:
   `Vars="@Options.Resolve(<X>Style.Name).Vars"`.
2. `wwwroot/<x>.css` — the `.neu-<x>` recipe, its `@keyframes`, and the phase
   rules: `.morph-stage[data-phase="exit"|"enter"] .neu-<x> { animation: … var(--enter-dur) … }`.
3. `Styles/<X>/<X>Style.cs` — a `TransitionDefinition` (timing/easing only; its
   `Vars` emit generic `--enter-dur`/`--exit-dur`/`--layer`/`--scatter`/eases) and
   an `AddX()` that calls `AddTransition(...)`.
4. Add the enum value to `MorphStyle` and a switch arm to `MorphSurface`; compose
   `AddX()` into `AddMorph`.

**Timing is per-style, per-subtree.** Each style component projects its own
`TransitionDefinition.Vars` onto the `.morph-item` it renders (via a `MorphShape`
`Vars` passthrough), so `var(--enter-dur)` resolves to *that style's* value even
when a single stage mixes raised + inset + cut-out. The stage emits only
`--max-depth`.

## How a phase completes

A phase ends when the animations it started have finished — and the engine waits
on the **actual scheduled set**, not a count or a timer:

```js
// morph.js — the only completion primitive
export function waitForAnimations(el) {
  return new Promise((resolve) => requestAnimationFrame(() => {
    const anims = el.getAnimations({ subtree: true })
      .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity); // skip infinite
    resolve(Promise.all(anims.map((a) => a.finished)));
  }));
}
```

`MorphStageBase` awaits `Interop.WaitForAnimationsAsync(StageElement)` once per
phase (after the phase's render, via `OnAfterRenderAsync`). This is **correct by
construction** for any delay, gap, nesting, or count — a style just declares its
keyframes and the engine waits for exactly what runs. Consequences:

- **Styles never touch completion.** The cut-out's motion lives on inner
  `floor`/`ring` layers, not its `.morph-item`; the engine waits for those layers
  with no sentinel, count, or special-casing.
- **Interruption** cancels the in-flight animations, so `.finished` rejects with
  `AbortError` → `JSException`; the engine catches it and the `_phaseGen`
  generation check discards the superseded phase.
- **No silent hang.** An empty set resolves instantly (static phase, reduced
  motion), so there is no count to mismatch.

This is the library's only JS beyond `prefersReducedMotion` — JS is admitted only
for a browser primitive C#/CSS can't express ("this subtree's animations are
done"). All phase logic, dispatch, timing, and config stay in C#/CSS.

## Reduced motion

Each style's CSS zeroes its animation under `prefers-reduced-motion: reduce`.
`DetectReducedMotionAsync` reads the media query once at startup and sets
`MorphOptions.ReducedMotion`, which makes the engine skip its await entirely. Call
it once after `Build()`. (Even without the short-circuit, `getAnimations` would
return an empty set and resolve instantly — the two agree.)

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
