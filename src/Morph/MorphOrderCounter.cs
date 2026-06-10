namespace Morph;

public sealed class MorphOrderCounter
{
    private int _next;

    public int Next() => _next++;

    public void Reset() => _next = 0;
}
