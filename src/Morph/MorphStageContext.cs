namespace Morph;

public sealed class MorphStageContext
{
    private readonly Action<int> _report;

    public MorphStageContext(int depth, Action<int> report)
    {
        Depth = depth;
        _report = report;
    }

    public int Depth { get; }

    public MorphStageContext Child() => new(Depth + 1, _report);

    public void Report() => _report(Depth);
}
