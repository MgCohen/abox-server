using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RemoteAgents.Web;
using RemoteAgents.Web.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The Host runs at its own origin (Tailscale-only, CORS open). In dev the WASM
// DevServer is a different port, so HostBaseAddress from wwwroot/appsettings.json
// points at the Host; if this bundle is ever served by the Host itself, drop the
// override and same-origin BaseAddress takes over.
var hostBase = builder.Configuration["HostBaseAddress"]
               ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(hostBase) });
builder.Services.AddScoped<HostApiClient>();

await builder.Build().RunAsync();
