using RemoteAgents.Tests.Structure.Support;
using static RemoteAgents.Tests.Structure.Support.HomeFolders;

namespace RemoteAgents.Tests.Structure.Tests;

// Physical-placement guard: reads the project folders on disk (SourceTree), not the loaded assembly
// graph, so it catches a stray project the moment it lands — even uncompiled or arch-excluded code.
// Namespace-matches-folder is enforced separately by IDE0130 at compile time.
public class StructureTests
{
    [Rule("Every project lives under an agreed home folder")]
    [Fact]
    public void EveryProjectUnderAHomeFolder()
    {
        var strays = SourceTree.ProjectTopSegments()
            .Where(seg => !IsHome(seg) && !IsPendingEviction(seg))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Projects live under folders that are not an agreed home:
            {Bullets(strays)}
            Agreed home folders:
            {Bullets(Agreed)}
            """);

        // The eviction list must not outlive its folders — a stale entry is a silent allow-hole.
        var stale = PendingEviction.Where(f => !SourceTree.HasTopSegment(f)).ToList();
        Assert.True(stale.Count == 0,
            $"""
            These folders are gone but still listed in PendingEviction:
            {Bullets(stale)}
            Drop them from HomeFolders.PendingEviction.
            """);
    }

    [Rule("No build output lives under src or tests")]
    [Fact]
    public void NoBuildOutputUnderSource()
    {
        var strays = SourceTree.StrayBuildOutput();
        Assert.True(strays.Count == 0,
            $"""
            Build output (bin/obj/artifacts) escaped into the source tree:
            {Bullets(strays)}
            The only legal artifacts home is the repo-root /artifacts (UseArtifactsOutput + a pinned
            ArtifactsPath). A stray here means a project escaped the root Directory.Build.props.
            """);
    }

    [Rule("Every folder under tests holds a registered test type")]
    [Fact]
    public void EveryTestFolderIsARegisteredType()
    {
        var strays = SourceTree.TestTypeFolders()
            .Where(f => !TestTypes.IsRegistered(f) && !TestTypes.IsNonType(f))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Folders under tests/Tests/ are not a registered test type:
            {Bullets(strays)}
            Registered test types:
            {Bullets(TestTypes.Registered)}
            Register the type in TestTypes.Registered, or move the folder under shared Support.
            """);
    }

    [Rule("Every test lives inside a registered test type")]
    [Fact]
    public void EveryTestInsideARegisteredType()
    {
        var misplaced = typeof(StructureTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(TestMarkers.Marks)
            .Where(m => !TestTypes.ContainsTest(m.DeclaringType?.Namespace))
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"""
            Tests live outside the formal RemoteAgents.Tests.<Type>.Tests structure, where the per-type
            ParityGuard cannot see them to require a [Rule]:
            {Bullets(misplaced)}
            Move each into a registered type's Tests/ folder.
            """);
    }

    [Rule("Every run attribute is a registered test marker")]
    [Fact]
    public void EveryRunAttributeIsARegisteredMarker()
    {
        var unregistered = typeof(StructureTests).Assembly.GetTypes()
            .Where(t => typeof(FactAttribute).IsAssignableFrom(t))
            .Where(t => !TestMarkers.Names.Contains(t.Name, StringComparer.Ordinal))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(unregistered.Count == 0,
            $"""
            Run attributes (Xunit.FactAttribute subtypes) in the test suite are not registered markers, so the
            tests they mark slip past every [Rule] citation check:
            {Bullets(unregistered)}
            Add each to TestMarkers.Names so parity and placement see the tests it marks.
            """);
    }

    private static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));
}
