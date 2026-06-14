using System.Text.Json;
using Microsoft.Extensions.Hosting;
using ABox.Domain.Projects;
using ABox.Infrastructure.Paths;
using ABox.Infrastructure.Storage;

namespace ABox.Host;

// Provisional one-time migration: seeds the canonical Project store from the legacy projects.json the
// retired ProjectRegistry used to read. Runs only when the store is empty, so it never fights POST /projects
// or re-imports on a later boot. Removed at L12 with the rest of the rebuild's migration scaffolding.
internal sealed class ProjectsJsonImport(IRepository<Project> store, IOrchestratorPaths paths) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if ((await store.GetAll(ct)).Count > 0 || !File.Exists(paths.ProjectsFile))
            return;

        Dictionary<string, string>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await File.ReadAllTextAsync(paths.ProjectsFile, ct));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A malformed legacy file must not block startup — there is simply nothing to import.
            return;
        }

        foreach (var (name, path) in entries ?? [])
            await store.Add(Project.Create(name, path), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
