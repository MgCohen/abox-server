namespace Probe;

// Minimal template primitives, mirroring the original spike:
//   [Snippet("id")]   marks a REAL, compiling C# method whose body is a template.
//   Block.Of("id")    a compile-time-only statement placeholder — a body slot the emitter
//                     fills with lifted child statements (like IfElse's then/else).

[AttributeUsage(AttributeTargets.Method)]
public sealed class SnippetAttribute(string name) : Attribute
{
    public string Name => name;
}

public static class Block
{
    public static object Of(string id) =>
        throw new InvalidOperationException($"Block.Of(\"{id}\") is a compile-time placeholder; never executed.");
}
