using System.Text.Json.Serialization;
using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

// Sample shape exercising the load-mutate-save flow the orchestrator
// would use for its own task list.
public sealed record TaskItem(string Id, string Status);
public sealed record TaskList(IReadOnlyList<TaskItem> Tasks);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TaskList))]
internal sealed partial class TaskListJsonContext : JsonSerializerContext { }

public class JsonStoreTests
{
    [Fact]
    public async Task ReadAsync_missing_file_returns_null()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-jsonstore-" + Guid.NewGuid().ToString("N") + ".json");
        var res = await JsonStore.ReadAsync(path, TaskListJsonContext.Default.TaskList);
        Assert.Null(res);
    }

    [Fact]
    public async Task UpdateAsync_seeds_from_null_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-jsonstore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList,
                current => current ?? new TaskList(new[] { new TaskItem("t1", "open") }));

            Assert.Single(saved.Tasks);
            Assert.Equal("open", saved.Tasks[0].Status);

            var reloaded = await JsonStore.ReadAsync(path, TaskListJsonContext.Default.TaskList);
            Assert.NotNull(reloaded);
            Assert.Equal("open", reloaded!.Tasks[0].Status);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task UpdateAsync_round_trip_status_flip()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-jsonstore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList,
                _ => new TaskList(new[]
                {
                    new TaskItem("t1", "open"),
                    new TaskItem("t2", "open"),
                }));

            // First-open → active
            await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList, current =>
            {
                var first = current!.Tasks.First(t => t.Status == "open");
                return new TaskList(current.Tasks
                    .Select(t => t.Id == first.Id ? t with { Status = "active" } : t)
                    .ToList());
            });

            var afterFlip = await JsonStore.ReadAsync(path, TaskListJsonContext.Default.TaskList);
            Assert.Equal("active", afterFlip!.Tasks[0].Status);
            Assert.Equal("open", afterFlip.Tasks[1].Status);

            // active → done
            await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList, current =>
                new TaskList(current!.Tasks
                    .Select(t => t.Status == "active" ? t with { Status = "done" } : t)
                    .ToList()));

            var afterDone = await JsonStore.ReadAsync(path, TaskListJsonContext.Default.TaskList);
            Assert.Equal("done", afterDone!.Tasks[0].Status);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task UpdateAsync_serializes_concurrent_writers()
    {
        // Increment a counter from N parallel tasks. Without the per
        // -path lock, classic read-modify-write would lose updates.
        var path = Path.Combine(Path.GetTempPath(), "ra-jsonstore-" + Guid.NewGuid().ToString("N") + ".json");
        const int N = 20;
        try
        {
            await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList,
                _ => new TaskList(new[] { new TaskItem("counter", "0") }));

            var tasks = Enumerable.Range(0, N).Select(_ => Task.Run(async () =>
            {
                await JsonStore.UpdateAsync(path, TaskListJsonContext.Default.TaskList, current =>
                {
                    var c = int.Parse(current!.Tasks[0].Status);
                    return new TaskList(new[] { current.Tasks[0] with { Status = (c + 1).ToString() } });
                });
            })).ToArray();
            await Task.WhenAll(tasks);

            var final = await JsonStore.ReadAsync(path, TaskListJsonContext.Default.TaskList);
            Assert.Equal(N.ToString(), final!.Tasks[0].Status);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task UpdateAsync_null_mutator_result_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "ra-jsonstore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                JsonStore.UpdateAsync<TaskList>(path, TaskListJsonContext.Default.TaskList, _ => null!));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
