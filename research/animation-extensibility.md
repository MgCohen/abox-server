# Animation extensibility: from 2 hardcoded transitions to a pluggable set

Status: analysis / proposal (not built). Scope: `spikes/ui-shell` only.

## Where we are today

Two coupled layers each hardcode the two transition types:

- **CSS** (`wwwroot/css/app.css`): two keyframe sets, `melt` and `extrude`, each
  animating `box-shadow` (flat↔raised), `scale`, `translateY`, `opacity`. They're
  bound by two selectors keyed off a phase class on the stage:
  `.stage.melting .morph-item { animation: melt ... }` and
  `.stage.extruding .morph-item { animation: extrude ... }`. A per-sibling `--i`
  custom property drives a stagger via `animation-delay`. Timing/curve come from
  four global vars on `.app-shell`: `--melt-dur`, `--extrude-dur`, `--stagger`,
  `--motion-ease`.
- **C#** (`Pages/Home.razor`): `enum Phase { Idle, Melting, Extruding }` drives a
  two-arm `PhaseClass` switch; `SwitchTo` runs the exit phase, swaps the view, runs
  the enter phase, with two `Task.Delay`s sized from the active `Preset`. A
  `MaxStaggerIndex = 8` constant bounds the wait. A `Preset` record carries
  `MeltMs/ExtrudeMs/StaggerMs/Ease`, serialized to the four CSS vars via
  `MotionVars`.

The two transition *types* (melt-out / extrude-in) are conflated with the two
*phases* (exit / enter) of a single transition. There is exactly one transition
("flat↔raised neumorphic morph"), and its two halves are the only "types." Adding
a *third* type (slide, flip, dissolve) means: new keyframes + new phase enum
members + new switch arms + new preset fields + new var serialization. Every layer
must be touched, and the type↔phase conflation has to be untangled first.

## Target model: a transition is data

Reframe the vocabulary. A **transition** is one named visual treatment with two
halves — an **exit** (the leaving content) and an **enter** (the arriving content).
"melt/extrude" is the *one* transition we have; "slide", "flip", "dissolve" are
peers. The orchestrator's phases stay `Idle → Exiting → Entering → Idle` — those
are universal and do not multiply per type.

So the axes are:
- **Phase** (exit / enter): structural, fixed, 3 states. Stays an enum.
- **Transition type** (melt, slide, flip, …): data, open set. Becomes a registry entry.

This is the key separation the current code is missing.

## CSS convention

One keyframe **pair** per transition type, named by convention, selected by a
`data-anim` attribute on the stage rather than a bespoke phase class.

### Naming + selection

```css
/* Convention: each type "X" defines @keyframes X-exit and @keyframes X-enter,
   and reads its timing from --X-exit-dur / --X-enter-dur / --X-ease, falling
   back to the global motion vars. Nothing else may reference X. */

.stage[data-phase="exit"]  .morph-item { animation-name: var(--anim-exit); }
.stage[data-phase="enter"] .morph-item { animation-name: var(--anim-enter); }

.stage .morph-item {
    animation-duration: var(--anim-dur, var(--melt-dur));
    animation-timing-function: var(--anim-ease, var(--motion-ease));
    animation-delay: calc(var(--i, 0) * var(--anim-stagger, var(--stagger)));
    animation-fill-mode: var(--anim-fill, forwards);
}
```

The stage carries the selected type's keyframe names and timing as inline vars
(`--anim-exit`, `--anim-enter`, `--anim-dur`, `--anim-ease`, `--anim-stagger`,
`--anim-fill`), set by C#. The phase is `data-phase`. With this, the per-phase
rules never mention a type name — adding a type touches **zero existing rules**.

`animation-name` accepting a CSS var works in all current evergreen browsers.
The one wrinkle: `fill-mode` differs between halves (today `melt` uses `forwards`,
`extrude` uses `backwards`). Expose it as `--anim-fill` so each half sets its own,
or normalize both halves to `both` (safe for these effects) and drop the var.

### Adding a third type (zero edits to existing types)

```css
/* slide.css — additive, self-contained */
@keyframes slide-exit {
    from { opacity: 1; transform: translateX(0); }
    to   { opacity: 0; transform: translateX(-24px); }
}
@keyframes slide-enter {
    from { opacity: 0; transform: translateX(24px); }
    to   { opacity: 1; transform: translateX(0); }
}
```

That's the entire CSS cost of a new type. The existing `melt`/`extrude` keyframes
are renamed once to `morph-exit`/`morph-enter` to match the convention, then never
touched again.

### Per-component extra style on top of the base shape

The base unit is the keyframe pair; a component layers its own animation **in
addition** via CSS's comma-separated `animation` list. Because the base rule sets
`animation-name` (not the `animation` shorthand), a component can append:

```css
/* a card that also wants a subtle hue shift while it morphs */
.morph-item.tinted {
    animation-name: var(--anim-exit), card-tint;   /* base + extra, composed */
}
```

Or, cleaner, keep the base untouched and add a second animated property the base
keyframes don't write (the two animations target disjoint properties, so they
compose without fighting). The rule: **base keyframes own transform/opacity/
box-shadow; per-component extras must animate other properties** (filter, color,
background-position) to avoid last-declaration-wins clobbering. Document that
contract next to the convention.

### Reduced motion

Keep the existing guard, generalized: `.stage[data-phase] .morph-item { animation: none }`
inside `@media (prefers-reduced-motion: reduce)`.

## C# abstraction

Replace the phase/type conflation with two clean pieces: a `TransitionDefinition`
record (the data) and a tiny registry. The orchestrator is parameterized by a
*selected* definition; phase stays an enum.

```csharp
public enum Phase { Idle, Exiting, Entering }

public sealed record TransitionDefinition(
    string Name,           // "morph", "slide", "flip"
    string ExitKeyframes,  // "morph-exit"  -> --anim-exit
    string EnterKeyframes, // "morph-enter" -> --anim-enter
    int ExitMs,
    int EnterMs,
    int StaggerMs,
    string Ease,
    string FillMode = "both")
{
    public int ExitWaitMs(int maxStaggerIndex) => ExitMs  + maxStaggerIndex * StaggerMs + 60;
    public int EnterWaitMs(int maxStaggerIndex) => EnterMs + maxStaggerIndex * StaggerMs + 60;
}
```

A registry is just a keyed collection — no DI ceremony needed for a spike, but it
maps cleanly onto a DI-registered service later (per the repo's "DI services over
statics" rule when this graduates out of the spike):

```csharp
public static class Transitions
{
    public static readonly TransitionDefinition Morph =
        new("morph", "morph-exit", "morph-enter", 420, 480, 35, "cubic-bezier(0.22,1,0.36,1)");

    public static readonly TransitionDefinition Slide =
        new("slide", "slide-exit", "slide-enter", 300, 340, 28, "cubic-bezier(0.4,0,0.2,1)");

    public static readonly IReadOnlyList<TransitionDefinition> All = [Morph, Slide];
    public static TransitionDefinition ByName(string n) => All.First(t => t.Name == n);
}
```

The "Preset" (Snappy/Smooth/Bouncy) and the "transition type" are now orthogonal:
a preset becomes a *speed/curve modifier* applied over any type, or — simpler for
the spike — fold both into `TransitionDefinition` and let the registry hold
`morph-smooth`, `morph-snappy`, `slide-smooth`, etc. Keep them separate only when
the second real use appears (YAGNI).

### Orchestrator, de-branched

```csharp
private Phase _phase = Phase.Idle;
private TransitionDefinition _active = Transitions.Morph;
private const int MaxStaggerIndex = 8;

private string PhaseAttr => _phase switch
{
    Phase.Exiting  => "exit",
    Phase.Entering => "enter",
    _ => "",
};

private string AnimVars =>
    $"--anim-exit:{_active.ExitKeyframes};--anim-enter:{_active.EnterKeyframes};" +
    $"--anim-dur:{(_phase == Phase.Exiting ? _active.ExitMs : _active.EnterMs)}ms;" +
    $"--anim-stagger:{_active.StaggerMs}ms;--anim-ease:{_active.Ease};--anim-fill:{_active.FillMode};";

private async Task SwitchTo(View target)
{
    if (_phase != Phase.Idle || _view == target) return;

    _phase = Phase.Exiting;  StateHasChanged();
    await Task.Delay(_active.ExitWaitMs(MaxStaggerIndex));

    _view = target;
    _phase = Phase.Entering; StateHasChanged();
    await Task.Delay(_active.EnterWaitMs(MaxStaggerIndex));

    _phase = Phase.Idle;     StateHasChanged();
}
```

The two `Task.Delay`s and the `Idle/Exit/Enter` sequence are now type-agnostic.
Adding a type is a registry entry + a CSS pair; the orchestrator never changes.
The status pill loses its `Melting/Extruding` literals and reads
`_active.Name + (_phase == Phase.Exiting ? " out" : " in")`.

### Per-stage and per-shape selection

- **Per-stage** (today's behavior): one `_active` definition for the whole stage;
  the markup binds `data-phase="@PhaseAttr"` and `style="@AnimVars"` on `.stage`.
- **Per-shape**: a shape can override by setting its own `--anim-exit`/`--anim-enter`
  inline (cascade beats the stage's vars on that element). In C#, let a
  `morph-item` carry an optional `TransitionDefinition?` and emit its own vars when
  present; otherwise inherit the stage's. The mechanism is identical at both
  scopes — only *where the vars are written* changes — which is what makes
  per-shape essentially free once per-stage works.

## Difficulty rating: **EASY → low-MEDIUM**

Justification:
- The hard part (PTY, JSONL, anything in the oracle) is absent — this is presentational.
- The refactor is mostly *renaming and lifting*: the keyframe bodies are reused
  verbatim, just renamed `morph-exit`/`morph-enter`; the orchestrator's control
  flow already has the right exit→swap→enter shape — only the literals come out.
- CSS custom properties for `animation-name`/timing are well-supported and the
  selection mechanism (`data-phase` + inline vars) needs no JS interop.
- It nudges toward MEDIUM only because of two real subtleties: (1) the
  **fill-mode mismatch** between the two halves (`forwards` vs `backwards`) must be
  parameterized or normalized, and (2) the **type-vs-phase conflation** has to be
  consciously untangled — a careless port keeps `Melting/Extruding` as "types" and
  the third type re-tangles everything. Get the axis split right and it's Easy.

## Single biggest risk

**Property-collision between the base keyframes and per-component extra animations.**
CSS resolves competing `@keyframes` that touch the same property by
last-declared-wins, silently — a component that animates `transform` "on top of"
a base type that also animates `transform` will clobber it with no error, and the
breakage is timing-dependent and easy to miss in review. The whole "components add
their own animation on top of the base shape" requirement lives or dies on a
clear, enforced contract: **base owns transform/opacity/box-shadow; extras must
use disjoint properties** (or opt into an explicit composed `animation-name` list
the author fully controls). Without that written rule, the pluggability that looks
clean in C# produces subtle visual bugs at the CSS layer.

(Secondary, smaller: the global `MaxStaggerIndex = 8` is a guess that must remain
≥ the largest `--i` any view emits; if a new dense view exceeds it the enter phase
gets cut off. Worth deriving from the rendered item count rather than hardcoding,
but out of scope for the minimal proof.)

## Minimal refactor that proves the design

Smallest change that demonstrates "type is data," end to end:

1. **CSS**: rename `@keyframes melt → morph-exit`, `extrude → morph-enter`. Replace
   the two `.stage.melting/.extruding .morph-item` rules with the single
   `data-phase` + var-driven rule above. Add one new self-contained pair
   (`slide-exit`/`slide-enter`) — proving a type is added with zero edits to `morph`.
2. **C#**: introduce `TransitionDefinition` + a 2-entry `Transitions` registry
   (`Morph`, `Slide`). Rename `Phase.Melting/Extruding → Exiting/Entering`. Replace
   `PhaseClass`/`MotionVars` with `PhaseAttr`/`AnimVars`. Delete the `Preset`-to-melt/
   extrude serialization; drive timing from `_active`.
3. **Markup**: bind `data-phase="@PhaseAttr"` and `style="@AnimVars"` on `.stage`;
   turn the existing preset buttons into a type picker over `Transitions.All`
   (Morph / Slide), so toggling the type at runtime visibly swaps the transition.
4. **One per-component proof**: tag a single card `.tinted` with an extra
   disjoint-property animation (e.g. a `filter` hue shift) to demonstrate the
   layering contract holds while the base morph runs.

Done-when: clicking a run still works; switching the picker from Morph to Slide
visibly changes the transition with no code branch added; the tinted card shows the
extra effect composed over the base. That exercises every seam (registry lookup,
data-driven selection, phase-agnostic orchestration, per-component layering) with
the least churn.
