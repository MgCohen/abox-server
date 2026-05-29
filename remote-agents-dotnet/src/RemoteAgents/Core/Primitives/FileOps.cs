using System.Text;

namespace RemoteAgents.Primitives;

// Small file-IO helpers for code that doesn't want to think about
// half-written files or append races. Two operations the orchestrator
// reaches for repeatedly:
//
//   - AtomicWriteAsync: write to <path>.tmp, fsync, rename into place.
//     If the process is killed mid-write, the destination either has
//     the old bytes or the new bytes — never half of each.
//
//   - AppendLineAsync: open in append mode with shared-read so a tail
//     -f works, write a single line, flush. Today JsonlSink and
//     ProviderJsonlIngestSink each roll their own; either can switch
//     to this once it's bedded in.
public static class FileOps
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task AtomicWriteAsync(string path, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("AtomicWriteAsync: path required", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Sibling temp file so the rename is on the same volume — cross
        // -volume rename isn't atomic. Suffix is random to avoid clobber
        // when two writers race; per-path lock above would serialize
        // them, but a stray collision here would also be safe.
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];

        var bytes = Utf8NoBom.GetBytes(content);
        await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(bytes, ct);
            await fs.FlushAsync(ct);
            // Flush(true) → flush OS buffers to disk. The .NET runtime
            // implements this on Windows and Linux; macOS is best-effort.
            try { fs.Flush(flushToDisk: true); } catch { }
        }

        // File.Move with overwrite=true uses MoveFileEx(REPLACE_EXISTING)
        // on Windows and rename(2) on Unix — both atomic within a volume.
        File.Move(tmp, path, overwrite: true);
    }

    public static async Task AppendLineAsync(string path, string line, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("AppendLineAsync: path required", nameof(path));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var payload = line.EndsWith('\n') ? line : line + "\n";
        var bytes = Utf8NoBom.GetBytes(payload);

        // FileShare.Read lets a tailing reader see the file grow.
        // FileShare.Write would let two appenders interleave bytes
        // mid-line, which is exactly what this primitive exists to
        // prevent — so it's omitted on purpose.
        await using var fs = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        await fs.WriteAsync(bytes, ct);
        await fs.FlushAsync(ct);
    }
}
