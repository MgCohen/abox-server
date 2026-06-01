using System.Text.Json.Serialization;
using RemoteAgents.Flows;
using RemoteAgents.Host;

var builder = WebApplication.CreateBuilder(args);

// Transport is Tailscale-only; CORS is wide open so a WASM bundle served from
// another origin can call the Host. No app-layer auth (feature-map A8).
const string CorsPolicy = "open";
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// String enums on the wire (FlowPhase/StepStatus render as names).
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddRemoteAgents();

// The flow catalog, declared once at the root (R-SPINE-2). L2: just the stub.
builder.Services.AddFlow<StubFlow>("stub", "Walking-skeleton stub: placeholder steps, no real work.");

var app = builder.Build();

app.UseCors(CorsPolicy);
app.MapRemoteAgents();

app.Run();
