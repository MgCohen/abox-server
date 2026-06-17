using ABox.Domain.Inbox;
using ABox.Infrastructure.Storage;

namespace ABox.Tests.Unit.Tests;

public sealed class InboxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("inbox-").FullName;

    private Inbox NewInbox() => new(new JsonRepository<InboxItem>(new StorageRoot(_dir)));

    [Rule("Inbox.Get → the item by id, or null when absent")]
    [Fact]
    public async Task Get_returns_the_item_without_stamping_it()
    {
        var inbox = NewInbox();
        var item = new NoteInboxItem { Title = "phase 3 needs review", Tags = ["box-7"] };
        await inbox.Add(item);

        var got = await inbox.Get(item.Id);

        Assert.Equal(item.Id, got!.Id);
        Assert.Null(got.SeenAt);
    }

    [Rule("Inbox.Get → the item by id, or null when absent")]
    [Fact]
    public async Task Get_returns_null_for_an_unknown_id() =>
        Assert.Null(await NewInbox().Get(Guid.NewGuid()));

    [Rule("Inbox.MarkSeen → the item stamped seen once and stable on repeat, null when absent")]
    [Fact]
    public async Task MarkSeen_stamps_once_and_keeps_the_first_time()
    {
        var inbox = NewInbox();
        var item = new NoteInboxItem { Title = "see me", Tags = [] };
        await inbox.Add(item);

        var first = await inbox.MarkSeen(item.Id);
        Assert.NotNull(first!.SeenAt);

        var again = await inbox.MarkSeen(item.Id);
        Assert.Equal(first.SeenAt, again!.SeenAt);
    }

    [Rule("Inbox.MarkSeen → the item stamped seen once and stable on repeat, null when absent")]
    [Fact]
    public async Task MarkSeen_returns_null_for_an_unknown_id() =>
        Assert.Null(await NewInbox().MarkSeen(Guid.NewGuid()));

    [Rule("Inbox.Complete → the item marked complete once and stable on repeat, null when absent")]
    [Fact]
    public async Task Complete_marks_the_item_complete()
    {
        var inbox = NewInbox();
        var item = new NoteInboxItem { Title = "finish me", Tags = [] };
        await inbox.Add(item);

        var done = await inbox.Complete(item.Id);

        Assert.NotNull(done!.CompletedAt);
    }

    [Rule("Inbox.Complete → the item marked complete once and stable on repeat, null when absent")]
    [Fact]
    public async Task Complete_returns_null_for_an_unknown_id() =>
        Assert.Null(await NewInbox().Complete(Guid.NewGuid()));

    [Rule("Inbox.Query → items carrying every requested tag in arrival order, all when no tag given")]
    [Fact]
    public async Task Query_with_no_tags_returns_all_in_arrival_order()
    {
        var inbox = NewInbox();
        var first = new NoteInboxItem { Title = "first", Tags = ["x"] };
        var second = new NoteInboxItem { Title = "second", Tags = ["y"] };
        await inbox.Add(first);
        await inbox.Add(second);

        var all = await inbox.Query([]);

        Assert.Equal([first.Id, second.Id], all.Select(i => i.Id));
    }

    [Rule("Inbox.Query → items carrying every requested tag in arrival order, all when no tag given")]
    [Fact]
    public async Task Query_narrows_to_items_carrying_every_requested_tag()
    {
        var inbox = NewInbox();
        var both = new NoteInboxItem { Title = "needs review", Tags = ["box-7", "review"] };
        var one = new NoteInboxItem { Title = "fyi", Tags = ["box-7"] };
        await inbox.Add(both);
        await inbox.Add(one);

        var matched = await inbox.Query(["box-7", "review"]);

        Assert.Equal([both.Id], matched.Select(i => i.Id));
    }

    [Rule("InboxItem persisted through the repository → reloads as its concrete subtype")]
    [Fact]
    public async Task Items_reload_as_their_concrete_type_from_a_fresh_repository()
    {
        var item = new NoteInboxItem { Title = "durable", Tags = ["t"] };
        await NewInbox().Add(item);

        var reloaded = await NewInbox().Get(item.Id);

        Assert.IsType<NoteInboxItem>(reloaded);
        Assert.Equal(item.Id, reloaded!.Id);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
