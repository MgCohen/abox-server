using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class InsetStyle
{
    public const string Name = "inset";

    public static readonly TransitionDefinition Transition = new(
        Name,
        ExitMs: 440, EnterMs: 500,
        DepthStep: 250, ScatterMax: 200,
        ExitEase: "cubic-bezier(0.52, 0, 0.74, 0.25)",
        EnterEase: "cubic-bezier(0.34, 1.25, 0.64, 1)");

    public static IServiceCollection AddInset(this IServiceCollection services) =>
        services.AddTransition(Transition);
}
