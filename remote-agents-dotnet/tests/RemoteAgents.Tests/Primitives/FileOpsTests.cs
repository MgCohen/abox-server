using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class FileOpsTests
{
    [Fact]
    public async Task AtomicWriteAsync_empty_path_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FileOps.AtomicWriteAsync("", "x"));
    }

    [Fact]
    public async Task AppendLineAsync_empty_path_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FileOps.AppendLineAsync("", "x"));
    }

    [Fact]
    public async Task AtomicWriteAsync_creates_file_and_leaves_no_tmp()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-fileops-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await FileOps.AtomicWriteAsync(path, "hello world");
            Assert.Equal("hello world", await File.ReadAllTextAsync(path));

            var leftovers = Directory.GetFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".tmp-*");
            Assert.Empty(leftovers);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task AtomicWriteAsync_overwrites_existing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-fileops-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "before");
            await FileOps.AtomicWriteAsync(path, "after");
            Assert.Equal("after", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task AtomicWriteAsync_creates_missing_parent_dirs()
    {
        var root = Path.Combine(Path.GetTempPath(), "ra-fileops-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "a", "b", "c.txt");
        try
        {
            await FileOps.AtomicWriteAsync(nested, "deep");
            Assert.Equal("deep", await File.ReadAllTextAsync(nested));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AppendLineAsync_appends_and_terminates_with_newline()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-fileops-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            await FileOps.AppendLineAsync(path, "one");
            await FileOps.AppendLineAsync(path, "two\n"); // already has \n; shouldn't double
            await FileOps.AppendLineAsync(path, "three");
            Assert.Equal("one\ntwo\nthree\n", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
