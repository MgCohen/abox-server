using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Morph;

public sealed class MorphInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async Task<bool> PrefersReducedMotionAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("prefersReducedMotion");
    }

    public async Task WaitForAnimationsAsync(ElementReference stage)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("waitForAnimations", stage);
    }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Morph/morph.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
