using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class CutoutStyle
{
    public static readonly TransitionDefinition Transition = new(
        Name: "cutout",
        ExitMs: 1540, EnterMs: 1540,
        LayerInterval: 0, Scatter: 0,
        ExitEase: "linear", EnterEase: "linear");

    public static IServiceCollection AddCutout(this IServiceCollection services) =>
        services.AddTransition(Transition);
}
