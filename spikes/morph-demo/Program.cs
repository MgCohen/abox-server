using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Morph;
using MorphDemo;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMorph(o =>
{
    o.LoadTimeout = 1500;
    o.Add(new TransitionDefinition(
        "slide", "slide-exit", "slide-enter", 300, 340, 60, 20,
        "cubic-bezier(0.4, 0, 0.2, 1)", "cubic-bezier(0.4, 0, 0.2, 1)"));
});
builder.Services.AddSingleton<DemoTransitionState>();

var host = builder.Build();
await host.Services.DetectReducedMotionAsync();
await host.RunAsync();
