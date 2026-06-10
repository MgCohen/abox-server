namespace Morph;

public sealed record TransitionDefinition(
    string Name,
    int ExitMs,
    int EnterMs,
    int LayerInterval,
    int Scatter,
    string ExitEase,
    string EnterEase)
{
    public string Vars =>
        $"--{Name}-exit-dur:{ExitMs}ms;--{Name}-enter-dur:{EnterMs}ms;" +
        $"--{Name}-layer:{LayerInterval}ms;--{Name}-scatter:{Scatter}ms;" +
        $"--{Name}-exit-ease:{ExitEase};--{Name}-enter-ease:{EnterEase};";
}
