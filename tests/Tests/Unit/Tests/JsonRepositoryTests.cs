using ABox.Infrastructure.Storage;

namespace ABox.Tests.Unit.Tests;

public sealed class JsonRepositoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("repo-").FullName;

    private JsonRepository<Thing> NewRepo() => new(new StorageRoot(_dir));

    [Rule("JsonRepository round-trips entities through Add, Get, Update, and Remove")]
    public async Task Crud_round_trips()
    {
        var repo = NewRepo();
        var thing = new Thing(Guid.NewGuid(), "one");

        await repo.Add(thing);
        Assert.Equal(thing, await repo.GetById(thing.Id));

        await repo.Update(thing with { Name = "renamed" });
        Assert.Equal("renamed", (await repo.GetById(thing.Id))!.Name);

        await repo.Remove(thing.Id);
        Assert.Null(await repo.GetById(thing.Id));
        Assert.Empty(await repo.GetAll());
    }

    [Rule("JsonRepository reloads persisted entities from a fresh instance")]
    public async Task Reloads_from_disk()
    {
        var first = NewRepo();
        var a = new Thing(Guid.NewGuid(), "a");
        var b = new Thing(Guid.NewGuid(), "b");
        await first.Add(a);
        await first.Add(b);

        var all = await NewRepo().GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(a, all);
        Assert.Contains(b, all);
    }

    [Rule("JsonRepository starts empty when the backing file is unreadable")]
    public async Task Corrupt_file_starts_empty()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "thing.json"), "{ this is not json");

        var repo = NewRepo();
        Assert.Empty(await repo.GetAll());

        var fresh = new Thing(Guid.NewGuid(), "fresh");
        await repo.Add(fresh);
        Assert.Equal(fresh, await repo.GetById(fresh.Id));
    }

    [Rule("JsonRepository serializes concurrent writers without tearing the store")]
    public async Task Concurrent_writes_do_not_tear()
    {
        var repo = NewRepo();
        var things = Enumerable.Range(0, 50)
            .Select(i => new Thing(Guid.NewGuid(), $"t{i}"))
            .ToList();

        await Task.WhenAll(things.Select(t => repo.Add(t)));

        var all = await NewRepo().GetAll();
        Assert.Equal(things.Count, all.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private sealed record Thing(Guid Id, string Name) : IEntity;
}
