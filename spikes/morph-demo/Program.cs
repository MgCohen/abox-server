using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Morph;
using MorphDemo;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMorph(o => { o.LoadTimeout = 1500; o.SwapDelay = 0; });

var host = builder.Build();
await host.Services.DetectReducedMotionAsync();
await host.RunAsync();
