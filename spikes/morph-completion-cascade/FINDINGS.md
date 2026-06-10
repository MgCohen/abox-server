# Spike: morph completion + per-style timing — FINDINGS

**Date:** 2026-06-10 · **Result:** ✅ PASS (8/8) · **Verdict:** both mechanisms are
sound; build them as designed (C#-only, no JS).

De-risks the two silent-failure mechanisms in
[`PLANS/morph-style-components-refactor.md`](../../PLANS/morph-style-components-refactor.md)
before they touch `src/Morph`. A hang or miscount fails silently (no timer
fallback in the engine), so a green build proves nothing — this runs a real
browser and asserts on event tally + timing.

## What it proves

- **Page is pure HTML/CSS** (`index.html`, no `<script>`). The Playwright harness
  (`tools/frontend-verify/probe-morph.mjs`) flips `data-phase` and tallies
  `animationend` — exactly what the C# engine does. So the production direction
  stays **C#-only / no JS**.
- One stage mixes three styles (raised 500ms, inset 900ms, cut-out 1100ms).

## Run

```
cd tools/frontend-verify && npm install   # one-time
node probe-morph.mjs
```

## Results (observed event trace)

```
content-in@403ms[content-tile]            ← not counted
morph-sentinel@510ms[raised]   ✓ counted  | morph-strude@510ms[raised]   ← not counted
morph-sentinel@963ms[inset]    ✓ counted  | morph-strude@963ms[inset]    ← not counted
morph-sentinel@1110ms[cutout]  ✓ counted  | cutout-open-ring/floor@1110ms ← not counted
```

| Check | Result |
|---|---|
| **A** · sentinel count == item count (no hang / no inflation) | 3 == 3 ✓ |
| **A** · every style's item counted | raised/inset/cutout ✓ |
| **A** · inner/content events fire but are excluded | 5 non-counted, no sentinel among them ✓ |
| **B** · per-subtree duration override (computed) | raised=500 inset=900 cutout=1100 ✓ |
| **B** · sentinels completed at their own durations | 510 / 963 / 1110 ms ✓ |
| **Q4** · sentinel ≥ longest visible motion (no early resolve) | cutout 1110 ≥ inner 1110 ✓ |
| **B** · stagger preserved (inset after raised) | 510 < 963 ✓ |
| no console/page errors | clean ✓ |

Screenshots (`tools/frontend-verify/artifacts/morph-spike/`) confirm the motion is
visually real: `01-mid.png` catches the cut-out mid-aperture while the raised card
(done at 500ms) has already settled.

## Mechanisms confirmed

1. **A — sentinel + `AnimationName` filter.** A no-op `morph-sentinel` animation on
   every `.morph-item` fires exactly one `animationend`; filtering the tally to
   `AnimationName == "morph-sentinel"` ignores inner-layer and screen-content
   events. Robust to any style with internal animation, zero JS.
2. **B — per-style timing.** `var(--enter-dur)` set on an inner `.morph-item`
   overrides the value for that subtree inside one mixed stage. Timing must be
   emitted by the **style component** onto its own `.morph-item`, not the stage.

## Refinement this surfaced (folded into the plan)

The sentinel **cannot** be a blanket generic `.morph-item` rule that coexists with
per-style item motion as a *separate* `animation:` declaration — the later
declaration clobbers the earlier (CSS `animation` is one property). For styles
whose item also has visible motion (raised/inset), the sentinel must be **one
comma entry in the same declaration**: `animation: morph-sentinel …, morph-strude …`.
Cut-out's item is sentinel-only (its motion is on floor/ring). Plan §5 updated.
