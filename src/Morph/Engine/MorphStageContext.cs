namespace Morph;

public sealed class MorphStageContext
{
    private readonly Action<int> _report;
    private readonly MorphOrderCounter _order;

    public MorphStageContext(int depth, Action<int> report, MorphOrderCounter order)
    {
        Depth = depth;
        _report = report;
        _order = order;
    }

    public int Depth { get; }

    public MorphStageContext Child() => new(Depth + 1, _report, _order);

    public void Report() => _report(Depth);

    public int NextOrder() => _order.Next();
}
