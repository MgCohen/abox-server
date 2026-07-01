using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Probe;

// Reference assemblies for the throwaway resolution Compilation (reused infra,
// identical to the slice). Points at the running runtime's trusted-platform-
// assemblies so BCL types (Guid, List<>, decimal, ...) resolve.
static class Net
{
    public static IReadOnlyList<MetadataReference> ReferenceAssemblies()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "";
        var refs = new List<MetadataReference>();
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                refs.Add(MetadataReference.CreateFromFile(path));
        }
        if (refs.Count == 0)
        {
            refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location));
        }
        return refs;
    }
}
