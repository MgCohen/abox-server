using RemoteAgents.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Transport is Tailscale-only; CORS is wide open so a WASM bundle served from
// another origin can call the Host. No app-layer auth (feature-map A8).
const string CorsPolicy = "open";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddRemoteAgents();

var app = builder.Build();

app.UseCors(CorsPolicy);
app.MapRemoteAgents();

app.Run();
