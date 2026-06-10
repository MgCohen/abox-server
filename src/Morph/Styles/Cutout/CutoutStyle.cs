using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class CutoutStyle
{
    public const string Name = "cutout";

    public static readonly TransitionDefinition Transition = new(
        Name,
        ExitMs: 700, EnterMs: 1100,
        DepthStep: 0, ScatterMax: 0);

    public static IServiceCollection AddCutout(this IServiceCollection services) =>
        services.AddTransition(Transition);
}
