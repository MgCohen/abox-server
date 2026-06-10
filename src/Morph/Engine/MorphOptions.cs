namespace Morph;

public sealed class MorphOptions
{
    private readonly TransitionDefinition[] _transitions;

    public MorphOptions(IEnumerable<TransitionDefinition> transitions) =>
        _transitions = transitions.ToArray();

    public int LoadTimeout { get; set; } = 10_000;
    public bool ReducedMotion { get; set; }

    public string AllVars => string.Concat(_transitions.Select(t => t.Vars));
}
