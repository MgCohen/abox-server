using Microsoft.Extensions.DependencyInjection;

namespace Morph;

public static class ServiceProviderExtensions
{
    public static async Task DetectReducedMotionAsync(this IServiceProvider services)
    {
        var interop = services.GetRequiredService<MorphInterop>();
        var options = services.GetRequiredService<MorphOptions>();
        options.ReducedMotion = await interop.PrefersReducedMotionAsync();
    }
}
