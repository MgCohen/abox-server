# Spike: getAnimations completion — FINDINGS

**Date:** 2026-06-10 · **Result:** ✅ PASS (6/6) · **Verdict:** adopt
`getAnimations({subtree:true})` as the completion mechanism (conflict A / D6);
retire the sentinel and all counting.

Decides the completion mechanism for
[`PLANS/morph-style-components-refactor.md`](../../PLANS/morph-style-components-refactor.md)
§3 D6. CSS has no "subtree is done" signal, so completion needs *something* from
the scripting layer. This spike proves the Web Animations API path is correct
where the pure-C# alternatives (sentinel, start/end counting) are fragile.

## What it proves

- **Page is pure HTML/CSS** (`index.html`, no `<script>`). The harness
  (`tools/frontend-verify/probe-getanimations.mjs`) defines the *exact* production
  helper and drives the page — mirroring what the C# engine awaits over JS interop.

## Run

```
cd tools/frontend-verify && npm install   # one-time
node probe-getanimations.mjs
```

## Results

```
gap: { resolveMs: 873, countMatchMs: 273 }   interrupt: { rejected, AbortError }
reRun: 793ms   static: 12ms
```

| Check | Result |
|---|---|
| gapped stagger · `getAnimations` waited for the delayed layer (~800ms) | resolved @873ms ✓ |
| gapped stagger · start/end **counting** would false-complete early (~200ms) | counter matched @273ms vs true end @873ms ✓ |
| interruption · `.finished` rejected cleanly (`AbortError`), no hang | rejected/AbortError ✓ |
| interruption · a fresh phase completes normally afterward | resolved @793ms ✓ |
| static phase · empty set resolved instantly (no hang) | resolved @12ms ✓ |
| no console/page errors | clean ✓ |

## The mechanism (production shape)

```js
// morph.js
export function waitForAnimations(el) {
  return new Promise((resolve) => requestAnimationFrame(() => {
    const anims = el.getAnimations({ subtree: true })
      .filter((a) => a.effect?.getComputedTiming().iterations !== Infinity);
    resolve(Promise.all(anims.map((a) => a.finished)));
  }));
}
```
```csharp
// MorphStageBase
try { await Interop.WaitForAnimationsAsync(StageElement); }
catch (JSException) { /* AbortError: phase superseded — _phaseGen guards it */ }
```

## Why this beats the C#-only options

- **vs sentinel:** no per-style hand-sizing (the sentinel had to be ≥ its subtree's
  longest motion or the phase resolved early). Styles stop caring about completion.
- **vs start/end counting:** counting false-completes on *gapped* staggers because
  `animationstart` fires only *after* `animation-delay` — a delayed layer is
  invisible until it starts (proven: matched @273ms on a layer ending @873ms).
  `getAnimations` returns scheduled-but-not-started animations, so gaps are fine.
- Eliminates the engine's silent-hang risk (no timer fallback in the counted model).

## Caveats handled (and the one to verify at build)

Handled in the helper/engine: filter infinite animations; snapshot on the next
frame (`requestAnimationFrame`); catch `AbortError` on interruption.
**Verify at step 5:** `{subtree:true}` support on the lowest target WebView — the
base `getAnimations()` is broadly supported; the `subtree` option shipped slightly
later on some engines (MDN documents a descendant-map fallback). Not a blocker on
current evergreen Chrome/Edge/Firefox/Safari.

## Cost: the JS line (D7)

This introduces the first logic-adjacent JS in the engine, governed by **D7**: JS
is admitted only for browser primitives C#/CSS can't express (a stateless,
promise-returning helper, awaited once per phase, owning nothing). Research
confirmed it's compile-neutral and runs everywhere Blazor WASM runs.
