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
        new("morph", "morph-melt", "morph-strude", 440, 500, 150, 30,
            "cubic-bezier(0.52, 0, 0.74, 0.25)", "cubic-bezier(0.34, 1.25, 0.64, 1)");
}
