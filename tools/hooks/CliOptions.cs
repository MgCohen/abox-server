namespace ABox.Governance.Hooks;

public sealed record CliOptions(string LogPath, string CursorPath, IReadOnlyList<string> Roots, int TimeoutMs)
{
    public static CliOptions Parse(string[] args)
    {
        var log = Path.Combine(".abox", "hooks.jsonl");
        var cursor = Path.Combine(".abox", "hooks.cursor");
        var roots = new List<string>();
        var timeoutMs = 30_000;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--log": log = Next(args, ref i); break;
                case "--cursor": cursor = Next(args, ref i); break;
                case "--root": roots.Add(Next(args, ref i)); break;
                case "--timeout-ms": timeoutMs = ParseTimeout(Next(args, ref i)); break;
                case "--since-cursor": break;
                default: throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        if (roots.Count == 0) roots.Add(Directory.GetCurrentDirectory());
        return new CliOptions(log, cursor, roots, timeoutMs);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"option '{args[i]}' needs a value");
        return args[++i];
    }

    private static int ParseTimeout(string value) =>
        int.TryParse(value, out var ms) && ms > 0
            ? ms
            : throw new ArgumentException($"--timeout-ms needs a positive integer, got '{value}'");
}
