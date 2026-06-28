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

        // Discover by recursive glob, not by reconstructing <Project>/<config>/<Project>.dll: a layout tweak
        // (an extra TFM subfolder, a renamed dir) would otherwise silently zero the set. Keep only the running
        // config's copy — the glob also sees the other config's bin, and LoadFrom-ing two copies of one assembly
        // name clashes by identity — and dedup by name so a single assembly loads once.
        return binRoot.GetFiles("*.dll", SearchOption.AllDirectories)
            .Where(f => TestAssemblies.IsFeatureTestAssembly(Path.GetFileNameWithoutExtension(f.Name)))
            .Where(f => InConfig(f, binRoot, config))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First().FullName)
            .Select(Assembly.LoadFrom)
            .Where(a => SourceDir(a) is not null)
            .ToList();
    }

    // A dll belongs to the running config when some path segment between it and artifacts/bin is that config —
    // robust to an extra TFM subfolder (<config>/net10.0/) while still excluding the other config's tree.
    private static bool InConfig(FileInfo dll, DirectoryInfo binRoot, string config)
    {
        for (var d = dll.Directory; d is not null && d.FullName != binRoot.FullName; d = d.Parent)
            if (string.Equals(d.Name, config, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static string? SourceDir(Assembly assembly) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(m => m.Key == TestsSourceDirKey)?.Value;

    // GetTypes over a discovered/loaded suite, turning a ReflectionTypeLoadException into one named, actionable
    // failure instead of a raw wall of loader exceptions — a missing/stale dependency reads as "clean rebuild",
    // not a mystery. The guards reflecting over the suites all funnel through here.
    public static IReadOnlyList<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var detail = string.Join("; ", ex.LoaderExceptions
                .Where(e => e is not null).Select(e => e!.Message).Distinct(StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Could not load all types from '{assembly.GetName().Name}' — a referenced assembly is missing or " +
                $"stale; try a clean rebuild (dotnet build after clearing artifacts/). Loader errors: {detail}", ex);
        }
    }
}
