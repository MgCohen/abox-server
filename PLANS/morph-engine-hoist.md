# Morph engine hoist — kill the per-style motion duplication

**Status:** 📐 planned (no code changed) · **Scope:** `src/Morph` (CSS + 2 style
components) · **Behavior:** preserved (no visual change) · **Owner:** —

This document is standalone. It addresses the three findings from the
thermo-nuclear review of the reference-fidelity branch. Read it cold and you
should understand what's duplicated, why it's an engine-vs-style boundary leak,
and the exact edits to fix each.

> **Why.** `raised.css` and `inset.css` are near-identical: the only genuinely
> style-specific thing in either is the box-shadow recipe. The **sequencing
> formula** (when an item fires) is copy-pasted 8×, the **content-layer rules** and
> the **lift keyframes** are byte-identical across both. raised and inset are not
> two motions — they're **one "lift melt" motion with two shadow recipes.** The
> engine should own the motion; styles should own only the recipe. That's the
> boundary `src/Morph/README.md` already claims but the code doesn't honor.

The end state: **morph.css owns *when* + the generic lift/content motion; each
style file owns only its `.neu-<x>` shadow recipe + two wiring lines; cut-out
stays the genuine outlier.**

---

## Fix 1 — Hoist the lift motion to the engine (`--morph-delay` + `.neu-lift`)

The big one. Behavior-preserving: same delays, same keyframes, same fade.

### 1a. `morph.css` becomes the canonical owner

Replace [morph.css](src/Morph/wwwroot/morph.css) with (keeps the existing stage
rules + content keyframes, **adds** the delay property, the shared lift keyframes,
the generic content rules, and one reduced-motion block):

```css
.morph-stage { display: block; }
.morph-screen { display: block; }

/* WHEN an item animates — the sequencing formula, defined once */
.morph-stage[data-phase="exit"] .morph-item {
    --morph-delay: calc((var(--max-depth, 0) - var(--depth, 0)) * var(--depth-step, 0ms) + var(--rand, 0) * var(--scatter, 0ms));
}
.morph-stage[data-phase="enter"] .morph-item {
    --morph-delay: calc(var(--depth, 0) * var(--depth-step, 0ms) + var(--rand, 0) * var(--scatter, 0ms));
}

/* the shared "lift melt" — raised + inset are recipes on top of this */
@keyframes lift-out { to   { --lift: 0; opacity: 0; } }
@keyframes lift-in  { from { --lift: 0; opacity: 0; } }

/* content layer — generic for any .neu-lift style; cut-out opts out by not carrying the class */
@keyframes morph-content-out { to   { opacity: 0; } }
@keyframes morph-content-in  { from { opacity: 0; } }

.morph-stage[data-phase="exit"] .neu-lift > *:not(.morph-item) {
    animation: morph-content-out calc(var(--exit-dur) * var(--content-lead)) var(--exit-ease) both;
    animation-delay: var(--morph-delay);
}
.morph-stage[data-phase="enter"] .neu-lift > *:not(.morph-item) {
    animation: morph-content-in calc(var(--enter-dur) * var(--content-trail)) var(--enter-ease) both;
    animation-delay: calc(var(--morph-delay) + var(--enter-dur) * (1 - var(--content-trail)));
}

@media (prefers-reduced-motion: reduce) {
    .morph-stage .neu-lift,
    .morph-stage .neu-lift > *:not(.morph-item) {
        animation: none !important;
    }
}
```

Why it works: `--morph-delay` is set on every `.morph-item` and **inherits** to its
content children, so the shell and its content share one base delay with no
re-typing. Nested `.morph-item`s get their own `--morph-delay` (and their content
inherits *that*), so nesting still cascades correctly. The content vars
(`--content-lead/-trail`, `--exit/enter-dur`, eases) already live on the
`.morph-item` via the style's `Vars`, so the generic rule resolves them per-style
through inheritance.

### 1b. `raised.css` shrinks to the recipe + wiring

Replace [raised.css](src/Morph/wwwroot/raised.css) entirely with:

```css
.neu-raised {
    background: var(--surface);
    box-shadow:
        calc(var(--lift) * 10px) calc(var(--lift) * 10px) calc(var(--lift) * 22px) var(--shadow-dark),
        calc(var(--lift) * -9px) calc(var(--lift) * -9px) calc(var(--lift) * 20px) var(--shadow-light);
}

.morph-stage[data-phase="exit"]  .neu-raised { animation: lift-out var(--exit-dur) var(--exit-ease) both; animation-delay: var(--morph-delay); }
.morph-stage[data-phase="enter"] .neu-raised { animation: lift-in  var(--enter-dur) var(--enter-ease) both; animation-delay: var(--morph-delay); }
```

(Gone: `raise-out`/`raise-in` keyframes → shared `lift-out/in`; the 4 duplicated
delay formulas → `var(--morph-delay)`; the content rules → generic; the
reduced-motion block → covered by the generic `.neu-lift` block. ~47 lines → ~9.)

### 1c. `inset.css` shrinks identically

Replace [inset.css](src/Morph/wwwroot/inset.css) entirely with:

```css
.neu-inset {
    background: var(--surface);
    box-shadow:
        inset calc(var(--lift) * 5px) calc(var(--lift) * 5px) calc(var(--lift) * 10px) var(--shadow-dark),
        inset calc(var(--lift) * -5px) calc(var(--lift) * -5px) calc(var(--lift) * 10px) var(--shadow-light);
}

.morph-stage[data-phase="exit"]  .neu-inset { animation: lift-out var(--exit-dur) var(--exit-ease) both; animation-delay: var(--morph-delay); }
.morph-stage[data-phase="enter"] .neu-inset { animation: lift-in  var(--enter-dur) var(--enter-ease) both; animation-delay: var(--morph-delay); }
```

### 1d. The two lift styles opt into the marker class

The positive marker is what excludes cut-out from the generic content rule
**without the engine ever naming cut-out** (no `:not(.neu-cutout)` feature-check in
shared code).

- [MorphRaised.razor](src/Morph/Styles/Raised/MorphRaised.razor):
  `Class="@($"neu-raised {Class}")"` → `Class="@($"neu-lift neu-raised {Class}")"`
- [MorphInset.razor](src/Morph/Styles/Inset/MorphInset.razor):
  `Class="@($"neu-inset {Class}")"` → `Class="@($"neu-lift neu-inset {Class}")"`

`MorphSurface` dispatches to these components, so its raised/inset arms inherit the
marker for free. **MorphCutout is untouched** — no `.neu-lift`, so it keeps its
bespoke `.cutout-floor` motion and its own reduced-motion block.

### 1e. Cut-out: no change

[cutout.css](src/Morph/wwwroot/cutout.css) and `CutoutStyle` stay as-is. Cut-out is
still a `.morph-item`, so it harmlessly gets a `--morph-delay` it never reads
(its `DepthStep`/`ScatterMax` are 0 anyway). Confirms cut-out is the real outlier,
not a special-case bolted into shared code.

**Result:** the cascade formula lives in exactly one place; adding a new
lift-style is "write a box-shadow recipe + two wiring lines + the `neu-lift`
marker," not "re-type the motion." Engine owns sequence; styles own identity.

---

## Fix 2 — One owner for the content-fraction defaults

`ContentLead`/`ContentTrail` default to `0.45`/`0.5` in **two** places: the
[TransitionDefinition](src/Morph/Engine/TransitionDefinition.cs) record defaults
**and** (today) the CSS fallbacks `var(--content-lead, 0.45)`.

**Fix:** the record is the single owner. `TransitionDefinition.Vars` *always*
emits `--content-lead`/`--content-trail`, and the generic content rule only ever
matches `.neu-lift` items (always created by `MorphRaised`/`MorphInset`, which
always pass `Vars`). So the CSS fallback is unreachable — drop it. The morph.css
rules in Fix 1 already use bare `var(--content-lead)` / `var(--content-trail)` (no
fallback). Nothing else to change; the default lives only in C#.

---

## Fix 3 — Collapse the `MorphShape.StyleString` branch

[MorphShape.razor](src/Morph/Engine/MorphShape.razor) guards an empty `Vars`:

```csharp
private string StyleString => string.IsNullOrEmpty(Vars)
    ? $"--depth:{Depth};--rand:{_rand}"
    : $"{Vars};--depth:{Depth};--rand:{_rand}";
```

`MorphShape` is an internal building block always driven by a style component that
supplies `Vars`; the empty case isn't reachable through the public surface. A
leading `;` in a `style` attribute is ignored by the browser, so the branch buys
nothing.

**Fix:** drop the branch.

```csharp
private string StyleString => $"{Vars};--depth:{Depth};--rand:{_rand}";
```

(If we'd rather not emit a stray leading `;` when `Vars` is genuinely empty, the
honest alternative is to make `Vars` `[EditorRequired]` so the contract is
explicit — but that's optional; the one-liner above is the minimal fix.)

---

## Build order & verification

1. **Fix 1** (the hoist) — the only one with visual risk. After it:
   - **Behavior parity is the gate.** Re-measure per-depth exit delays with a big
     `DepthStep` (e.g. 600, `ScatterMax` 0) on Profile — must still read d3@0 →
     d2@600 → d1@1200 → d0@1800, exactly as before the hoist.
   - Burst-capture a `Gallery → Dashboard` nav and eyeball against the pre-hoist
     frames: empty midpoint reached, content leads out / trails in, nesting clean.
   - `dotnet build RemoteAgents.slnx` warning-free; `Morph.Tests` 5/5; no console
     errors. (Watch out for the WASM hot-reload corruption — clean watch restart
     after the C# `MorphRaised`/`MorphInset` edits, not a hot patch.)
2. **Fix 2 + Fix 3** — fold into the same pass; they're trivial and covered by the
   same build + nav check.

One coherent commit (it's a net deletion). Update `src/Morph/README.md`'s "Adding
a style" section: a lift-style now adds the `neu-lift` marker and references
`var(--morph-delay)` instead of re-declaring the delay formula; cut-out-style
(bespoke motion) omits the marker.

---

## Risks / notes

- **`var()` in `animation-delay`** is already how the current code works (`var(--step)`
  etc.), so `var(--morph-delay)` is the same proven pattern — no new mechanism.
- **`@property --lift`** stays registered in `theme.css`; the shared `lift-*`
  keyframes reference it fine from `morph.css`.
- **Engine↔style coupling** is now a *positive* opt-in (`.neu-lift`) rather than a
  negative cut-out exclusion — the engine never names a concrete style.
- **Scope guard:** do **not** also try to merge raised+inset into one file or
  generalize the box-shadow — the recipes are their real identities and must stay
  separate, one file each. The hoist removes *motion* duplication only.
