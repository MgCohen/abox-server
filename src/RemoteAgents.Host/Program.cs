using RemoteAgents.Host;

var builder = WebApplication.CreateBuilder(args);
Composition.AddServices(builder);

var app = builder.Build();
app.UseCors(Composition.CorsPolicy);
Endpoints.Map(app);

app.Run();
