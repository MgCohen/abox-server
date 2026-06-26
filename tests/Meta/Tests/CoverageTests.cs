using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Meta.Tests;

// The load-bearing backstop once a feature's tests no longer sit behind the central protected tree: every
// co-located Tests/ folder on disk must map to a built ABox.<Owner>.Tests assembly whose co-located Rulebook
// and [Rule] tests are in lockstep, per type. A Tests/ folder that ships tests but no assembly — an untested
// feature slipping the net — fails here; so does a co-located Rulebook drifting from its tests.
public class CoverageTests
{
    [Rule("Every co-located feature Tests folder is policed by a built assembly")]
    [Fact]
    public void EveryFeatureTestsFolderIsParityChecked()
    {
        var assemblies = Suites.Colocated();
        var built = assemblies.Select(a => Normalize(Suites.SourceDir(a)!))
            .ToHashSet(StringComparer.Ordinal);
        var onDisk = RepoTree.FeatureTestRoots().Select(Normalize).ToList();

        Assert.NotEmpty(onDisk);

        var unbuilt = onDisk
            .Where(d => !built.Contains(d))
            .Select(d => Path.GetRelativePath(RepoTree.Root, d))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        Assert.True(unbuilt.Count == 0,
            $"""
            These co-located Tests/ folders ship tests but no ABox.<Owner>.Tests assembly was built or discovered
            for them — an untested feature would slip the net:
            {Bullets(unbuilt)}
            Add the feature's test csproj (the scaffold stamps it) so its parity is enforced.
            """);

        foreach (var assembly in assemblies)
            foreach (var type in TypeFolders(Suites.SourceDir(assembly)!))
                ParityGuard.ForColocated(assembly, type).Assert();
    }

    private static IEnumerable<string> TypeFolders(string sourceDir) =>
        Directory.EnumerateDirectories(sourceDir)
            .Where(d => Directory.Exists(Path.Combine(d, "Rulebook")))
            .Select(d => Path.GetFileName(d)!)
            .OrderBy(t => t, StringComparer.Ordinal);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
