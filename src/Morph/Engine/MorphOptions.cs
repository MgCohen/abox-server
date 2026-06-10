namespace Morph;

public sealed class MorphOptions
{
    private readonly Dictionary<string, TransitionDefinition> _transitions;

    public MorphOptions(IEnumerable<TransitionDefinition> transitions) =>
        _transitions = transitions.ToDictionary(t => t.Name);

    public int LoadTimeout { get; set; } = 10_000;
    public bool ReducedMotion { get; set; }

    public TransitionDefinition Resolve(string name)
    {
        if (_transitions.TryGetValue(name, out var transition))
            return transition;

        throw new InvalidOperationException(
            $"No Morph transition named '{name}' is registered. A style registers it with AddTransition(...).");
    }
}
