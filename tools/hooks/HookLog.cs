using System.Text;

namespace ABox.Governance.Hooks;

public static class HookLog
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Append(string logPath, HookEvent e)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (dir is not null) Directory.CreateDirectory(dir);
        File.AppendAllText(logPath, e.ToJsonl() + "\n", Utf8);
    }

    public static HookLogSlice ReadSince(string logPath, long offset)
    {
        if (!File.Exists(logPath)) return new HookLogSlice([], offset);

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset > stream.Length) offset = 0;
        stream.Seek(offset, SeekOrigin.Begin);

        var text = ReadAll(stream);
        var events = new List<HookEvent>();
        var consumed = offset;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            // Advance the cursor only past completed lines; a trailing partial line (a mid-write append) is
            // left for the next pass — deferred, never torn.
            consumed += Utf8.GetByteCount(text.AsSpan(start, i - start + 1));
            if (HookEvent.TryParse(text[start..i].TrimEnd('\r'), out var e) && e is not null)
                events.Add(e);
            start = i + 1;
        }
        return new HookLogSlice(events, consumed);
    }

    private static string ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }
}
