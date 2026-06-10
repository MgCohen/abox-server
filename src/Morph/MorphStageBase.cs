using Microsoft.AspNetCore.Components;

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

    private TaskCompletionSource? _phaseComplete;
    private int _phaseGen;
    private int _animEnd;
    private int _target = -1;
    private bool _countScheduled;

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

    protected void OnAnimationEnd()
    {
        _animEnd++;
        TryComplete();
    }

    private void TryComplete()
    {
        if (_phaseComplete is not null && _target >= 0 && _animEnd >= _target)
            _phaseComplete.TrySetResult();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_phaseComplete is null || _countScheduled)
            return;

        _countScheduled = true;
        _target = await Interop.CountItemsAsync(StageElement);
        TryComplete();
    }

    protected async Task<bool> RunPhaseAsync(MorphPhase phase)
    {
        var generation = ++_phaseGen;
        _phaseComplete?.TrySetResult();

        Phase = phase;
        _animEnd = 0;
        _target = -1;
        _countScheduled = false;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _phaseComplete = complete;
        StateHasChanged();

        if (Options.ReducedMotion)
        {
            if (generation != _phaseGen)
                return false;
            _phaseComplete = null;
            return true;
        }

        await complete.Task;

        if (generation != _phaseGen)
            return false;

        _phaseComplete = null;
        return true;
    }

    protected async Task TransitionAsync(Func<Task>? load, Action swap)
    {
        LoadError = null;
        var loading = load?.Invoke() ?? Task.CompletedTask;

        if (!await RunPhaseAsync(MorphPhase.Exiting))
            return;

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
                return;
            }
        }

        swap();
        ResetDepth();
        if (await RunPhaseAsync(MorphPhase.Entering))
            Settle();
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
