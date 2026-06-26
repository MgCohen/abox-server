using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Paths;
using ABox.Infrastructure.Storage;

namespace ABox.Host.Tests.Wire;

[Collection(WireHostCollection.Name)]
public sealed class ProjectsImportWireTests
{
    [Rule("first boot with an empty store → the legacy projects.json is imported")]
    [Fact]
    public async Task Legacy_projects_json_is_imported_on_boot()
    {
        using var app = new ImportApp();

        using var res = await app.CreateClient().GetAsync("/projects");

        var projects = await res.Content.ReadFromJsonAsync<ProjectDto[]>();
        Assert.NotNull(projects);
        Assert.Contains(projects!, p => p.Name == "imported" && p.Path == app.ProjectPath);
    }

    // Boots the real Host over a throwaway root carrying a one-entry projects.json and an empty StorageRoot,
    // so the one-time importer runs against legacy data the test controls.
    private sealed class ImportApp : WebApplicationFactory<Program>
    {
        public string ProjectPath { get; } = Directory.CreateTempSubdirectory("import-proj-").FullName;
        private readonly string _root = Directory.CreateTempSubdirectory("import-root-").FullName;
        private readonly string _store = Directory.CreateTempSubdirectory("import-store-").FullName;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            File.WriteAllText(
                Path.Combine(_root, "projects.json"),
                $$"""{ "imported": {{JsonSerializer.Serialize(ProjectPath)}} }""");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new StorageRoot(_store));
                services.AddSingleton<IOrchestratorPaths>(new FakePaths(_root));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            foreach (var dir in new[] { ProjectPath, _root, _store })
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
