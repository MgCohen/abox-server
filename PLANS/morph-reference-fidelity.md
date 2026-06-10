# Morph reference-fidelity transition

**Status:** ✅ built (A, B, D, E shipped; C tried + reverted) · **next:** sequential
order-based cascade · **Scope:** `src/Morph` (engine CSS + `SwapDelay`) +
`spikes/morph-demo` (showcase) + host CSS · **Owner:** —

This document is standalone. Read it cold and you should understand what the
Morph page transition does today, what the reference design does, the precise
gap, and exactly what to touch to close it.

> **Build outcome (2026-06-10).**
> - **A (two-tier content layer)** — `morph-content-out/in` keyframes in
>   `morph.css` (**pure opacity fade**, no slide — the reference surfaces content
>   in place); per-style `.neu-raised/.neu-inset > *:not(.morph-item)` rules
>   (content leads out at 0.45×exit, trails in at 0.5×enter, depth-staggered).
>   Cut-out keeps its bespoke content rule (also de-slid to a pure fade).
>   Completion unaffected (`getAnimations` waits on the extra animations for free).
>   Verified: content fades while its shell trails; max effective item opacity hits
>   **0 (genuine empty midpoint)** before the swap.
> - **B (shell the chrome)** — page titles in a `.page-head` shell (3 pages);
>   Dashboard's 4 section label+button rows in `.section-head` shells. Pages now
>   enter as all-empty shells (no popped text).
> - **C (fixed well) — TRIED, THEN REVERTED.** A fixed-height internal-scroll well
>   killed the height snap but clipped the shells' neumorphic shadows at the well
>   edges and felt cramped/non-full-screen. Reverted to normal document flow +
>   **D** for the flicker, and widened `.shell` to `max-width: 1080px`. The
>   vertical height change between pages is accepted (it no longer reads as a flick
>   once the horizontal gutter is stable).
> - **D (scrollbar gutter)** — `scrollbar-gutter: stable` on `html`. This alone
>   removes the sideways flick; it is the chosen flicker fix (C was overkill).
> - **E (empty hold)** — `MorphOptions.SwapDelay` (ms, default 0). Awaited at the
>   empty midpoint in `TransitionAsync` + `MorphRouteStage.EnterAsync`; skipped
>   under reduced motion. Verified toggleable: 0 → exit ends ~817ms; 400 → ~1238ms.
> - **Stagger** — `Scatter` bumped 30→140 (raised + inset) so same-depth siblings
>   visibly cascade. Measured: depth staggering works correctly (d2@105ms →
>   d1@~275ms → d0@315–437ms, deepest-first on exit), but reads as concurrent
>   because (a) duration 440ms ≫ the 150ms inter-depth gap → heavy overlap, (b)
>   scatter≈layer smears the bands, (c) 8 of 11 containers share the depth-0 band.
>   Random scatter can't produce a *directional* cascade — hence the next step.
> - **NEXT — sequential order-based cascade.** Emit `--order` from `MorphShape`;
>   delay = `order × interval` (reversed on exit) for one clean monotonic sweep;
>   trim per-item duration so starts aren't buried under overlap. Replaces random
>   scatter as the legibility mechanism. *(Not yet built.)*
> - Q1 (nesting) checked on the depth-2 cards: inner shells flatten cleanly, no
>   vanishing glitch. slnx 0 warnings, Morph.Tests 5/5, console clean.

> **Why now.** Reviewing the live `Gallery → Dashboard` transition against the
> original reference clip (`32353.mp4`, a `Run detail → Overview` morph) showed we
> are doing a *different animation*, not a rougher version of the same one. This
> plan is the realignment.

---

## 1. The target — what the reference actually does

The reference is a clean **four-beat melt-and-reform of one surface**, with a
fixed chrome frame around it. Frames cited are 5fps extracts in
`tools/frontend-verify/artifacts/refvid/` (local only — `artifacts/` is
gitignored; re-extract with the ffmpeg line in §6).

1. **Content evaporates first** (`f001 → f003`). Text/numbers/icons inside each
   panel fade + lift away, leaving the panels as **empty embossed shells**. The
   app even labels it: status reads *"melting.."*.
2. **Empty shells flatten and dissolve** (`f003 → f006`). The neumorphic lift
   drops to zero and the shells sink into the flat surface until the screen is
   **completely empty** — a genuine blank slate. The old page is 100% gone.
3. **New empty shells extrude up** from the flat surface in the new layout
   (`f006 → f007`), still with no text. Status flips to *"extruding.."*.
4. **Content fills back into the new shells** (`f007 → f012`).

Plus two structural facts:

- **Persistent chrome frame.** The bottom toolbar (`Swap to … / Auto-loop`) and
  the right-edge settings panel never move — **only a central content well
  morphs.** The morph is scoped, not full-viewport.
- **Everything lives in a shell.** There is no bare floating text in the well;
  the header breadcrumb is itself inside a panel, so it melts with the rest.

The shape of it: **two layers (content vs shell), each phase ordered
content→shell on the way out and shell→content on the way in, reaching a clean
empty midpoint, inside a fixed frame.**

---

## 2. Where we are vs the target

| Aspect | Reference | Ours today | Gap |
|---|---|---|---|
| What animates | every element (all in shells) | only `.neu-*` shapes | bare text/headings/buttons **hard-cut** |
| Content vs shell | two layers, staged (lead-out / trail-in) | one layer — content shares the shell's opacity | plain crossfade, no "content leaves, then shell flattens" |
| Empty midpoint | full blank slate, brief hold | shapes hit 0 before swap, but bare text persists → new text pops | never reads as "surface cleared" |
| Frame | fixed chrome + morphing well | nav bar is outside the stage (✓), but the *well* is raw document flow | height snaps 954→1378px at swap |
| Horizontal stability | fixed frame, internal scroll | centered `.shell`, page-level scroll | scrollbar appears on taller page → ~7px **sideways flick** |

Sequencing is **already correct** — exit fully completes before the swap, and the
new screen mounts at opacity 0 then enters (measured: exit ends ~817ms, enter
starts ~833ms, new content mounts hidden). The problem is **not** timing/delay.
It is the three root causes below.

---

## 3. Root causes

- **R1 — one-layer morph.** `MorphShape` animates only the `.neu-*` shell;
  content is just opacity-dragged along by the shell. No distinct content layer
  means no content→shell / shell→content staging, and anything **not** wrapped in
  a shell (page titles, section `<h2>`, buttons) gets no animation at all.
- **R2 — raw document well.** `MorphRouteStage` morphs the literal page body in
  normal flow, so its height jumps at the swap and its scrollbar toggles per page.
- **R3 — authoring.** The demo pages put chrome (`<h1>`, `<p class="page-sub">`,
  section headers, `<button>`) **outside** any shell, so even a perfect engine
  would hard-cut them.

---

## 4. The changes — by area

### A. Two-tier content layer  *(the core; `src/Morph`)*

Give every shell a **content layer** distinct from the **shell layer**, ordered
content-leads-out / content-trails-in. This is the cut-out's existing
`.cutout-floor > *` content reveal, generalized to raised + inset.

**Engine — shared content motion** in [morph.css](src/Morph/wwwroot/morph.css)
(engine owns the generic vocabulary; no `.neu-*` coupling here):

```css
@keyframes morph-content-out { to   { opacity: 0; transform: translateY(8px); } }
@keyframes morph-content-in  { from { opacity: 0; transform: translateY(8px); } }
```

**Per-style opt-in** in [raised.css](src/Morph/wwwroot/raised.css) +
[inset.css](src/Morph/wwwroot/inset.css). Target *direct, non-shape* children so
**nested `.morph-item` shells are excluded** and keep running their own shell
animation:

```css
.morph-stage[data-phase="exit"] .neu-raised > *:not(.morph-item) {
    animation: morph-content-out calc(var(--exit-dur) * 0.45) var(--exit-ease) both;
}
.morph-stage[data-phase="enter"] .neu-raised > *:not(.morph-item) {
    animation: morph-content-in calc(var(--enter-dur) * 0.5) var(--enter-ease)
               calc(var(--enter-dur) * 0.5) both;   /* trails: starts at the halfway point */
}
```

The **shell** keyframes (`raise-out`/`raise-in`, lift + opacity over the full
phase) stay as-is. The layering then composes for free: content opacity × shell
opacity makes content vanish *before* the empty shell finishes flattening (exit)
and appear *after* the shell has risen (enter) — i.e. beats 1–4 of §1.

- **No `MorphShape` markup change needed** — `.neu-raised > *` already are the
  content nodes (mirrors how cut-out targets `.cutout-floor > *`). If a future
  style needs an explicit wrapper we add it then (YAGNI).
- **Cut-out keeps its bespoke content rule** (it already stages floor/ring +
  `cutout-content`); the generic `:not(.morph-item)` selector is scoped to
  `.neu-raised`/`.neu-inset`, so it never touches `.cutout-floor`/`.cutout-ring`.
- **Completion is unaffected** — `getAnimations({subtree:true})` already waits on
  the actual scheduled set, so the extra content animations are awaited for free
  (no count, no sentinel). Reduced-motion: add the content selectors to each
  style's existing `prefers-reduced-motion` `animation: none` block.
- **Tunables:** the `0.45` / `0.5` fractions are the lead/trail. Optionally hoist
  to a `--content-lead` / `--content-trail` var on `TransitionDefinition` if
  per-style tuning is wanted; default-and-see first.

### B. Everything-in-a-shell discipline  *(authoring; `spikes/morph-demo`)*

A `MorphRouteStage` is a **page well**: every visible element inside it should be
a `Morph*` shell, or it hard-cuts. Rework the demo pages so the header and
section labels ride the morph — e.g. wrap each page's title/sub in a
`<MorphRaised class="page-head">`, and group section heading + its trigger button
into a shell. Document the rule in [README.md](src/Morph/README.md): *bare
content inside a stage will not morph.* (This is the cheap half of the fix and is
what most directly kills the "page chrome pops" effect.)

### C. Fixed well + persistent chrome  *(showcase; `spikes/morph-demo`)*

Adopt the reference's frame model so the well stops snapping height and the page
stops scrolling as a whole:

- Give the morphing well a **fixed height** with internal `overflow:auto`
  (app-frame layout), instead of document-flow height. Kills the 954→1378px snap
  at the swap (R2) and the height jump the user saw.
- Keep nav/chrome **outside** `<MorphRouteStage>` (already true) and optionally
  add a persistent status chip (`melting… / extruding…`) bound to the stage phase
  to match the reference's affordance. *Provisional / showcase only — not a Morph
  package feature unless a second consumer wants it.*

### D. Scrollbar gutter  *(host CSS; `spikes/morph-demo/wwwroot/css/app.css`)*

One line removes the sideways flick regardless of C:

```css
html { scrollbar-gutter: stable; }
```

Reserves the scrollbar gutter permanently so the centered `.shell` never shifts
when a taller page adds a scrollbar. Pure host concern — **not** the Morph
package. (If C lands with an internal-scroll well, this becomes moot but harmless.)

### E. Optional — a beat of "empty hold"

If the blank-slate moment still feels too quick after A–C, insert a short hold at
the empty midpoint (a tiny delay between exit-settle and enter-start in
`TransitionAsync` / the route handler). **Measure first** — A’s lead/trail
already widens the empty window; only add a timer if the eye still wants it, and
label it provisional.

---

## 5. Build order

1. **D (scrollbar gutter)** — 1 line, instantly removes the flick. *(trivial)*
2. **A (two-tier content layer)** — the reusable engine deliverable. Verify on the
   existing depth-2 nested cards that nested shells still flatten correctly (the
   `:not(.morph-item)` exclusion is the thing to confirm in-browser).
3. **B (shell everything in the demo)** — makes the page chrome melt; this is what
   the user is reacting to.
4. **C (fixed well)** — removes the height snap, makes the demo *look* like the
   reference frame. Bigger demo edit; do after A/B prove the motion.
5. **E (empty hold)** — only if still needed after a real-browser look.

Each step: warning-free build + green `Morph.Tests` + **browser-verified** (it is
a CSS-motion change; the in-tool preview can't see it) + one coherent commit.

---

## 6. Verification

Drive the live demo and compare against the reference frames:

```bash
# re-extract the reference target (local only; artifacts/ is gitignored)
ffmpeg -i "32353.mp4" -vf "fps=5,scale=570:-1" tools/frontend-verify/artifacts/refvid/f%03d.png
```

A diagnostic probe (throwaway, like the one used to find this) should assert,
across a `Gallery → Dashboard` nav:

- **Empty midpoint:** at exit-end, *no* visible content node in the well has
  opacity > ~0.05 (text included, not just shapes).
- **Lead/trail order:** content nodes reach opacity 0 *before* their shell on
  exit; shell reaches opacity 1 *before* its content on enter.
- **Nesting intact:** depth-2 inset still animates its own lift (excluded from the
  content rule) — no "inner panel just vanishes" regression.
- **No console/page errors**, reduced-motion still swaps near-instantly.

Eyeball the burst-captured frames against `f003` (empty shells), `f006` (blank
slate), `f007` (extruding empties) for the four-beat shape.

---

## 7. Risks / open questions

- **Q1 — opacity compounding on nested shells.** Ancestor shell opacity
  multiplies onto nested shells. Both head to 0/1 so it should read as a faster
  fade, not a glitch — but a deeply nested shell could vanish before its own
  flatten plays. *Validate on the demo's depth-2 cards; if it reads wrong, drop
  the shell's own opacity fade and let only `--lift` flatten + the content layer
  carry the fade.*
- **Q2 — content inside wrapper divs.** `.neu-raised > .row > .circle` — the
  `:not(.morph-item)` rule fades `.row`, dragging the nested `.circle` with it.
  Acceptable (everything's going to empty), but note it; the clean fix is to keep
  content-bearing wrappers shallow or mark them.
- **Q3 — is the fixed well (C) in scope for a spike demo?** It's the most
  invasive edit and is showcase-only. A/B/D may already satisfy the complaint;
  gate C on a look after step 3.
- **Q4 — `--content-lead/trail` as `TransitionDefinition` fields?** Only if a
  style needs to differ. Default-and-see (YAGNI).

---

## 8. Non-goals

- No change to the **completion engine**, `PhaseCompletion`, or the
  `getAnimations` seam — they already wait on the real set.
- No change to **sequencing** (exit→swap→enter is already correct).
- No new JS — content staging is pure CSS.
- Not turning the persistent status chip / fixed frame into Morph package
  features unless a real second consumer appears.
