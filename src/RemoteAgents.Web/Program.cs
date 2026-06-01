using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RemoteAgents.Web;
using RemoteAgents.Web.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In dev the WASM DevServer is a different origin, so HostBaseAddress points at the Host;
// if the Host ever serves this bundle, drop it and same-origin BaseAddress takes over.
var hostBase = builder.Configuration["HostBaseAddress"]
               ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(hostBase) });
builder.Services.AddScoped<HostApiClient>();

await builder.Build().RunAsync();
