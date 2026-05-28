using System.Reflection;

namespace Flows.Agents;

internal static class Prompts
{
    private static readonly Assembly Asm = typeof(Prompts).Assembly;

    public static string Load(string name)
    {
        var resourceName = $"Flows.Agents.prompts.{name}.md";
        using var stream = Asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded prompt not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
