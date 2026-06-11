# Morph engine hoist — kill the per-style motion duplication

**Status:** ✅ shipped · **Scope:** `src/Morph` (CSS + 1 engine component) ·
**Behavior:** preserved (no visual change) · **Owner:** —

Three findings from the thermo-nuclear review of the reference-fidelity branch.
Fix 2 + Fix 3 shipped first (commit `1388d30`); Fix 1 shipped as the **minimal**
extraction below — deliberately *not* the full hoist first drafted here.

---

## Fix 1 — Hoist only the cascade formula (`--morph-delay`)

### The smell, narrowed

`raised.css` and `inset.css` look near-identical, but the duplication is not all
the same *kind*, and that distinction decides what to extract:

- **Box-shadow recipe** — the genuine style identity (raised = outer shadow,
  inset = inset shadow). Stays per-file. Untouched.
- **Keyframes** (`raise-out` vs `inset-out`) — byte-identical *today*, but this is
  where a future "inset melts differently" divergence would live. Cheap to keep
  local; **not** extracted.
- **Content-fade rules** — short, per-style, and a plausible place to want
  per-style timing later (raised content leading more than inset). Stay local;
  they just stop re-typing the formula.
- **The sequencing/delay formula** — `calc((max-depth - depth) * step + rand *
  scatter)`. This is *the cascade*: how the stage orchestrates its children by
  depth. It is **not** a style choice — raised and inset cascading by different
  depth-math would look incoherent side-by-side. And its siblings
  (`morph-content-*` keyframes, `--depth`, `--max-depth`, the stage phases)
  **already live in morph.css**. The style files were reaching *up* into the
  engine's vars to re-derive it, copy-pasted 8×. That is the real leak.

So we extract **only the formula**, and leave everything else local. This kills
the 8× duplication (the part everyone agrees is bad) without inventing an
opt-in/opt-out marker for the styles that don't melt.

### Why we did NOT do the full hoist

The first draft hoisted the keyframes + content rules into a generic
`.neu-lift` engine layer, with raised/inset opting in via a marker class and
cut-out opting out by absence. Rejected: it merges things that are identical
*by current taste, not by shared essence* (keyframes, content timing), so the
day one style diverges we'd unpick the abstraction — and it introduces marker
plumbing to gate the shared rule. Net: more machinery, less independence, for
duplication that is cheap and legitimately per-style. YAGNI — extract the one
piece that is genuinely the engine's, no more.

### The edit

`morph.css` defines the cascade timing once, on every `.morph-item`:

```css
.morph-stage[data-phase="exit"]  .morph-item { --morph-delay: calc((var(--max-depth, 0) - var(--depth, 0)) * var(--depth-step, 0ms) + var(--rand, 0) * var(--scatter, 0ms)); }
.morph-stage[data-phase="enter"] .morph-item { --morph-delay: calc(var(--depth, 0) * var(--depth-step, 0ms) + var(--rand, 0) * var(--scatter, 0ms)); }
```

`raised.css` / `inset.css` keep their own keyframes and content rules; the four
delay sites each collapse to `var(--morph-delay)` (enter-content adds its trail
offset on top):

```css
.morph-stage[data-phase="exit"]  .neu-raised { animation: raise-out var(--exit-dur) var(--exit-ease) both; animation-delay: var(--morph-delay); }
.morph-stage[data-phase="enter"] .neu-raised { animation: raise-in  var(--enter-dur) var(--enter-ease) both; animation-delay: var(--morph-delay); }
.morph-stage[data-phase="exit"]  .neu-raised > *:not(.morph-item) { animation: morph-content-out calc(var(--exit-dur) * var(--content-lead)) var(--exit-ease) both; animation-delay: var(--morph-delay); }
.morph-stage[data-phase="enter"] .neu-raised > *:not(.morph-item) { animation: morph-content-in calc(var(--enter-dur) * var(--content-trail)) var(--enter-ease) both; animation-delay: calc(var(--morph-delay) + var(--enter-dur) * (1 - var(--content-trail))); }
```

**Why it works:** `--morph-delay` is set per `.morph-item` and is an unevaluated
token stream — its inner `var(--depth)`/`--rand`/`--max-depth` resolve at the
point of use. The shell reads it on the same element (`.neu-raised` *is* the
`.morph-item`); content children inherit it and resolve the inner vars from their
own inherited `--depth`/`--rand`, which equal the parent's. This is the same
inheritance the old content rules already relied on — no new mechanism.

**Cut-out needs nothing.** `MorphCutout`'s `.morph-item` gets a `--morph-delay`
too, but its `DepthStep`/`ScatterMax` are `0`, so it evaluates to `0ms` and
`cutout.css` never references it. No marker, no exclusion.

**Keep `--morph-delay` unregistered.** Do *not* register it via `@property` with a
`<time>` syntax — that would force early computation and break the per-element
lazy resolution the extraction depends on.

---

## Fix 2 — One owner for the content-fraction defaults — shipped (`1388d30`)

`ContentLead`/`ContentTrail` defaulted to `0.45`/`0.5` in both the
`TransitionDefinition` record **and** the CSS fallbacks `var(--content-lead, 0.45)`.
`Vars` always emits the values and the content rules only match items that carry
`Vars`, so the fallback was unreachable. Dropped it; the record is the single
owner.

---

## Fix 3 — `MorphShape.Vars` is a required contract — shipped (`1388d30`)

`StyleString` branched on an empty `Vars` that no caller produces. Made `Vars`
`[EditorRequired]` + non-nullable (not the C# `required` keyword — that breaks
Blazor's parameterless instantiation) and collapsed `StyleString` to one arm.

---

## Verification (Fix 1)

- `dotnet build RemoteAgents.slnx` warning-free; `Morph.Tests` 5/5.
- **Behavior parity gate:** re-measure per-depth exit delays with a big
  `DepthStep` (600, `ScatterMax` 0) on Profile — must still read d3@0 → d2@600 →
  d1@1200 → d0@1800, exactly as before.
- Burst-capture `Gallery → Dashboard`; confirm empty midpoint, content leads
  out / trails in, nesting clean.
- Watch the WASM hot-reload corruption note: CSS edits hot-reload fine; this fix
  touches no `.razor`, so no rude-edit restart needed.

---

## Follow-on — custom content motion + structural completion

A separate thread (not one of the three fixes above): make per-element custom
motion a first-class, low-ceremony capability, and simplify what "the transition
is complete" means. Three seams, all additive — every existing screen is
unchanged because each defaults to off.

### 1. Structural completion — `morph.js`

`waitForAnimations` decides which animations the swap waits for. It now keys on
**structure, not naming**: an animation is awaited iff it is *one-shot* and its
target sits inside a `.morph-item`.

```js
const anims = el.getAnimations({ subtree: true }).filter((a) =>
    a.effect?.getComputedTiming().iterations !== Infinity &&   // loops never finish — exclude
    a.effect?.target?.closest?.(".morph-item"));                // inside a shell ⇒ part of the transition
```

- **Auto-wait, no opt-in.** Any one-shot animation on a shell or its content is
  awaited — consumers do not name keyframes `morph-*` to be counted. The demo's
  spin keyframes are plain `spin-in` / `spin-out` and are still held for.
- **Foreign isolation is structural.** A non-morph widget placed in a screen is
  not wrapped in a `.morph-item`, so it is ignored and runs free — it cannot
  stall the swap.
- **Loops still excluded** regardless of location: an infinite animation's
  `.finished` never resolves, so awaiting it would hang the transition.

This replaced an earlier name-prefix proxy (`/^(raise|inset|cutout|morph)-/`):
the structural rule draws the same line without the naming footgun (forget the
prefix and your animation is silently clipped at swap).

### 2. `.morph-custom` — opt out of the default content fade

Engine content rules fade every non-shell child (`> *:not(.morph-item)`). A child
tagged `.morph-custom` is excluded (`:not(.morph-item):not(.morph-custom)`) so a
consumer can bring its own motion without the default opacity fade fighting it.
Applied in `raised.css` / `inset.css` / `cutout.css`. Orthogonal to completion —
purely about the fade CSS.

### 3. `--shell-hold` — container waits for its content

A custom content animation longer than its shell's exit fade gets its tail hidden:
the shell animates `opacity: 0` over the content, and a parent's opacity caps the
child's. To keep the container up until the content finishes, the exit-shell delay
gains an opt-in term:

```css
.morph-stage[data-phase="exit"] .neu-raised {
    animation-delay: calc(var(--morph-delay) + var(--shell-hold, 0ms));
}
```

Default `0ms` ⇒ no change. A consumer sets `--shell-hold` to the content's
duration on the card; during the hold, `animation-fill-mode: both` pins the shell
at its visible start state, so the content plays over a fully-visible container,
then the container fades. Exit-only (on enter the shell appears first, content
animates over it). Scoped to raised + inset; cutout has its own bespoke
aperture/content timing and is deliberately excluded.

### The consumer's mental model — one knob

- **Waiting is automatic** (structural). You do not think about it.
- **`--shell-hold` is the only knob** — does the container wait for its content
  before animating. One var drives both the content's duration and the hold.

### Showcase + verification

Gallery's first card (`spikes/morph-demo`): the heading does a full 360° Z-axis
spin (`--shell-hold: 1.1s`, one var driving spin duration + hold) and carries
`.morph-custom` to escape the default fade. Browser-verified (Playwright over
system Chrome):

- Exit holds the full ~1.9s for the spin even though its keyframe is plain
  `spin-out` — structural wait, no `morph-` prefix.
- Card opacity stays `1.0` through the entire spin, then fades — `--shell-hold`
  working; the tail is no longer hidden behind a faded container.
- Module filter test: a 200ms animation inside a `.morph-item` is awaited; a
  4000ms animation outside one, and an infinite loop inside one, are both ignored
  (resolved ~209ms).
