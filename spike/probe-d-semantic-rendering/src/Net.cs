using System.Reflection;
using Microsoft.CodeAnalysis;

namespace ProbeD;

// The set of reference assemblies a throwaway Compilation needs to resolve BCL types (Int32, Guid,
// List<>, Dictionary<>, ValueTuple). We point at the actual runtime's trusted-platform-assemblies
// so int/Guid/List/tuple all resolve. Good enough for a probe; a real impl would use a pinned
// reference pack.
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
            // Fallback: at least core + this assembly's deps, so Guid/List resolve.
            refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            refs.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));
            refs.Add(MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location));
        }
        return refs;
    }
}
