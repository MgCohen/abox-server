using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Flow;
using ABox.Infrastructure.Projects;

namespace ABox.Tests.Wire.Support;

// Boots the real Host (Program) over an in-memory TestServer, then swaps two seams so the wire is driven
// deterministically without a live CLI or a projects.json: a fake project registry (resolves to a temp dir,
// lists one entry) and a catalog carrying the CLI-free StubFlow. ConfigureTestServices runs after the app's
// own registration, so these win.
public sealed class WireApp : WebApplicationFactory<Program>
{
    public string ProjectDir { get; } = Directory.CreateTempSubdirectory("wire-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IProjectRegistry>(new FakeProjects(ProjectDir));

            var catalog = new FlowCatalog();
            catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub for wire smoke."));
            services.AddSingleton(catalog);
            services.AddTransient<StubFlow>();
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            try { Directory.Delete(ProjectDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
