namespace ABox.Versioning;

public sealed record SurfaceMember(string Type, string Kind, string Name, string Signature)
{
    public string Key => $"{Type}|{Kind}|{Name}";
}

public sealed record AssemblySurface(string Name, string Hash, IReadOnlyList<SurfaceMember> Members);
