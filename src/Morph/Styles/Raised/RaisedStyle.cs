using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class RaisedStyle
{
    public const string Name = "raised";

    public static readonly TransitionDefinition Transition = new(
        Name,
        ExitMs: 440, EnterMs: 500,
        LayerInterval: 150, Scatter: 140,
        ExitEase: "cubic-bezier(0.52, 0, 0.74, 0.25)",
        EnterEase: "cubic-bezier(0.34, 1.25, 0.64, 1)");

    public static IServiceCollection AddRaised(this IServiceCollection services) =>
        services.AddTransition(Transition);
}
