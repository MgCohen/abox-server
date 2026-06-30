using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookControllerTests
{
    [Rule("HookController dispatches the pending log slice once and advances the cursor past completed lines")]
    [Fact]
    public async Task Dispatches_pending_once_and_advances_cursor()
    {
        var dir = Directory.CreateTempSubdirectory("hookctl-").FullName;
        try
        {
            var hookDir = Directory.CreateDirectory(Path.Combine(dir, "feat")).FullName;
            var runs = Path.Combine(dir, "runs.txt");
            File.WriteAllText(Path.Combine(hookDir, "count.hook"),
                $"on: [TurnEnded]\nrun: printf x >> \"{runs}\"\n");

            var log = Path.Combine(dir, ".abox", "hooks.jsonl");
            var cursor = Path.Combine(dir, ".abox", "hooks.cursor");
            HookLog.Append(log, Event("s1"));
            HookLog.Append(log, Event("s2"));
            File.AppendAllText(log, "{\"kind\":\"TurnEnded\",\"source\":\"Claude");

            var controller = new HookController(
                new HookCatalog([hookDir]), new HookDispatcher(new HookRunner(10_000)));

            Assert.Equal(2, await controller.DispatchPendingAsync(log, cursor));
            Assert.Equal(2, File.ReadAllText(runs).Length);

            Assert.Equal(0, await controller.DispatchPendingAsync(log, cursor));
            Assert.Equal(2, File.ReadAllText(runs).Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static HookEvent Event(string sessionId) =>
        new(HookKind.TurnEnded, HookSource.Claude, sessionId, "/x", JsonDocument.Parse("{}").RootElement.Clone());
}
