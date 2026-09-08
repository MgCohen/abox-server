namespace ABox.DocEngine;

public sealed class ParsedBlock
{
    public required string Type { get; init; }
    public string Title { get; set; } = "";
    public string? Group { get; init; }
    public bool Unknown { get; init; }
    public Dictionary<string, string> Attrs { get; } = new();
    public string Body { get; set; } = "";
    public List<string> Lines { get; } = new();
    public List<ParsedBlock> Children { get; } = new();
}
