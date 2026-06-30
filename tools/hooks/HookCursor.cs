namespace ABox.Governance.Hooks;

public static class HookCursor
{
    public static long Read(string cursorPath)
    {
        if (!File.Exists(cursorPath)) return 0;
        return long.TryParse(File.ReadAllText(cursorPath).Trim(), out var offset) && offset >= 0 ? offset : 0;
    }

    public static void Write(string cursorPath, long offset)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(cursorPath));
        if (dir is not null) Directory.CreateDirectory(dir);

        var tmp = cursorPath + ".tmp";
        File.WriteAllText(tmp, offset.ToString());
        File.Move(tmp, cursorPath, overwrite: true);
    }
}
