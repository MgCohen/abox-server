using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Flow;
using ABox.Infrastructure.Paths;
using ABox.Infrastructure.Storage;

namespace ABox.Tests.Wire.Support;

// Boots the real Host (Program) over an in-memory TestServer, then swaps three seams so the wire is driven
// deterministically without a live CLI or touching the user's data dir: a temp StorageRoot (so JsonRepository
// reads/writes under a throwaway dir, not ~/.abox — wire tests arrange their own projects there), paths whose
// projects.json points at that empty dir (so the one-time importer no-ops), and a catalog carrying the
// CLI-free StubFlow. ConfigureTestServices runs after the app's own registration, so these win.
public sealed class WireApp : WebApplicationFactory<Program>
{
    public string ProjectDir { get; } = Directory.CreateTempSubdirectory("wire-").FullName;
    public string StorageDir { get; } = Directory.CreateTempSubdirectory("wire-store-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(new StorageRoot(StorageDir));
            services.AddSingleton<IOrchestratorPaths>(new FakePaths(StorageDir));

            var catalog = new FlowCatalog();
            catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub for wire smoke."));
            services.AddSingleton(catalog);
            services.AddTransient<StubFlow>();
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { Directory.Delete(ProjectDir, recursive: true); } catch { /* best-effort cleanup */ }
            try { Directory.Delete(StorageDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
