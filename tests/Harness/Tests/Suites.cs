using System.Reflection;

namespace ABox.Tests.Harness.Tests;

// Discovers the co-located feature test assemblies (ABox.<Owner>.Tests) from the build output, so the harness
// tests can police EVERY suite without a per-feature ProjectReference — that would be the manual wiring
// co-location exists to remove. A co-located assembly is the one carrying the TestsSourceDir metadata its stub
// stamps. ABox.Tests.Central is excluded by the .Tests suffix filter; ABox.Tests.Harness.Tests itself also ends
// in .Tests but stamps no TestsSourceDir, so the gate excludes it (the enforcer is not part of the set it
// checks). Output is the repo's pinned artifacts/bin/<Project>/<config>/<Project>.dll (Directory.Build.props),
// located from this assembly's own base dir. LoadFrom resolves shared deps (Harness, xunit) to the
// already-loaded copy by identity, so the [Rule] type stays the same across assemblies and parity sees the
// citations.
internal static class Suites
{
    private const string TestsSourceDirKey = "TestsSourceDir";

    public static IReadOnlyList<Assembly> Colocated()
    {
        var baseDir = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var config = baseDir.Name;
        var binRoot = baseDir.Parent?.Parent
            ?? throw new InvalidOperationException(
                $"Could not locate the build-output root (artifacts/bin) from '{AppContext.BaseDirectory}'.");

        return binRoot.GetDirectories()
            .Where(d => IsFeatureTestProject(d.Name))
            .Select(d => Path.Combine(d.FullName, config, d.Name + ".dll"))
            .Where(File.Exists)
            .Select(Assembly.LoadFrom)
            .Where(a => SourceDir(a) is not null)
            .ToList();
    }

    public static string? SourceDir(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == TestsSourceDirKey)?.Value;

    private static bool IsFeatureTestProject(string name) =>
        name.StartsWith("ABox.", StringComparison.Ordinal)
        && name.EndsWith(".Tests", StringComparison.Ordinal);
}
