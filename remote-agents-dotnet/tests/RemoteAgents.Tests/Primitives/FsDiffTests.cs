using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class FsDiffTests : IDisposable
{
    private readonly string _root;

    public FsDiffTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-fsdiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Snapshot_skips_node_modules_and_git()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "x");
        File.WriteAllText(Path.Combine(_root, "node_modules", "pkg.json"), "x");
        File.WriteAllText(Path.Combine(_root, "src", "hello.cs"), "x");

        var snap = FsDiff.Snapshot(_root);

        Assert.Contains("src/hello.cs", snap.Keys);
        Assert.DoesNotContain(snap.Keys, k => k.StartsWith(".git/"));
        Assert.DoesNotContain(snap.Keys, k => k.StartsWith("node_modules/"));
    }

    [Fact]
    public void Diff_detects_add_change_remove()
    {
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "alpha");
        File.WriteAllText(Path.Combine(_root, "remove.txt"), "bravo");
        var before = FsDiff.Snapshot(_root);

        // mutate
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "alpha-modified-longer");
        File.Delete(Path.Combine(_root, "remove.txt"));
        File.WriteAllText(Path.Combine(_root, "added.txt"), "charlie");
        var after = FsDiff.Snapshot(_root);

        var diff = FsDiff.Diff(before, after);

        Assert.Contains("added.txt", diff.Added);
        Assert.Contains("remove.txt", diff.Removed);
        Assert.Contains("keep.txt", diff.Changed);
        Assert.Equal(3, diff.All.Count);
    }
}
