using Microsoft.Extensions.Hosting;
using ABox.Domain.Projects;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.Module;

// Provisional: seeds the project store on first run so the read-only slice demonstrates end to end, until
// create/edit in the UI becomes the real source of entries. Remove when create lands.
internal sealed class ProjectSeeder(IRepository<Project> projects) : IHostedService
{
    private static readonly Project[] Seed =
    [
        new(Guid.Parse("3f2a8c10-9b4e-4d21-a7c6-1e0f5b8d2a44"), "Card Framework"),
        new(Guid.Parse("b71d4e92-0c3a-4f88-9a15-6d2e7c4b1f03"), "Scaffold"),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        if ((await projects.GetAll(ct)).Count > 0) return;
        foreach (var project in Seed)
            await projects.Add(project, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
