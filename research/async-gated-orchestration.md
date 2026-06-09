---
type: research
status: proposal
tags: [#ui, #blazor, #wasm, #transitions, #async]
---

# Async-gated transition orchestration

How hard is it to turn the fixed `melt → swap → extrude` timer sequence
(`spikes/ui-shell/Pages/Home.razor`) into a phase state-machine where a phase
can **await arbitrary work of unknown duration** — e.g. melt partway, await a
real content load like `RunView.razor`'s `Api.GetFlowAsync`, then extrude once
content is ready, never extruding into an empty/loading screen.

**Difficulty: MEDIUM.** The C# control flow is genuinely easy (an `async`
method already sequences the phases). The medium-ness is entirely in the
*timing and edge cases*: a resting "melted" state that holds for unknown time,
the min-duration floor so a fast load doesn't flash, a timeout/error branch,
and — the one real trap — keeping CSS animation state in sync when a phase that
*finishes its animation* must then sit and wait. The render-barrier lesson from
the spike (`FINDINGS.md` problems 1–2) is the thing most likely to bite.

## What we have today

`Home.razor` `SwitchTo` is a 3-state machine driven purely by `Task.Delay`:

```
_phase = Melting; StateHasChanged();  await Delay(meltDur + stagger + 60);
_view  = target;  _phase = Extruding; StateHasChanged(); await Delay(extrudeDur + stagger + 60);
_phase = Idle;    StateHasChanged();
```

The CSS does the visible work: `.stage.melting .morph-item` runs the `melt`
keyframe `forwards` (ends flat/invisible and *stays* there), `.stage.extruding`
runs `extrude` `backwards`. The `Task.Delay` values are hand-tuned to outlast
`animation-duration + last stagger`. There is no content load in the loop — the
view swap is a synchronous field assignment between two static stub components.

`RunView.razor` is the real load shape we must wrap: `OnInitializedAsync` awaits
`Api.GetFlowAsync(Id)` (variable duration, may return null → redirect), then for
active runs spins an SSE stream that mutates `_snap` over time. So "content is
ready" = the first snapshot has arrived (not the whole stream — the stream keeps
running after extrude).

## The phase model

Five phases instead of three. The new ones are the **gate** (`Loading`, a
resting melted state) and the terminal **error** path.

```
Idle ─▶ Melting ─▶ Loading ─▶ Extruding ─▶ Idle
                      │
                      └─(load failed / timeout)─▶ Extruding(error view) ─▶ Idle
```

- **Melting** — same as today: `.melting` class, await the melt animation. But
  we *also* kick off the content load concurrently at the very start of melt, so
  the load's clock and the melt's clock overlap (the load gets the melt duration
  for free).
- **Loading** — a *resting* state held while content loads. The morph-items have
  finished `melt forwards`, so they're already flat/invisible; we just need the
  stage to stay in that visual state and show a loading affordance (spinner /
  skeleton in the emptied stage). This is the new, unbounded-duration phase.
- **Extruding** — unchanged mechanism, but only entered once content is ready
  *and* the min-melt floor has elapsed.
- **Error** — load failed or timed out; extrude an error view instead of
  content. Reuses the Extruding mechanism with a different view payload.

### Orchestrator sketch

```csharp
private enum Phase { Idle, Melting, Loading, Extruding }

private async Task SwitchToAsync<T>(
    View target,
    Func<CancellationToken, Task<T>> load,   // the real async content load
    Action<T> commit)                          // assign loaded data into view state
{
    if (_phase != Phase.Idle) return;

    var floor = TimeSpan.FromMilliseconds(_active.MeltMs);   // min-melt floor
    var budget = TimeSpan.FromSeconds(10);                   // max before we give up
    using var cts = new CancellationTokenSource(budget);

    // 1) start load + melt concurrently — load reuses the melt time for free.
    var loadTask = load(cts.Token);
    var meltStart = Stopwatch.GetTimestamp();

    _phase = Phase.Melting;
    await RenderAndSettleAsync();                 // commit .melting, then await the melt animation
    await DelayForAnimation(_active.MeltMs);

    // 2) resting melted state while load finishes (unknown duration).
    _phase = Phase.Loading;
    StateHasChanged();                            // shows the loading affordance

    T data;
    try
    {
        data = await loadTask;                    // the gate: await real work
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        data = default!;                          // timeout → error view below
        _loadError = "Timed out loading.";
    }
    catch (Exception ex)
    {
        data = default!;
        _loadError = ex.Message;                  // surface, don't swallow (oracle / FINDINGS lesson)
    }

    // 3) min-melt floor: if the load was faster than the floor, hold the rest.
    var elapsed = Stopwatch.GetElapsedTime(meltStart);
    if (elapsed < floor) await Task.Delay(floor - elapsed);

    // 4) swap view payload, then extrude.
    if (_loadError is null) commit(data);
    _view = target;                               // or View.Error
    _phase = Phase.Extruding;
    await RenderAndSettleAsync();                 // commit .extruding + new DOM before its animation runs
    await DelayForAnimation(_active.ExtrudeMs);

    _phase = Phase.Idle;
    StateHasChanged();
}
```

`DelayForAnimation(ms)` = `Task.Delay(ms + MaxStaggerIndex * _active.StaggerMs + 60)`
— the existing "outlast the keyframe + stagger" rule, extracted.

The key structural change vs today: **the load is started before/at melt, not
after the swap.** That overlaps the cheapest possible time (melt is happening
anyway) with the load, so the only *visible* extra wait is `max(0, loadTime −
meltTime)` spent in the resting `Loading` state. For loads that finish within
the melt window, the user sees the exact same melt→extrude they see now, with no
loading flash — which is the whole goal.

## The CSS side (no JS interop)

Two small additions to `app.css`; the existing melt/extrude keyframes are
untouched.

1. **A resting melted class.** When `melt` runs `forwards` the items already
   hold their end-state (flat, opacity 0), so a long `Loading` phase mostly
   needs the stage to *keep* the `.melting` end-state and show an affordance. The
   clean way is a dedicated `.stage.melted` (or `.loading`) rule that pins the
   end-state without re-running the animation:

   ```css
   .stage.melted .morph-item {
       opacity: 0;
       transform: scale(0.95) translateY(6px);
       box-shadow: none;          /* matches melt's "to" frame */
   }
   .stage.loading .stage-loader { opacity: 1; }   /* spinner/skeleton fades in */
   ```

   We switch the class from `.melting` to `.melted`/`.loading` only *after* the
   melt animation has played, so there's no visual jump (the held state ==
   keyframe end state).

2. **A loading affordance in the emptied stage** — a `.stage-loader` element
   (spinner or 2–3 skeleton cards) that lives outside `.morph-item` so it isn't
   itself melted, fading in during `Loading`. Honor `prefers-reduced-motion` (it
   already kills the morph animations).

`extrude` already runs `backwards`, so its `from` frame applies during the
`animation-delay` window — meaning the new content is invisible until its
stagger slot, which is exactly what prevents a flash of un-extruded content.

## The render-barrier lesson (the one real trap)

`FINDINGS.md` problems 1–2: `StateHasChanged()` can re-enter render synchronously
and the new DOM/class isn't guaranteed committed before the next `await`. The
spike fixed the *symptom* (NRE + rAF deadlock) by dropping View Transitions; here
we keep CSS animations but still must guarantee **the class change is committed
before we start timing its animation**, or the animation can be skipped/clipped.

For pure CSS keyframe animations this is *less* fragile than the View-Transitions
path (no JS promise that rejects, no rAF) — but the swap-then-immediately-time
pattern is the same risk. The robust, interop-free barrier is a
`TaskCompletionSource` completed in `OnAfterRenderAsync`:

```csharp
private TaskCompletionSource? _rendered;

private async Task RenderAndSettleAsync()
{
    _rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var barrier = _rendered;        // hold in a local — FINDINGS bug 1 was a re-entrancy NRE
    StateHasChanged();
    await barrier.Task;             // resolves after the DOM with the new class is committed
}

protected override Task OnAfterRenderAsync(bool firstRender)
{
    var barrier = _rendered;
    _rendered = null;
    barrier?.TrySetResult();
    return Task.CompletedTask;
}
```

Two carried-forward rules from the spike: **hold the TCS in a local** (re-entrant
render nulled the field mid-flight → NRE), and **no rAF** (the `OnAfterRender`
barrier already guarantees commit; an rAF inside an update callback deadlocks).
`RunContinuationsAsynchronously` keeps the continuation off the render reentrancy
path.

We only *need* the barrier at the two moments a class change is immediately
followed by timing an animation (entering `Melting`, entering `Extruding`). The
`Loading` transition is a plain `StateHasChanged()` — no animation to race.

## Wrapping a real load like RunView

`RunView`'s load decomposes cleanly into the `load`/`commit` delegates:

```csharp
await SwitchToAsync(
    View.Run,
    load: async ct =>
    {
        var snap = await Api.GetFlowAsync(Id);          // the variable-duration gate
        if (snap is null) throw new ContentMissingException();  // → error path, or redirect before transition
        return snap;
    },
    commit: snap =>
    {
        _snap = snap;
        if (IsActive(snap.Phase)) StartSseStream(Id);   // stream starts AFTER extrude; it mutates _snap live
    });
```

Important boundary: **only the first snapshot is the gate.** The SSE stream
(`FlowStreamClient.StreamAsync`) is long-lived and must *not* block the
transition — it starts in `commit`, after which normal `StateHasChanged` updates
drive the already-extruded view. The null-snapshot redirect in `RunView` becomes
either a pre-transition guard (redirect before melting) or the error path.

## Risks

- **Render-barrier re-entrancy (highest).** Exactly the spike's bug 1. Mitigated
  by the local-held TCS + `RunContinuationsAsynchronously` + no rAF. Must be
  verified with a *visible* browser (the headless preview hid this last time).
- **Animation/await drift.** `Task.Delay` only approximates the CSS clock; under
  GC pause or a slow Debug-WASM frame the class can flip before the keyframe
  visually finishes. Tolerable (it's a 60ms pad today), but a long resting
  `Loading` makes any mismatch more noticeable. Animation-driven barriers
  (`animationend` via a tiny `@onanimationend` Blazor handler, no JS interop)
  would be exact — a fallback if `Task.Delay` proves too loose.
- **Cancellation / re-entrancy of the transition itself.** User clicks again
  mid-load. Today `_phase != Idle` guards re-entry; with an unbounded `Loading`
  phase that guard window is much longer, so a "cancel current transition" path
  (cancel the load CTS, snap to Idle/previous view) likely becomes a real
  requirement, not optional.
- **Timeout UX.** A 10s budget that ends in an error extrude is fine; but if the
  real content (an active run) legitimately takes longer to *first-snapshot*,
  the budget must be tuned to the API's p99, not guessed.
- **Min-floor vs perceived stall.** The floor prevents a flash on fast loads but
  *adds* latency to them. Keep it == melt duration (already "spent") so it's free.

## Minimal experiment to prove it

Smallest thing that proves the gate + barrier + floor work, before touching the
real `RemoteAgents.Web`:

1. In the `spikes/ui-shell` `Home.razor`, replace the swap-and-`Task.Delay` with
   `SwitchToAsync` above, and make `GoToRun`'s `load` an **artificial async with
   injectable duration**: `async ct => { await Task.Delay(_fakeLoadMs, ct); return Unit; }`.
2. Add the `.stage.melted/.loading` rule + a `.stage-loader` spinner to `app.css`.
3. Drive three cases from the existing motion-control buttons (or a slider):
   - **fast load** (`_fakeLoadMs` < melt): must look identical to today, *no*
     loader flash (the floor + overlap absorb it).
   - **slow load** (`_fakeLoadMs` ≫ melt, e.g. 3s): stage rests melted, loader
     visible, extrude fires only after the load resolves.
   - **failing load** (`throw`): extrudes an error placeholder; never sits blank.
4. Verify in a **visible** Chrome via the existing Playwright `capture2.js`
   harness (`FINDINGS.md` Observability) — the headless preview will hide a
   render-barrier regression, which is exactly the class of bug we're guarding.

Pass criteria: fast case is visually unchanged from current behavior; slow case
holds the resting state and never extrudes into emptiness; error case extrudes a
real error view; no skipped/clipped melt or extrude across ~10 runs (the
render-barrier proof).

If that holds, lifting it into `RemoteAgents.Web` is mechanical: swap the
artificial `load` delegate for the `Api.GetFlowAsync` + start-SSE-in-`commit`
shape shown above.
