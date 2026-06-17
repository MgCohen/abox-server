using ABox.Domain.Inbox;

namespace ABox.Tests.Unit.Tests;

public sealed class InMemoryInboxTests
{
    [Rule("InMemoryInbox.Get → the added item by id, or null when absent")]
    [Fact]
    public void Get_returns_the_added_item()
    {
        var inbox = new InMemoryInbox();
        var item = new NoteInboxItem("a note", []);
        inbox.Add(item);

        Assert.Same(item, inbox.Get(item.Id));
    }

    [Rule("InMemoryInbox.Get → the added item by id, or null when absent")]
    [Fact]
    public void Get_returns_null_for_an_unknown_id() =>
        Assert.Null(new InMemoryInbox().Get(Guid.NewGuid()));

    [Rule("InMemoryInbox.Query → items carrying every requested tag in arrival order, all when no tag given")]
    [Fact]
    public void Query_with_no_tags_returns_all_in_arrival_order()
    {
        var inbox = new InMemoryInbox();
        var first = new NoteInboxItem("first", ["x"]);
        var second = new NoteInboxItem("second", ["y"]);
        inbox.Add(first);
        inbox.Add(second);

        var all = inbox.Query([]);

        Assert.Equal([first.Id, second.Id], all.Select(i => i.Id));
    }

    [Rule("InMemoryInbox.Query → items carrying every requested tag in arrival order, all when no tag given")]
    [Fact]
    public void Query_narrows_to_items_carrying_every_requested_tag()
    {
        var inbox = new InMemoryInbox();
        var both = new NoteInboxItem("needs review", ["box-7", "review"]);
        var one = new NoteInboxItem("fyi", ["box-7"]);
        inbox.Add(both);
        inbox.Add(one);

        var matched = inbox.Query(["box-7", "review"]);

        Assert.Equal([both.Id], matched.Select(i => i.Id));
    }
}
