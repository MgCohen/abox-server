namespace Morph;

public sealed record TransitionDefinition(
    string Name,
    string ExitKeyframes,
    string EnterKeyframes,
    int ExitMs,
    int EnterMs,
    int LayerInterval,
    int Scatter,
    string ExitEase,
    string EnterEase)
{
    public string Vars =>
        $"--anim-exit:{ExitKeyframes};--anim-enter:{EnterKeyframes};" +
        $"--exit-dur:{ExitMs}ms;--enter-dur:{EnterMs}ms;" +
        $"--layer:{LayerInterval}ms;--scatter:{Scatter}ms;" +
        $"--exit-ease:{ExitEase};--enter-ease:{EnterEase};";
}
