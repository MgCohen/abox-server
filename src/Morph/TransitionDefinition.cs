namespace Morph;

public sealed record TransitionDefinition(
    string Name,
    string ExitKeyframes,
    string EnterKeyframes,
    int ExitMs,
    int EnterMs,
    int LayerInterval,
    string Ease)
{
    public string Vars =>
        $"--anim-exit:{ExitKeyframes};--anim-enter:{EnterKeyframes};" +
        $"--exit-dur:{ExitMs}ms;--enter-dur:{EnterMs}ms;" +
        $"--layer-interval:{LayerInterval}ms;--ease:{Ease};";
}
