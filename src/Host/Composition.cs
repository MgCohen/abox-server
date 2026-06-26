using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;
using ABox.Domain.Decisions;
using ABox.Domain.Flow;
using ABox.Domain.Git;
using ABox.Domain.Inbox;
using ABox.Domain.Projects;
using ABox.Features.Decisions.Module;
using ABox.Features.Flows.Module;
using ABox.Features.Git.Contract;
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

        // Public HTTPS via a Cloudflare tunnel with no app-layer auth (feature-map A8), so CORS is wide open.
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddFastEndpoints(o => o.Assemblies =
            [ProjectsModule.EndpointsAssembly, GitModule.EndpointsAssembly, InboxModule.EndpointsAssembly, DecisionsModule.EndpointsAssembly]);

        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();

        services.AddSingleton(StorageRoot.Default);
        services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddHostedService<ProjectsJsonImport>();

        services.AddSingleton<IInbox, Inbox>();
        services.AddSingleton<IDecisions, Decisions>();

        services.AddSingleton<PendingDecisions>();
        services.AddSingleton<IDecisionResolver, InteractiveResolver>();
        services.AddSingleton<AutoResolver>();
        services.AddSingleton<DenyResolver>();
        services.AddSingleton<AutoPolicy>();
        services.AddSingleton<ResolverSelector>();
        services.AddSingleton(ClaudeBox.Confined());
        services.AddSingleton(CodexBox.Confined());
        services.AddSingleton<IAgentFactory, AgentFactory>();

        services.AddFlows(flows);
        services.AddSingleton<IPullRequests, StubPullRequests>();
    }
}
