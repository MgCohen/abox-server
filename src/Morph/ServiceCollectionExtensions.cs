using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMorph(this IServiceCollection services, Action<MorphOptions>? configure = null)
    {
        var options = new MorphOptions().Add(DefaultTransition);
        configure?.Invoke(options);
        services.AddScoped<MorphInterop>();
        return services.AddSingleton(options);
    }

    private static TransitionDefinition DefaultTransition =>
        new("morph", "morph-exit", "morph-enter", 420, 480, 140, "cubic-bezier(0.22, 1, 0.36, 1)");
}
