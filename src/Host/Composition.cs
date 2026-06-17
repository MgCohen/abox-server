using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;
using ABox.Domain.Flow;
using ABox.Domain.Inbox;
using ABox.Domain.Projects;
using ABox.Features.Flows.Module;
using ABox.Features.Git.Module;
using ABox.Features.Inbox.Module;
using ABox.Features.Projects.Module;
using ABox.Infrastructure.Paths;
using ABox.Infrastructure.Storage;

namespace ABox.Host;

internal static class Composition
{
    public const string CorsPolicy = "open";

    public static void AddServices(WebApplicationBuilder builder, Action<FlowCatalog>? flows = null)
    {
        var services = builder.Services;

        // Transport is Tailscale-only with no app-layer auth (feature-map A8), so CORS is wide open.
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddFastEndpoints(o => o.Assemblies = [ProjectsModule.EndpointsAssembly, InboxModule.EndpointsAssembly]);

        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();

        services.AddSingleton(StorageRoot.Default);
        services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddHostedService<ProjectsJsonImport>();

        services.AddSingleton<IInbox, InMemoryInbox>();

        services.AddSingleton<PendingDecisions>();
        services.AddSingleton<IDecisionResolver, InteractiveResolver>();
        services.AddSingleton<AutoResolver>();
        services.AddSingleton<DenyResolver>();
        services.AddSingleton<AutoPolicy>();
        services.AddSingleton<ResolverSelector>();
        services.AddSingleton<IAgentFactory, AgentFactory>();

        services.AddFlows(flows);
        services.AddGit();
    }
}
