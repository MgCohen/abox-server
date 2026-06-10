namespace Morph;

public sealed class MorphStageContext
{
    private readonly Action<int> _reportDepth;

    public MorphStageContext(int depth, Action<int> reportDepth)
    {
        Depth = depth;
        _reportDepth = reportDepth;
    }

    public int Depth { get; }

    public MorphStageContext Child() => new(Depth + 1, _reportDepth);

    public void Report() => _reportDepth(Depth);
}
