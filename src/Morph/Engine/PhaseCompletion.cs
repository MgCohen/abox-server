namespace Morph;

internal sealed class PhaseCompletion
{
    private TaskCompletionSource? _pending;
    private int _generation;
    private bool _waitScheduled;

    public (int Generation, Task Completion) Begin()
    {
        var generation = ++_generation;
        _pending?.TrySetResult();
        _waitScheduled = false;
        _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return (generation, _pending.Task);
    }

    public bool IsCurrent(int generation) => generation == _generation;

    public void Clear() => _pending = null;

    public bool TryBeginWait(out int generation)
    {
        generation = _generation;
        if (_pending is null || _waitScheduled)
            return false;

        _waitScheduled = true;
        return true;
    }

    public void CompleteIfCurrent(int generation)
    {
        if (generation == _generation)
            _pending?.TrySetResult();
    }
}
