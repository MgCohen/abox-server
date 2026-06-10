using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMorph(this IServiceCollection services, Action<MorphOptions>? configure = null)
        => services
            .AddRaised()
            .AddInset()
            .AddCutout()
            .ConfigureMorph(configure);

    public static IServiceCollection AddTransition(this IServiceCollection services, TransitionDefinition transition)
        => services.AddSingleton(transition);

    public static IServiceCollection ConfigureMorph(this IServiceCollection services, Action<MorphOptions>? configure)
    {
        services.AddScoped<MorphInterop>();
        services.AddSingleton(sp =>
        {
            var options = new MorphOptions(sp.GetServices<TransitionDefinition>());
            configure?.Invoke(options);
            return options;
        });
        return services;
    }
}
