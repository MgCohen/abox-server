using ABox.Infrastructure.Storage;

namespace ABox.Domain.Projects;

public sealed record Project : IEntity
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }

    public static Project Create(string name, string path) =>
        new() { Id = Guid.NewGuid(), Name = RequireName(name), Path = RequirePath(path) };

    public Project Rename(string name) => this with { Name = RequireName(name) };

    public Project MoveTo(string path) => this with { Path = RequirePath(path) };

    private static string RequireName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length > 0
            ? trimmed
            : throw new ArgumentException("Project name is required.", nameof(name));
    }

    private static string RequirePath(string path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        return trimmed.Length > 0
            ? System.IO.Path.GetFullPath(trimmed)
            : throw new ArgumentException("Project path is required.", nameof(path));
    }
}
