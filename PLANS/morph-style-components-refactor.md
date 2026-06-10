# Morph style-components refactor

**Status:** ✅ built (steps 1–7 shipped, runtime-verified) · **Scope:** `src/Morph` + its consumers · **Owner:** —

> **Build outcome (2026-06-10).** All seven steps landed as seven commits, each
> warning-free + browser-verified. Decisions made while building, where the plan
> was ambiguous or the snippets were illustrative:
>
> - **Q1 (D6.2) resolved → sentinel.** The cut-out's `.morph-item` carries a
>   duration-matched no-op animation (`cutout-life`, a registered `@property`), so
>   it emits exactly one counted `animationend`; the floor/ring/content motion runs
>   on inner layers (filtered out by D6.1). Hosting the completing event on the item
>   (not an inner layer) reads correctly — verified across two consecutive
>   close→open cycles (no hang) + reduced motion (~36ms, no hang).
> - **Q2 dropped, not chosen.** D4's switch-expression dispatch is the mechanism;
>   the redundant runtime `MorphStyle→Type` registry (`AddMorphStyle`) was *not*
>   built — it is the `DynamicComponent` path D4 rejects. Each `AddX()` registers
>   only its `TransitionDefinition`; `MorphOptions` is built from the DI-registered
>   set. Q2 is moot.
> - **Stage-level transition retired (consequence of D2).** `MorphStage`/
>   `MorphRouteStage` lost the `Transition` param; the demo lost its morph/slide
>   picker + `DemoTransitionState`. Motion now lives entirely per style; the stage
>   writes every registered style's namespaced vars (`Options.AllVars`) and only
>   flips `data-phase`. `TransitionDefinition` is now pure timing (no keyframe-name
>   fields — keyframes are named in each style's CSS).
> - **CSS stays in `wwwroot/` (servability).** RCL static web assets must live under
>   `wwwroot/`; `<Content Include>` from `Styles/` does *not* project to
>   `_content/Morph/` (verified empirically). So per-style CSS is `wwwroot/<x>.css`
>   while the component + values co-locate in `Styles/<X>/`. Consistent with the
>   plan's "CSS-isolation filing is a non-issue here" note (§D3).
> - **Cut-out layer order = floor then ring** (the spike's order), *not* the §5
>   snippet's ring-then-floor — the ring must paint above the floor for the
>   depth/bevel to show.
> - **Completion now needs `registerMorphEvents()` before `Blazor.start()`**
>   (`autostart=false`) — the only way to get `event.target` into the C# handler
>   (D6.1). New consumer setup step, documented in the README.
> - **Cut-out timing = the spike's tuned 1540ms** (both phases, phased percentages
>   baked in), not the looser 700/1100 in §5 — the spike is the tuning source of
>   truth.

This document is standalone. It assumes no prior conversation. Read it cold and
you should understand what Morph is today, what we're changing, why, and exactly
what the target looks like.

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
  **There is no timer fallback** — a miscount hangs the phase.

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

### D5 — What controls what (config layering).

| Concern | Lives in | Notes |
|---|---|---|
| **Motion *shape*** (the choreography: pop → hold → open) | CSS `@keyframes` in the style folder | The style's identity; not a per-instance knob. The cut-out's phased timing lives in multi-stop keyframes with per-stop `animation-timing-function`. |
| **Motion *timing/easing*** (durations, ease) | a `TransitionDefinition` instance in the style folder | The single tuning point. Registered centrally, applied everywhere that style is used. |
| **Style *structure*** (which scaffold) | the style component (`MorphCutout.razor`) | Owns the divs; consumers never see them. |
| **Which style** (fixed or variable) | the product component (`Card`/`Panel`) | A fixed style is the default; a variable one is an optional `Style` param forwarded to `MorphSurface`. |
| **Size / layout** | a `Class` modifier + CSS (`card`, `tile`, `wide`) | Not animation; never C#. |
| **Per-instance override** (rare: "this one is slower") | an opt-in component param that sets a CSS var | Add only on the component that needs it (YAGNI), defaulting to the registered value. |
| **Global safety** (`LoadTimeout`, reduced-motion) | `MorphOptions` | Unchanged. |

The product component decides **structure + which style**; it stays free of
numbers. Timing is data; motion-shape is keyframes; size is CSS.

### D6 — The cut-out must integrate with counted completion. (critical)

Completion counts `.morph-item` elements and expects **one `animationend` per
counted item**, with no timer fallback. The cut-out breaks this assumption: its
motion runs on inner `floor`/`ring` layers (which are *not* `.morph-item`s), so a
naive cut-out could emit **zero** `animationend` on its `.morph-item` (→ tally
never reaches count → **permanent hang**) or **extra** bubbled events from the
inner layers (→ completes early / miscounts).

**Proposed resolution (validate during the build):**

1. **Filter the tally to the counted element.** Change the stage's `animationend`
   handler to count only events whose `target` is a `.morph-item` (ignore events
   bubbling from descendant layers and from animated screen content). This makes
   the engine robust to *any* style with internal animation, not just the cut-out.
2. **Give the cut-out's `.morph-item` exactly one completing animation.** The
   primary open/close animation (the one whose duration defines "done") is hosted
   such that the counted `.morph-item` emits a single `animationend` of the right
   duration; secondary inner-layer animations are timed to finish no later and do
   not participate in the count (covered by step 1).

This is the highest-risk part of the work. It must be proven by running it (a
hung phase is silent), not just by a green build. See §7.

---

## 4. Target folder structure

Namespaces stay flat (`namespace Morph;`) — folders organize files, not
assemblies, consistent with the repo's "folders not walls" stance.

```
BEFORE  (src/Morph, flat)              AFTER  (src/Morph, sliced)
────────────────────────────          ─────────────────────────────────────────
MorphStage.razor                       Engine/
MorphRouteStage.razor                    MorphStage.razor
MorphStageBase.cs                        MorphRouteStage.razor
MorphStageContext.cs                     MorphStageBase.cs        ← +animationend target filter (D6.1)
MorphOrderCounter.cs                     MorphStageContext.cs
MorphPhase.cs                            MorphOrderCounter.cs
MorphShape.razor                         MorphPhase.cs
MorphInterop.cs                          MorphShape.razor
MorphOptions.cs                          MorphInterop.cs          ← countItems unchanged; tally filtered in C#
TransitionDefinition.cs                  MorphOptions.cs
ServiceCollectionExtensions.cs           TransitionDefinition.cs  ← the generic RECORD (shared)
ServiceProviderExtensions.cs             ServiceProviderExtensions.cs
wwwroot/morph.css                        wwwroot/morph.css        ← engine rules only
wwwroot/morph.js                         wwwroot/morph.js
wwwroot/neu.css                        Surface/
                                         MorphStyle.cs            ← the enum (the one closed list)
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
`.neu-inset` rules move into the respective style folders.

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

### Style values + self-registration (co-located with the component)

```csharp
// Styles/Cutout/CutoutStyle.cs — the cut-out's numbers + how it registers itself
public static class CutoutStyle
{
    public static readonly TransitionDefinition Transition = new(
        Name: "cutout",
        ExitKeyframes: "cutout-close", EnterKeyframes: "cutout-open",
        ExitMs: 700, EnterMs: 1100,      // tuned in spikes/cutout-demo
        LayerInterval: 0, Scatter: 0,    // cut-out is a depth-0 container, no stagger
        ExitEase: "linear", EnterEase: "linear"); // phased easing lives per-stop in the keyframes

    public static IServiceCollection AddCutout(this IServiceCollection s) => s
        .AddMorphStyle(MorphStyle.Cutout, typeof(MorphCutout)) // registry entry for MorphSurface
        .AddTransition(CutoutStyle.Transition);                // into MorphOptions
}
```

```csharp
// ServiceCollectionExtensions.cs — AddMorph composes the built-ins; each line is one folder
public static IServiceCollection AddMorph(this IServiceCollection s, Action<MorphOptions>? configure = null)
    => s.AddRaised().AddInset().AddCutout()
        .ConfigureMorph(configure);
```

### Style-specific motion (CSS keyed on the style class — D2)

```css
/* Styles/Cutout/cutout.css — motion lives WITH the style, not on the generic .morph-item */
.morph-stage[data-phase="enter"] .neu-cutout .cutout-floor { animation: cutout-open-floor var(--enter-dur) both; }
.morph-stage[data-phase="enter"] .neu-cutout .cutout-ring  { animation: cutout-open-ring  var(--enter-dur) both; }
.morph-stage[data-phase="exit"]  .neu-cutout .cutout-floor { animation: cutout-close-floor var(--exit-dur) both; }
.morph-stage[data-phase="exit"]  .neu-cutout .cutout-ring  { animation: cutout-close-ring  var(--exit-dur) both; }

@keyframes cutout-open-ring { /* phased: pop → hold → scale, per-stop timing functions */ }
/* … cutout-open-floor / cutout-close-* … (ported from spikes/cutout-demo) */
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
   Wire `AddRaised()`/`AddInset()` and the `AddMorphStyle` registry. Migrate the
   demo to the named components. Behavior identical. One commit.
3. **Add `MorphSurface`** (the switch-expression dispatch) + the registry-backed
   `Type` lookup. Add a `Style` param to one demo component to prove variable
   style works. One commit.
4. **Adopt motion-per-style (D2) for raised/inset.** Move the melt/extrude
   keyframes onto `.neu-raised`/`.neu-inset` rules; confirm each animates with its
   own motion (an inset no longer borrows the raised keyframes). Verify visually.
   One commit.
5. **Harden completion for inner-layer animation (D6.1).** Filter the stage tally
   to `animationend` whose `target` is a `.morph-item`. Verify existing
   transitions still complete; add a regression for an animated child inside a
   screen *not* inflating the count. One commit.
6. **Build the cut-out (D6.2).** Port the `floor`/`ring` scaffold + phased
   keyframes + tuned values from `spikes/cutout-demo` into `Styles/Cutout`.
   Validate open *and* close complete with no hang, content timing correct, depth
   reads from frame one. Verify in a real browser (the in-tool preview can't see
   CSS animation — use `tools/frontend-verify`). One commit.
7. **Delete `spikes/cutout-demo`** once parity is confirmed in the component.

Steps 1–4 are pure refactor/encapsulation and low-risk. Steps 5–6 carry the real
risk (D6) and must be runtime-verified.

---

## 7. Verification

A green build does **not** prove this works — completion can hang silently and CSS
animation is invisible to the in-tool preview. Every animated step (4, 5, 6) must
be driven in a real browser via `tools/frontend-verify/` (Playwright over system
Chrome/Edge): confirm the phase resolves (no hang), capture animation frames for
the open/close, and check the console for errors.

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

- **Q1 (D6.2 mechanism).** Does hosting the cut-out's single completing
  `animationend` on the `.morph-item` (vs. on the `ring`/`floor`) read correctly,
  or does the primary animation need to live on an inner layer with the
  `.morph-item` carrying a duration-matched sentinel animation? Resolve by
  building step 6 and observing.
- **Q2 (registry shape).** Key the `MorphStyle → Type` registry by the enum
  (closed, discoverable, one central enum file) or by the transition `Name` string
  (open, fully co-located, less type-safe)? Default: **enum**, unless Non-goal #1
  changes.
- **Q3 (product-component home).** Do `Card`/`Panel` live inside the Morph package
  or in the product UI layer? Leaning product UI — Morph ships the *style*
  primitives + `MorphSurface`; the app composes them into domain components.
```
