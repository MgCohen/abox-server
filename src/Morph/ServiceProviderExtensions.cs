using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Morph;

public static class ServiceProviderExtensions
{
    public static async Task DetectReducedMotionAsync(this IServiceProvider services)
    {
        var js = services.GetRequiredService<IJSRuntime>();
        var options = services.GetRequiredService<MorphOptions>();

        await using var module = await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Morph/morph.js");
        options.ReducedMotion = await module.InvokeAsync<bool>("prefersReducedMotion");
    }
}
