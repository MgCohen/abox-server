namespace ABox.Governance.Hooks;

public static class Cli
{
    private const string Usage =
        "abox-hooks run [--log <path>] [--cursor <path>] [--root <dir>]... [--timeout-ms <n>] [--since-cursor]\n" +
        "  Dispatch pending hook events from the log to the discovered .hook files.";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0) return Fail("no command given");
        if (args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        return args[0] switch
        {
            "run" => await RunDispatch(args[1..]),
            _ => Fail($"unknown command '{args[0]}'"),
        };
    }

    private static async Task<int> RunDispatch(string[] args)
    {
        CliOptions opts;
        try
        {
            opts = CliOptions.Parse(args);
        }
        catch (ArgumentException e)
        {
            return Fail(e.Message);
        }

        var catalog = new HookCatalog(opts.Roots, msg => Console.Error.WriteLine($"abox-hooks: {msg}"));
        var dispatcher = new HookDispatcher(new HookRunner(opts.TimeoutMs));
        var controller = new HookController(catalog, dispatcher);

        var dispatched = await controller.DispatchPendingAsync(opts.LogPath, opts.CursorPath);
        Console.WriteLine($"abox-hooks: dispatched {dispatched} event(s)");
        return 0;
    }

    private static int Fail(string why)
    {
        Console.Error.WriteLine($"abox-hooks: {why}");
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
