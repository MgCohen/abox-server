using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Morph;

public abstract class MorphStageBase : ComponentBase
{
    [Inject] protected MorphOptions Options { get; set; } = default!;
    [Inject] protected MorphInterop Interop { get; set; } = default!;

    protected MorphPhase Phase { get; private set; } = MorphPhase.Idle;
    protected Exception? LoadError { get; private set; }

    protected MorphStageContext RootContext { get; }
    protected int MaxDepth { get; private set; }
    protected ElementReference StageElement { get; set; }

    private readonly MorphOrderCounter _order = new();
    private readonly PhaseCompletion _completion = new();

    protected MorphStageBase() => RootContext = new MorphStageContext(0, ReportDepth, _order);

    private void ReportDepth(int depth)
    {
        if (depth <= MaxDepth)
            return;

        MaxDepth = depth;
        StateHasChanged();
    }

    protected string DataPhase => Phase switch
    {
        MorphPhase.Exiting or MorphPhase.Loading or MorphPhase.Error => "exit",
        MorphPhase.Entering => "enter",
        _ => string.Empty,
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_completion.TryBeginWait(out var generation))
            return;

        try
        {
            await Interop.WaitForAnimationsAsync(StageElement);
        }
        catch (JSException)
        {
            // AbortError: the phase was superseded mid-flight; the generation check discards it.
        }

        _completion.CompleteIfCurrent(generation);
    }

    protected async Task<bool> RunPhaseAsync(MorphPhase phase)
    {
        var (generation, completion) = _completion.Begin();
        Phase = phase;
        StateHasChanged();

        if (!Options.ReducedMotion)
            await completion;

        if (!_completion.IsCurrent(generation))
            return false;

        _completion.Clear();
        return true;
    }

    protected async Task<MorphTransitionOutcome> TransitionAsync(Func<Task>? load, Action swap)
    {
        LoadError = null;
        var loading = load?.Invoke() ?? Task.CompletedTask;

        if (!await RunPhaseAsync(MorphPhase.Exiting))
            return MorphTransitionOutcome.Superseded;

        if (!loading.IsCompleted)
        {
            Phase = MorphPhase.Loading;
            StateHasChanged();
            try
            {
                await loading.WaitAsync(TimeSpan.FromMilliseconds(Options.LoadTimeout));
            }
            catch (TimeoutException)
            {
                LoadError = new TimeoutException(
                    $"Load did not finish within the {Options.LoadTimeout}ms LoadTimeout budget.");
                Phase = MorphPhase.Error;
                StateHasChanged();
                if (await RunPhaseAsync(MorphPhase.Entering))
                    Settle();
                return MorphTransitionOutcome.LoadFailed;
            }
        }

        swap();
        ResetDepth();
        await HoldAtEmptyAsync();
        if (await RunPhaseAsync(MorphPhase.Entering))
            Settle();
        return MorphTransitionOutcome.Completed;
    }

    protected async Task HoldAtEmptyAsync()
    {
        if (Options.ReducedMotion || Options.SwapDelay <= 0)
            return;

        await Task.Delay(Options.SwapDelay);
    }

    protected void ResetDepth()
    {
        MaxDepth = 0;
        _order.Reset();
    }

    protected void Settle()
    {
        Phase = MorphPhase.Idle;
        StateHasChanged();
    }
}
