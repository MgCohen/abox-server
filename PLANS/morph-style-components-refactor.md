# Morph style-components refactor

**Status:** reviewed + revised (not started) · **Scope:** `src/Morph` + its consumers · **Owner:** —

This document is standalone. It assumes no prior conversation. Read it cold and
you should understand what Morph is today, what we're changing, why, and exactly
what the target looks like.

> **Revision note (post-review).** Four conflicts surfaced when the design was
> checked against the actual engine; their resolutions are folded into the
> decisions below and supersede the first draft. **A and B (the runtime-risky ones)
> are spike-validated** (`spikes/morph-getanimations/`, `spikes/morph-completion-cascade/`);
> **C and D are pure-refactor decisions with no runtime risk to spike.**
> - **A (completion, D6).** The original counted model has no timer fallback, so a
>   miscount hangs *silently* — and the cut-out's inner-layer animations make a
>   correct count hard. Several C#-only fixes were explored (target-filter — not
>   expressible, Blazor's `AnimationEventArgs` carries no DOM target; a named
>   sentinel animation; balancing `animationstart`/`animationend`) and each carries
>   a fragility (sentinel must be hand-sized ≥ its content; balance-counting
>   false-completes on *gapped* staggers — proven, it fires at 273ms on a layer
>   that truly ends at 873ms). Resolved instead with a **tiny JS seam**:
>   `element.getAnimations({subtree:true})` + `Promise.all(...finished)`, awaited
>   from C#. It waits on the *actual* scheduled animation set — any delay, gap,
>   nesting, or count — so completion is **correct by construction**. This retires
>   the silent-hang risk and deletes the sentinel/counting machinery entirely.
>   (Resolves Q1; see **D7** for the principle that admits the JS.)
> - **B (timing scope, D2/D5).** Durations are emitted once on `.morph-stage`, but
>   a single stage mixes styles (the demo nests raised+inset) and the cut-out's
>   timing differs (700/1100 vs 440/500). Resolved: **each style component emits
>   its own timing vars onto its `.morph-item`** (via a `MorphShape` passthrough);
>   `var(--enter-dur)` then resolves per-subtree. The stage keeps only `--max-depth`.
> - **C (dead registry, D4).** The `MorphStyle → Type` map has no consumer — the
>   switch references component tags at compile time. Resolved: **drop
>   `AddMorphStyle`**; each style registers only its `TransitionDefinition`.
>   (Resolves Q2 by removal.)
> - **D (record shape, D5).** The single `Exit/EnterKeyframes` fields can't name a
>   multi-layer style (cut-out needs floor+ring) and go unused once CSS owns
>   motion. Resolved: **drop the keyframe-name fields**; each style names its
>   keyframes literally in its own CSS. Easing fields stay (default `linear`).

---

## 1. What Morph is

Morph is a theme-agnostic Blazor WASM animation library. It orchestrates a single
transition lifecycle — **`exit → (await load) → enter`** — and exposes a small
surface for building animated container UI without callers writing animation code.

The pieces today (`src/Morph`):

- **Engine** — `MorphStageBase` drives the phase machine; `MorphStage<TKey>`
  (in-page swaps) and `MorphRouteStage` (router) are the two triggers on it.
- **Shape** — `MorphShape` is the one container. A visual variant is **a CSS
  class** (`neu-raised`, `neu-inset`); nesting is the depth model.
- **Transition** — `TransitionDefinition` is data (keyframe names + durations +
  easing). The stage picks one **by name** via the `Transition` parameter.
- **Completion** — counted via `animationend`. `morph.js#countItems` returns
  `stage.querySelectorAll(".morph-item").length`; every bubbled `animationend`
  increments a tally; the phase resolves when the tally reaches the count.
  **There is no timer fallback** — a miscount hangs the phase. *(This counted model
  is exactly what D6 replaces — it's described here as today's behavior, not the target.)*

### Current state of the relevant types (accurate as of writing)

```csharp
// TransitionDefinition.cs — the generic record (timing/easing data)
public sealed record TransitionDefinition(
    string Name, string ExitKeyframes, string EnterKeyframes,
    int ExitMs, int EnterMs, int LayerInterval, int Scatter,
    string ExitEase, string EnterEase);

// ServiceCollectionExtensions.cs — the one built-in transition
new("morph", "morph-melt", "morph-strude", 440, 500, 150, 30,
    "cubic-bezier(0.52, 0, 0.74, 0.25)", "cubic-bezier(0.34, 1.25, 0.64, 1)");
```

```css
/* morph.css — the engine binds the stage's transition vars onto every .morph-item */
.morph-stage[data-phase="exit"]  .morph-item { animation: var(--anim-exit)  var(--exit-dur)  var(--exit-ease)  both; }
.morph-stage[data-phase="enter"] .morph-item { animation: var(--anim-enter) var(--enter-dur) var(--enter-ease) both; }
```

Consumers today write a shape as **one tag + a class string** — zero scaffold:

```razor
<MorphShape Class="neu-inset tile"> … </MorphShape>
<MorphShape Class="neu-raised card"> … <MorphShape Class="neu-inset inner"> … </MorphShape> </MorphShape>
```

---

## 2. Why change anything

Two forces:

1. **A new style — the cut-out** (a panel that reads as a hole punched into the
   surface; it *opens* from a point and *closes* back to nothing). It was
   prototyped and tuned in `spikes/cutout-demo/`. Unlike raised/inset, its depth
   needs **two sibling layers** — a clip-masked `floor` (content) and a
   depth-bearing `ring` — because `clip-path` clips an element's pseudo-elements,
   so the depth can't hide in a `::before`. It therefore **cannot** be expressed
   as "just a class."

2. **A standard.** This is an agent-first repo: structure over prose, fewest
   moving pieces at the consumer, nuance hidden behind components. The cut-out
   forces a decision we don't currently have to make anywhere, and we want to make
   it once, as a rule that holds for *every* style — present and future.

---

## 3. Decisions (the rules)

These are the settled rules. Each is a "what controls what" boundary.

### D1 — One component per style. Never inline scaffold.

`MorphRaised`, `MorphInset`, `MorphCutout`, and every future style get their own
component. Adding a style = adding a component, full stop. Consumers never type a
`neu-*` class or a scaffold `<div>`. The call site is uniform regardless of how
gnarly the style's internals are:

```razor
<MorphRaised Class="card"> … </MorphRaised>
<MorphInset  Class="tile"> … </MorphInset>
<MorphCutout Class="panel"> … </MorphCutout>
```

Rationale: today raised/inset are bare classes (0 scaffold divs); the cut-out
needs 2 load-bearing divs. Rather than let *one* style break the "just a class"
convention at every call site (and silently break depth if someone forgets a
div), we make **every** style a thin component and pay the scaffold cost once,
inside the component.

### D2 — Each style is independent; motion belongs to the style.

Raised ≠ inset ≠ cut-out. They are not opposite poles of one transition — they
are three separate things, each with its own in/out:

- **raised** rises from flat, sinks back to flat
- **inset** presses into the surface, returns to flat
- **cut-out** opens from a point, closes back to nothing

All start and end at the same neutral (empty) state; content lives in the middle,
unaffected. **Consequence:** motion moves *from the stage onto the style.* Today
the stage's one `Transition` is applied to every `.morph-item` (so an inset
physically animates with the *raised* keyframes). Going forward, each style's CSS
keys its own enter/exit off its class, and the stage's job narrows to flipping
`data-phase` and resolving completion.

### D3 — Per-style co-location (vertical slice).

Everything a style *is* lives in one folder: its component (+ scaffold), its CSS
(class + keyframes), and its timing values (+ self-registration). To know
everything the cut-out does, you open `Styles/Cutout/`. Generic machinery (the
phase engine, the `TransitionDefinition` *record*, the registry) is shared in
`Engine/`. (CSS-isolation filing is a non-issue here — the UI layer is slated to
move to its own repo under different rules later, so global CSS living beside a
style is acceptable.)

### D4 — Variable style via a type-safe switch expression, not `DynamicComponent`.

Some components legitimately need a *changeable* look (e.g. a tile that is inset
at rest and a cut-out when opened). The mechanism is a `MorphSurface` dispatcher
that maps a `MorphStyle` enum to the per-style component via a **switch
expression returning `RenderFragment`** — a type-safe map:

```razor
@* Surface/MorphSurface.razor — the dispatch. Map-shaped, compile-checked, no magic strings. *@
@(Style switch
{
    MorphStyle.Cutout => CutoutFrag,
    MorphStyle.Inset  => InsetFrag,
    _                 => RaisedFrag,
})

@code {
    [Parameter] public MorphStyle Style { get; set; } = MorphStyle.Raised;
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private RenderFragment RaisedFrag => @<MorphRaised Class="@Class">@ChildContent</MorphRaised>;
    private RenderFragment InsetFrag  => @<MorphInset  Class="@Class">@ChildContent</MorphInset>;
    private RenderFragment CutoutFrag => @<MorphCutout Class="@Class">@ChildContent</MorphCutout>;
}
```

**Why not `DynamicComponent`:** it shines when component types are *unknown at
build time* (plugins, server-driven UI). Ours is a small, closed, homogeneous set
(every style takes the same `Class` + `ChildContent`). `DynamicComponent` would
trade away compile-time safety — its parameters pass through a
`Dictionary<string, object>` of magic strings with no IntelliSense and no contract
enforcement, a deliberate design limitation per the Blazor team — to buy dynamism
we don't need. The switch expression keeps real component tags (compile-checked),
reads like a map, and the only central cost is **one line here per new style** —
worth it for the safety. (If styles ever become plugin-extensible, *that* is when
`DynamicComponent` earns its keep.)

The dispatch references components by name only — per-style scaffold/CSS/values
stay in each style's folder, so D3's locality is preserved.

**No runtime style registry.** The switch *is* the `MorphStyle → component` map,
resolved at compile time. There is deliberately **no** `MorphStyle → Type`
registry: nothing would consume it (the switch names component tags directly), and
a runtime Type map is exactly the `DynamicComponent` indirection this decision
rejects. Each style therefore self-registers only its **timing**
(`TransitionDefinition`), never its component type. (Conflict C.)

### D5 — What controls what (config layering).

| Concern | Lives in | Notes |
|---|---|---|
| **Motion *shape*** (the choreography: pop → hold → open) | CSS `@keyframes` in the style folder | The style's identity; not a per-instance knob. The cut-out's phased timing lives in multi-stop keyframes with per-stop `animation-timing-function`. |
| **Motion *timing/easing*** (durations, ease) | a `TransitionDefinition` instance in the style folder, **emitted by the style component onto its own `.morph-item`** | The single tuning point. Registered centrally; the style component projects its `Vars` onto the `.morph-item` it renders (via a `MorphShape` passthrough), so `var(--enter-dur)` resolves *per style/per subtree*. **The stage no longer emits `--*-dur`/`--*-ease`/`--anim-*`** — only `--max-depth`. This is what lets one stage mix raised+inset+cut-out, each with its own timing. (Conflict B.) |
| **Style *structure*** (which scaffold) | the style component (`MorphCutout.razor`) | Owns the divs; consumers never see them. |
| **Which style** (fixed or variable) | the product component (`Card`/`Panel`) | A fixed style is the default; a variable one is an optional `Style` param forwarded to `MorphSurface`. |
| **Size / layout** | a `Class` modifier + CSS (`card`, `tile`, `wide`) | Not animation; never C#. |
| **Per-instance override** (rare: "this one is slower") | an opt-in component param that sets a CSS var | Add only on the component that needs it (YAGNI), defaulting to the registered value. |
| **Global safety** (`LoadTimeout`, reduced-motion) | `MorphOptions` | Unchanged. |

The product component decides **structure + which style**; it stays free of
numbers. Timing is data; motion-shape is keyframes; size is CSS.

### D6 — Completion: await the actual animations via `getAnimations`. (critical)

**The problem with the inherited model.** Today completion *counts*: `countItems`
returns `.morph-item` count, every bubbled `animationend` increments a tally, the
phase resolves when they're equal — and there is **no timer fallback, so a
miscount hangs silently.** The cut-out breaks the count (its motion is on inner
`floor`/`ring` layers, not the `.morph-item`), and animated screen content
inflates it. Every C#-only fix re-encodes the same fragility somewhere:

- *Filter the tally by event `target`* — **impossible**: Blazor's
  `AnimationEventArgs` carries no DOM element (not serializable), so there's
  nothing to filter on.
- *Named `morph-sentinel` animation + `AnimationName` filter* — works, but the
  sentinel must be **hand-sized ≥ its subtree's longest motion** or the phase
  resolves early; a footgun on every future style.
- *Balance `animationstart`/`animationend`* — clean and number-free, but
  **false-completes on gapped staggers**: `animationstart` fires only *after*
  `animation-delay`, so a delayed layer is invisible until it starts. Proven in
  `spikes/morph-getanimations/`: the counter matched at **273ms** on a layer that
  truly ended at **873ms**. Only safe for *overlapping* cascades.

**Resolution — a tiny JS seam (admitted by D7).** CSS has no "subtree is done"
signal; the Web Animations API does. The engine awaits the real animation set:

```js
// morph.js — observes whatever is actually running; no count, no duration, no timer
export function waitForAnimations(el) {
  return new Promise((resolve) => requestAnimationFrame(() => {
    const anims = el.getAnimations({ subtree: true })
      .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity); // skip infinite
    resolve(Promise.all(anims.map((a) => a.finished)));
  }));
}
```

```csharp
// MorphStageBase — replaces the animationend tally + countItems + TCS dance
try { await Interop.WaitForAnimationsAsync(StageElement); }
catch (JSException) { /* AbortError: phase superseded mid-flight — _phaseGen handles it */ }
```

Why this wins: it waits on the *actual* scheduled set — any delay, gap, nesting,
data-driven count, or none — so completion is **correct by construction**. It
**deletes** the sentinel, the comma-list discipline, the `AnimationName` filter,
`countItems`, the `@onanimationend` binding, `EventHandlers.cs`, and the
`_animEnd`/`_target`/`_countScheduled`/`TaskCompletionSource` state. The cut-out
needs no special handling — its layers animate, the engine waits. **Styles stop
caring about completion entirely** (a footgun removed from every future style),
and the silent-hang risk is gone (empty set → resolves instantly; reduced-motion
falls out for free).

Validated in `spikes/morph-getanimations/` (6/6): gapped stagger waits correctly
(873ms), interruption rejects cleanly with `AbortError` (no hang), a fresh phase
recovers, a static phase resolves in 12ms. The three caveats handled in the
helper/engine: filter infinite animations, snapshot on the next frame (`rAF`),
catch `AbortError` as "superseded" (maps onto the engine's existing `_phaseGen`
interruption model). One thing to verify at build: `{subtree:true}` support on the
lowest target WebView (the base `getAnimations()` is broadly supported; the
`subtree` option shipped slightly later — MDN documents a descendant-map fallback).

### D7 — JS is the thin adapter for browser primitives C#/CSS can't express.

The seam in D6 establishes a rule, not a free-for-all. **JS is admitted only for
browser primitives that have no C#/CSS expression** (here: "all animations in this
subtree have finished" — CSS provably has no completion callback). Everything else
— phase logic, per-style timing, dispatch, config — stays in C#/CSS. Concretely
that means: no business logic in JS, no chatty per-frame round-trips, no
`DotNetObjectReference` callback graphs. The D6 helper is the canonical shape: a
stateless, promise-returning function, awaited once per phase, owning nothing.
`morph.js` keeps its one existing adapter of this kind (`prefersReducedMotion`);
`waitForAnimations` is the second, replacing the old `countItems`. This line is the
deliverable — it answers the *next* JS question too.

---

## 4. Target folder structure

Namespaces stay flat (`namespace Morph;`) — folders organize files, not
assemblies, consistent with the repo's "folders not walls" stance.

The two columns are **independent file lists** — read each top-to-bottom; rows do
*not* line up as a before→after mapping. The `←` annotations flag what changes in
the target.

```
BEFORE  (src/Morph, flat)              AFTER  (src/Morph, sliced)
────────────────────────────          ─────────────────────────────────────────
MorphStage.razor                       Engine/
MorphRouteStage.razor                    MorphStage.razor
MorphStageBase.cs                        MorphRouteStage.razor
MorphStageContext.cs                     MorphStageBase.cs        ← await WaitForAnimationsAsync; drop tally/TCS/count state (D6)
MorphOrderCounter.cs                     MorphStageContext.cs
MorphPhase.cs                            MorphOrderCounter.cs
MorphShape.razor                         MorphPhase.cs
MorphInterop.cs                          MorphShape.razor         ← +Vars passthrough onto .morph-item (conflict B)
MorphOptions.cs                          MorphInterop.cs          ← WaitForAnimationsAsync (replaces CountItemsAsync)
TransitionDefinition.cs                  MorphOptions.cs
ServiceCollectionExtensions.cs           TransitionDefinition.cs  ← shared RECORD, keyframe-name fields dropped (conflict D)
EventHandlers.cs  (deleted, see below)   ServiceProviderExtensions.cs
ServiceProviderExtensions.cs             wwwroot/morph.css        ← engine rules only (no sentinel)
wwwroot/morph.css                        wwwroot/morph.js         ← waitForAnimations (replaces countItems) (D6/D7)
wwwroot/morph.js                       Surface/
wwwroot/neu.css                          MorphStyle.cs            ← the enum (the one closed list)
                                         MorphSurface.razor       ← the type-safe dispatch (D4)
                                       Styles/
                                         Raised/
                                           MorphRaised.razor      ← component (0 scaffold divs)
                                           raised.css             ← .neu-raised + raise keyframes
                                           RaisedStyle.cs         ← TransitionDefinition values + AddRaised()
                                         Inset/
                                           MorphInset.razor
                                           inset.css
                                           InsetStyle.cs
                                         Cutout/                  ← NEW
                                           MorphCutout.razor      ← component + 2-layer scaffold
                                           cutout.css             ← .neu-cutout + cutout-open/close keyframes
                                           CutoutStyle.cs         ← TransitionDefinition values + AddCutout()
                                       ServiceCollectionExtensions.cs ← AddMorph() composes AddRaised()/AddInset()/AddCutout()
```

`neu.css`'s `:root` tokens move to a shared theme file; its `.neu-raised`/
`.neu-inset` rules move into the respective style folders. **`EventHandlers.cs` is
deleted** (it only registered the `@onanimationend` event, which `getAnimations`
makes unnecessary — D6), and `morph.js`'s `countItems` is removed in favor of
`waitForAnimations`.

---

## 5. Key snippets (target)

### Style component (cut-out — the only one with scaffold)

```razor
@* Styles/Cutout/MorphCutout.razor *@
<MorphShape Class="neu-cutout @Class">
    <div class="cutout-ring"></div>
    <div class="cutout-floor">@ChildContent</div>
</MorphShape>

@code {
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

```razor
@* Styles/Inset/MorphInset.razor — single-layer styles stay trivial *@
<MorphShape Class="neu-inset @Class">@ChildContent</MorphShape>

@code {
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

### The shared record (conflict D — keyframe names dropped)

```csharp
// TransitionDefinition.cs — timing + stagger + easing only. No keyframe NAMES:
// each style names its keyframes literally in its own CSS (a multi-layer style
// like the cut-out needs two — floor + ring — which one name field can't carry).
public sealed record TransitionDefinition(
    string Name, int ExitMs, int EnterMs, int LayerInterval, int Scatter,
    string ExitEase = "linear", string EnterEase = "linear")
{
    public string Vars =>
        $"--exit-dur:{ExitMs}ms;--enter-dur:{EnterMs}ms;" +
        $"--layer:{LayerInterval}ms;--scatter:{Scatter}ms;" +
        $"--exit-ease:{ExitEase};--enter-ease:{EnterEase};";
}
```

### Style values + self-registration (co-located with the component)

```csharp
// Styles/Cutout/CutoutStyle.cs — the cut-out's numbers + how it registers itself
public static class CutoutStyle
{
    public static readonly TransitionDefinition Transition = new(
        Name: "cutout",
        ExitMs: 700, EnterMs: 1100,   // tuned in spikes/cutout-demo
        LayerInterval: 0, Scatter: 0); // cut-out is a depth-0 container, no stagger;
                                       // easing is per-stop in the keyframes → default linear

    // No component-type registration (conflict C): MorphSurface's switch is the map.
    // Register only the timing. (`AddTransition` is the renamed MorphOptions.Add.)
    public static IServiceCollection AddCutout(this IServiceCollection s) =>
        s.AddTransition(CutoutStyle.Transition);
}
```

```csharp
// ServiceCollectionExtensions.cs — AddMorph composes the built-ins; each line is one folder
public static IServiceCollection AddMorph(this IServiceCollection s, Action<MorphOptions>? configure = null)
    => s.AddRaised().AddInset().AddCutout()
        .ConfigureMorph(configure);
```

`AddTransition` and `ConfigureMorph` are thin `IServiceCollection` helpers over
`MorphOptions`: `AddTransition` is today's `MorphOptions.Add` lifted to the service
collection (it registers one `TransitionDefinition`), and `ConfigureMorph` applies
the caller's `configure` delegate to the options. Both operate on the same single
`MorphOptions` instance.

### Style-specific motion (CSS keyed on the style class — D2)

```css
/* Styles/Cutout/cutout.css — motion lives WITH the style, not on the generic .morph-item.
   var(--enter-dur)/var(--exit-dur) resolve to the cut-out's OWN values because the
   style component emitted them onto this subtree's .morph-item (conflict B). */
.morph-stage[data-phase="enter"] .neu-cutout .cutout-floor { animation: cutout-open-floor var(--enter-dur) both; }
.morph-stage[data-phase="enter"] .neu-cutout .cutout-ring  { animation: cutout-open-ring  var(--enter-dur) both; }
.morph-stage[data-phase="exit"]  .neu-cutout .cutout-floor { animation: cutout-close-floor var(--exit-dur) both; }
.morph-stage[data-phase="exit"]  .neu-cutout .cutout-ring  { animation: cutout-close-ring  var(--exit-dur) both; }

@keyframes cutout-open-ring { /* phased: pop → hold → scale, per-stop timing functions */ }
/* … cutout-open-floor / cutout-close-* … (ported from spikes/cutout-demo) */
```

**Completion needs nothing from the style.** Because the engine awaits the real
animation set via `getAnimations` (D6), a style just declares its keyframes — no
sentinel, no counted name, no comma-list, no overlap or duration discipline. The
cut-out's `.morph-item` runs no visible animation of its own and that's fine: its
`floor`/`ring`/content layers animate, the engine waits for exactly those. The
engine CSS keeps only the reduced-motion guard (which now *also* doubles as
correctness insurance — no animations means `getAnimations` resolves instantly):

```css
/* morph.css (engine) — no sentinel; reduced-motion guard covers the new inner layers too. */
@media (prefers-reduced-motion: reduce) { .morph-stage .morph-item, .neu-cutout * { animation: none !important; } }
```

For single-layer styles (raised/inset) the visible `--lift` motion stays a single
ordinary `animation` on the `.morph-item` — no special shape required:

```css
/* raised.css — morph-strude is the existing raise/extrude ENTER keyframe (morph-melt is its exit) */
.morph-stage[data-phase="enter"] .neu-raised { animation: morph-strude var(--enter-dur) var(--enter-ease) both; }
```

### Product component with optional style

```razor
@* Card.razor — owns slots + a default style; forwards an optional override *@
<MorphSurface Style="@Style" Class="card @Class">
    <header class="card-head">@Title</header>
    <div class="card-body">@ChildContent</div>
</MorphSurface>

@code {
    [Parameter] public MorphStyle Style { get; set; } = MorphStyle.Raised;  // a card is raised by default
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```

```razor
@* Consumer end — flat; override is one optional attribute *@
<Card Title="Open runs"> … </Card>                          @* raised, the default *@
<Card Title="Dashboard" Style="MorphStyle.Cutout"> … </Card> @* same component, opens as a cut-out *@
```

The full chain: **consumer → Card/Panel (slots + default style) → MorphSurface
(type-safe dispatch) → MorphRaised/Inset/Cutout (scaffold) → MorphShape →
engine.** No magic strings anywhere; one-line-per-style dispatch; each style
self-contained.

---

## 6. Migration steps (incremental; each step: warning-free build + green tests + verified by running)

1. **Reorganize without behavior change.** Create `Engine/`, `Surface/`,
   `Styles/Raised`, `Styles/Inset`. Move existing files; split `neu.css` into the
   shared theme tokens + per-style CSS. No new behavior. Build + existing
   `spikes/morph-demo` renders identically. One commit.
2. **Introduce `MorphStyle` + `MorphRaised`/`MorphInset` + self-registration.**
   Wire `AddRaised()`/`AddInset()` (each registers only its `TransitionDefinition`
   — no component-type registry, conflict C). Migrate the demo to the named
   components. Behavior identical. One commit.
3. **Add `MorphSurface`** (the switch-expression dispatch — the compile-time map,
   no runtime `Type` lookup). Add a `Style` param to one demo component to prove
   variable style works. One commit.
4. **Adopt motion-per-style (D2) + per-style timing (conflict B) for raised/inset.**
   Move the melt/extrude keyframes onto `.neu-raised`/`.neu-inset` rules; relocate
   `TransitionDefinition.Vars` emission from the stage onto each style's
   `.morph-item` (a `Vars` passthrough on `MorphShape`); the stage keeps only
   `--max-depth`. Confirm each style animates with its own motion *and* that
   per-subtree `var(--enter-dur)` overrides work in a mixed stage. Verify visually.
   One commit. *(De-risked first by the §7 spike.)*
5. **Switch completion to `getAnimations` (D6 / D7).** Add `waitForAnimations` to
   `morph.js`; add `WaitForAnimationsAsync` to `MorphInterop`; in `MorphStageBase`
   replace the `animationend` tally + `countItems` + `TaskCompletionSource` with
   `await Interop.WaitForAnimationsAsync(StageElement)` (`StageElement` is the
   captured `.morph-stage` `ElementReference`), catching `JSException`
   (`AbortError`) as a superseded phase (the `_phaseGen` check already guards it).
   **Delete** `EventHandlers.cs`, the `@onanimationend` binding, `countItems`, and
   the now-dead tally state. Verify existing transitions still complete, an
   interrupted phase doesn't hang, and reduced-motion resolves instantly. One
   commit. *(Mechanism proven by `spikes/morph-getanimations/`.)*
6. **Build the cut-out.** Port the `floor`/`ring` scaffold + phased keyframes +
   tuned values from `spikes/cutout-demo` into `Styles/Cutout`. No sentinel, no
   completion plumbing — the engine waits for the layers automatically. Validate
   open *and* close complete with no hang, content timing correct, depth reads from
   frame one. Verify in a real browser (`tools/frontend-verify`). One commit.
7. **Delete `spikes/cutout-demo`** once parity is confirmed in the component.

Steps 1–3 are pure refactor/encapsulation and low-risk. Steps 4–6 carry the real
risk (conflicts A + B) and must be runtime-verified — the §7 spikes prove the
mechanisms before they touch `src/Morph`.

---

## 7. Verification

A green build does **not** prove this works — completion can hang silently and CSS
animation is invisible to the in-tool preview. Every animated step (4, 5, 6) must
be driven in a real browser via `tools/frontend-verify/` (Playwright over system
Chrome/Edge): confirm the phase resolves (no hang), capture animation frames for
the open/close, and check the console for errors.

The mechanism-level spikes are already done and passing (each has a `FINDINGS.md`):
- `spikes/morph-completion-cascade/` — per-style timing override + stagger in a
  mixed stage (conflict B). Driven by `tools/frontend-verify/probe-morph.mjs`.
- `spikes/morph-getanimations/` — `getAnimations` completion across gapped stagger,
  interruption (`AbortError`), and a static phase (conflict A / D6). Driven by
  `tools/frontend-verify/probe-getanimations.mjs`.

---

## 8. Non-goals

- **No plugin/extensible style system.** The style set is closed and known; that
  is precisely why D4 chooses a switch expression over `DynamicComponent`. Revisit
  only if styles must be contributed from outside the assembly at runtime.
- **No per-instance style classes leaking to callers.** Callers pass a `Style`
  enum and a `Class` *modifier* (size/role), never a `neu-*` identity class.
- **No blanket per-instance timing overrides.** Add an override param only on the
  specific component that needs one (D5), not as a global capability.

---

## 9. Open questions

- **Q1 (completion mechanism). RESOLVED → `getAnimations`.** The engine awaits
  `element.getAnimations({subtree:true})` + `Promise.all(...finished)`; no sentinel,
  no counting. Correct by construction for any delay/gap/nesting. See D6/D7. Proven
  by `spikes/morph-getanimations/` (6/6). Supersedes the earlier sentinel answer.
- **Q2 (registry shape). RESOLVED by removal.** There is no `MorphStyle → Type`
  registry — the `MorphSurface` switch is the compile-time map (conflict C / D4).
  The only registry that exists, `MorphOptions`' transition table, stays keyed by
  the transition `Name` string (the stage's `Transition` param resolves by name).
- **Q3 (product-component home).** Do `Card`/`Panel` live inside the Morph package
  or in the product UI layer? Leaning product UI — Morph ships the *style*
  primitives + `MorphSurface`; the app composes them into domain components.
- **Q4 (`{subtree:true}` reach).** The base `getAnimations()` is broadly supported;
  the `subtree` option shipped slightly later on some engines. Verify against the
  lowest target browser/WebView at step 5; MDN documents a descendant-map fallback
  if a target lacks it. (Not a blocker on current evergreen Chrome/Edge/Firefox/Safari.)
```
