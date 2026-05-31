using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using RemoteAgents.UI.Components.Api;
using RemoteAgents.UI.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// When this WASM bundle is served by RemoteAgents.Host itself, BaseAddress
// is exactly what we want — same origin, no CORS. When developed against
// a separately-running Host (different port), HostBaseAddress override
// from appsettings.json wins.
var hostBase = builder.Configuration["HostBaseAddress"]
               ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(hostBase) });
builder.Services.AddScoped<HostApiClient>();

await builder.Build().RunAsync();
