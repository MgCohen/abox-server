using System.Reflection;

namespace ABox.Tests.Harness.Tests;

// Discovers the co-located feature test assemblies (ABox.<Owner>.Tests) from the build output, so the harness
// tests can police EVERY suite without a per-feature ProjectReference — that would be the manual wiring
// co-location exists to remove. A co-located assembly is one TestAssemblies.IsFeatureTestAssembly accepts by name
// AND that carries the TestsSourceDir metadata its stub stamps. ABox.Tests.Central and ABox.Tests.Harness.Tests
// are both excluded by the predicate (the enforcer and the central suite are not part of the set they check), and
// the metadata gate is the authoritative backstop. Output is the repo's pinned
// artifacts/bin/<Project>/<config>/<Project>.dll (Directory.Build.props), located from this assembly's own base
// dir. LoadFrom resolves shared deps (Harness, xunit) to the already-loaded copy by identity, so the [Rule] type
// stays the same across assemblies and parity sees the citations.
internal static class Suites
{
    internal const string TestsSourceDirKey = "TestsSourceDir";

    public static IReadOnlyList<Assembly> Colocated()
    {
        var baseDir = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var config = baseDir.Name;
        var binRoot = baseDir.Parent?.Parent
            ?? throw new InvalidOperationException(
                $"Could not locate the build-output root (artifacts/bin) from '{AppContext.BaseDirectory}'.");

        return binRoot.GetDirectories()
            .Where(d => TestAssemblies.IsFeatureTestAssembly(d.Name))
            .Select(d => Path.Combine(d.FullName, config, d.Name + ".dll"))
            .Where(File.Exists)
            .Select(Assembly.LoadFrom)
            .Where(a => SourceDir(a) is not null)
            .ToList();
    }

    public static string? SourceDir(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == TestsSourceDirKey)?.Value;
}
