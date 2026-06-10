# Morph — phase-completion refactor

**Status:** shipped — event-driven (real `animationend`, exact per-item count).
Verified in real Chrome via Playwright.
**Scope:** `src/Morph` — phase-completion detection only.

## The discovery (why the first two attempts were wrong)

The plan began as "replace the debounce with an `animationend` counter," then
detoured into "Blazor can't deliver `animationend`, so compute a timeout." Both
were wrong. Real-browser validation (Playwright over system Chrome, driving
`spikes/morph-demo`) found the actual root cause:

**`onanimationend` is not in Blazor's built-in event set, so the binding was
inert.** Blazor only attaches a delegated DOM listener for events it knows
(`registerBuiltInEventType`); animation/transition events aren't among them.
`@onanimationend="OnAnimationEnd"` therefore compiled even with **no such method**
— it was a literal attribute, not an event. Native `animationend` fired (a capture
listener saw every one), but no managed handler ran. Completion silently fell
through to the old `Ceiling` timeout (~1200ms flat) the entire time.

The fix is a four-line registration class, **no JavaScript for the event path**:

```csharp
[EventHandler("onanimationend", typeof(EventArgs))]
public static class EventHandlers { }
```

Proof it was the cause: adding this class flipped `@onanimationend` into a real
binding — the build then *failed* demanding the `OnAnimationEnd` handler that had
been missing all along.

## Shipped design: real events, exact per-item count, no fallback

`MorphStageBase` now completes a phase when every animated layer has fired
`animationend`:

- The CSS animates **every** `.morph-item` under a transitioning stage (descendant
  selector — it crosses nested stage boundaries). So expected events == item count.
- At phase start the item count is read once from the DOM via `countItems`
  (`MorphInterop` → `morph.js`). `animationend` bubbles to the stage; each one ticks
  `_animEnd`; the phase completes when `_animEnd >= _target`.
- Bubbling is load-bearing: an outer `MorphRouteStage` receives the events of items
  owned by inner `MorphStage`s, so its count-and-wait is correct across nesting.
- **No timer fallback.** The two cases where no event can arrive short-circuit
  explicitly: reduced motion (engine skips the await) and a zero-item count
  (completes immediately). Nothing else can hang the gate.

`MorphOptions.Ceiling` is deleted. `RunPhaseAsync` lost its `TransitionDefinition`
parameter (no duration math remains). `MorphInterop` consolidates both JS calls
(`prefersReducedMotion`, `countItems`) behind one held module reference.

## Verified (Playwright, real Chrome, morph-demo)

Completion now lands on the **true last `animationend`**, scaling with content:

| Transition | Items | Phase ends at | Matches |
|---|---|---|---|
| swap exit (screen, depth 2) | 4 | 4th event (~729ms) | depth-0 root finishing at 2·140+420=700ms |
| swap enter | 4 | 8th event (~1505ms) | last layer |
| route Home→B exit | 5 (4 swap + 1 load-demo) | 5th event (~700ms) | full subtree — **old nested early-cut (~510ms) fixed** |
| reduced motion | 0 | immediate, content swaps, no hang | engine skips await |

## Known edges

- **Simultaneous nested transitions** (an inner stage mid-swap *while* an outer
  route transition starts) are not modeled — the trigger guards (`Phase != Idle`)
  make them rare, and the prior code didn't handle them either.
- **One animation per item** is the model; an item running multiple/iterating
  animations would fire `animationend` more than once and over-count. Per-shape
  extras must stay single-run (the README already requires disjoint properties).
- **Foreign `animationend`** from non-Morph CSS inside a stage would also bubble and
  be counted. Not observed in practice; if it ever matters, the fix is custom event
  args carrying `animationName` (`registerCustomEventType`) to filter by keyframe —
  deliberately not built (extra JS for a hypothetical).
