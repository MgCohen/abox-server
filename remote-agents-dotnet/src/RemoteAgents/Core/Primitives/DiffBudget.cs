using System.Text;

namespace RemoteAgents.Primitives;

public sealed record DiffBudgetOptions(
    // Total byte cap on the returned diff. Once exceeded, remaining
    // files are dropped entirely (replaced with a single elision line).
    int MaxTotalBytes = 200_000,
    // Per-file hunk-line cap. Files whose hunks exceed this are
    // truncated mid-file with an elision marker. The file header
    // (`diff --git`, index, ---, +++) is always preserved.
    int MaxLinesPerFile = 800,
    // Hard cap on the number of files included. After this many, the
    // rest are dropped with a "<N more files elided>" marker.
    int MaxFiles = 50);

public sealed record DiffBudgetResult(
    string Diff,
    int OriginalBytes,
    int OutputBytes,
    int FilesIncluded,
    int FilesElided,
    bool Truncated);

// Trim a unified diff to fit a review prompt. The diff that
// review flows embed in the Codex prompt can be tens of MB for big
// turns — past a few hundred KB it both wastes tokens and risks
// exceeding context. This primitive turns a raw `git diff` into a
// bounded blob with elision markers, preserving every file's header
// (so the reviewer can still see what was touched) and as many hunks
// as the budget allows.
public static class DiffBudget
{
    public static DiffBudgetResult Trim(string diff, DiffBudgetOptions? options = null)
    {
        options ??= new DiffBudgetOptions();
        var originalBytes = Encoding.UTF8.GetByteCount(diff);

        var files = SplitFiles(diff);
        if (files.Count == 0)
            return new DiffBudgetResult(diff, originalBytes, originalBytes, 0, 0, false);

        var includedFiles = Math.Min(files.Count, options.MaxFiles);
        var elidedFiles = files.Count - includedFiles;

        var sb = new StringBuilder();
        var filesIncluded = 0;
        var truncated = elidedFiles > 0;

        for (var i = 0; i < includedFiles; i++)
        {
            var trimmedFile = TrimFile(files[i], options.MaxLinesPerFile, out var fileTruncated);
            var nextBytes = Encoding.UTF8.GetByteCount(trimmedFile);

            if (sb.Length > 0 && Encoding.UTF8.GetByteCount(sb.ToString()) + nextBytes > options.MaxTotalBytes)
            {
                elidedFiles += includedFiles - i;
                truncated = true;
                break;
            }

            sb.Append(trimmedFile);
            if (!trimmedFile.EndsWith('\n')) sb.Append('\n');
            filesIncluded++;
            if (fileTruncated) truncated = true;
        }

        if (elidedFiles > 0)
            sb.Append($"... <{elidedFiles} more file{(elidedFiles == 1 ? "" : "s")} elided to fit budget>\n");

        var output = sb.ToString();
        return new DiffBudgetResult(
            Diff: output,
            OriginalBytes: originalBytes,
            OutputBytes: Encoding.UTF8.GetByteCount(output),
            FilesIncluded: filesIncluded,
            FilesElided: elidedFiles,
            Truncated: truncated);
    }

    // Split on `diff --git` boundaries. Anything before the first one
    // (rare — usually empty) is discarded.
    private static List<string> SplitFiles(string diff)
    {
        var files = new List<string>();
        if (string.IsNullOrEmpty(diff)) return files;

        var lines = diff.Split('\n');
        var current = new StringBuilder();
        var inFile = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git "))
            {
                if (inFile) { files.Add(current.ToString()); current.Clear(); }
                inFile = true;
            }
            if (inFile)
            {
                current.Append(line);
                current.Append('\n');
            }
        }
        if (inFile && current.Length > 0) files.Add(current.ToString());
        return files;
    }

    // Keep the file header (everything through the first `@@ ` hunk
    // header's preceding `+++` line). Cap subsequent hunk lines.
    private static string TrimFile(string file, int maxLines, out bool truncated)
    {
        truncated = false;
        var lines = file.Split('\n');

        // Find the end of the file header: first `@@ ` line marks the
        // start of hunk content. Everything before it stays.
        var headerEnd = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("@@ ")) { headerEnd = i; break; }
        }

        // No hunks (rename-only, mode-change, binary marker, etc.) —
        // include verbatim, no truncation.
        if (headerEnd == 0)
            return file;

        var hunkLines = lines.Length - headerEnd;
        if (hunkLines <= maxLines) return file;

        truncated = true;
        var sb = new StringBuilder();
        for (var i = 0; i < headerEnd; i++) { sb.Append(lines[i]); sb.Append('\n'); }
        for (var i = headerEnd; i < headerEnd + maxLines; i++) { sb.Append(lines[i]); sb.Append('\n'); }
        sb.Append($"... <{hunkLines - maxLines} hunk lines elided>\n");
        return sb.ToString();
    }
}
