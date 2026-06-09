namespace Morph;

public sealed class MorphOptions
{
    private readonly Dictionary<string, TransitionDefinition> _transitions = new();

    public string Default { get; set; } = "morph";
    public int Ceiling { get; set; } = 1200;
    public int LoadTimeout { get; set; } = 10_000;
    public bool ReducedMotion { get; set; }

    public MorphOptions Add(TransitionDefinition transition)
    {
        _transitions[transition.Name] = transition;
        return this;
    }

    public TransitionDefinition Resolve(string? name)
    {
        var key = name ?? Default;
        if (_transitions.TryGetValue(key, out var transition))
            return transition;

        throw new InvalidOperationException(
            $"No Morph transition named '{key}' is registered. Register it with AddMorph(o => o.Add(...)).");
    }
}
