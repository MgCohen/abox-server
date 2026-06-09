using Microsoft.AspNetCore.Components;

namespace Morph;

public abstract class MorphStageBase : ComponentBase
{
    private const int QuietSlackMs = 50;

    [Inject] protected MorphOptions Options { get; set; } = default!;

    protected MorphPhase Phase { get; private set; } = MorphPhase.Idle;

    private TaskCompletionSource? _phaseComplete;
    private int _animationGeneration;
    private int _quietWindowMs;

    protected string DataPhase => Phase switch
    {
        MorphPhase.Exiting or MorphPhase.Loading => "exit",
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
        try
        {
            var loading = load?.Invoke() ?? Task.CompletedTask;

            await RunPhaseAsync(MorphPhase.Exiting, definition);

            if (!loading.IsCompleted)
            {
                Phase = MorphPhase.Loading;
                StateHasChanged();
                await loading.WaitAsync(TimeSpan.FromMilliseconds(Options.LoadTimeout));
            }

            swap();
            await RunPhaseAsync(MorphPhase.Entering, definition);
            Settle();
        }
        finally
        {
            if (Phase != MorphPhase.Idle)
                Settle();
        }
    }

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
