using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

public sealed record Project : IEntity
{
    public Guid Id { get; init; }
    public required string Name { get; init; }

    public static Project Create(string name) => new() { Id = Guid.NewGuid(), Name = Require(name) };

    public Project Rename(string name) => this with { Name = Require(name) };

    private static string Require(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length > 0
            ? trimmed
            : throw new ArgumentException("Project name is required.", nameof(name));
    }
}
