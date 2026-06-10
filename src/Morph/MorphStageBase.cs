using Microsoft.AspNetCore.Components;

namespace Morph;

public abstract class MorphStageBase : ComponentBase
{
    private const int QuietSlackMs = 50;

    [Inject] protected MorphOptions Options { get; set; } = default!;

    protected MorphPhase Phase { get; private set; } = MorphPhase.Idle;
    protected Exception? LoadError { get; private set; }

    protected MorphStageContext RootContext { get; }
    protected int MaxDepth { get; private set; }

    private TaskCompletionSource? _phaseComplete;
    private int _animationGeneration;
    private int _quietWindowMs;

    protected MorphStageBase() => RootContext = new MorphStageContext(0, ReportDepth);

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

    protected void OnAnimationEnd(EventArgs args)
    {
        var generation = ++_animationGeneration;
        _ = ResolveWhenQuiet(generation);
    }

    protected async Task RunPhaseAsync(MorphPhase phase, TransitionDefinition definition)
    {
        Phase = phase;
        _phaseComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _quietWindowMs = definition.LayerInterval + QuietSlackMs;
        StateHasChanged();

        if (Options.ReducedMotion)
            return;

        await Task.WhenAny(_phaseComplete.Task, Task.Delay(Options.Ceiling));
    }

    protected async Task TransitionAsync(TransitionDefinition definition, Func<Task>? load, Action swap)
    {
        LoadError = null;
        try
        {
            var loading = load?.Invoke() ?? Task.CompletedTask;

            await RunPhaseAsync(MorphPhase.Exiting, definition);

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
                    await RunPhaseAsync(MorphPhase.Entering, definition);
                    return;
                }
            }

            swap();
            ResetDepth();
            await RunPhaseAsync(MorphPhase.Entering, definition);
        }
        finally
        {
            Settle();
        }
    }

    protected void ResetDepth() => MaxDepth = 0;

    protected void Settle()
    {
        Phase = MorphPhase.Idle;
        StateHasChanged();
    }

    private async Task ResolveWhenQuiet(int generation)
    {
        await Task.Delay(_quietWindowMs);
        if (generation == _animationGeneration)
            _phaseComplete?.TrySetResult();
    }
}
