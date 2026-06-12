using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Features.Flows.Contracts;

namespace ABox.Features.Flows.Catalog;

public static class CatalogEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/catalog", (FlowCatalog catalog) =>
            catalog.All().Select(d => new FlowInfo(d.Config.Name, d.Config.Description)));
}
