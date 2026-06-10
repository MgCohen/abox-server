namespace Morph;

public sealed record TransitionDefinition(
    string Name,
    int ExitMs,
    int EnterMs,
    int LayerInterval,
    int Scatter,
    string ExitEase = "linear",
    string EnterEase = "linear")
{
    public string Vars =>
        $"--exit-dur:{ExitMs}ms;--enter-dur:{EnterMs}ms;" +
        $"--layer:{LayerInterval}ms;--scatter:{Scatter}ms;" +
        $"--exit-ease:{ExitEase};--enter-ease:{EnterEase}";
}
