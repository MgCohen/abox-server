namespace Morph;

public sealed record TransitionDefinition(
    string Name,
    int ExitMs,
    int EnterMs,
    int DepthStep,
    int ScatterMax,
    double ContentLead = 0.45,
    double ContentTrail = 0.5,
    string ExitEase = "linear",
    string EnterEase = "linear")
{
    public string Vars =>
        $"--exit-dur:{ExitMs}ms;--enter-dur:{EnterMs}ms;" +
        $"--depth-step:{DepthStep}ms;--scatter:{ScatterMax}ms;" +
        $"--content-lead:{Num(ContentLead)};--content-trail:{Num(ContentTrail)};" +
        $"--exit-ease:{ExitEase};--enter-ease:{EnterEase}";

    private static string Num(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
