using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Features.Flows.Contracts;

namespace RemoteAgents.Features.Flows.Catalog;

public static class CatalogEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/catalog", (FlowCatalog catalog) =>
            catalog.All().Select(d => new FlowInfo(d.Config.Name, d.Config.Description)));
}
