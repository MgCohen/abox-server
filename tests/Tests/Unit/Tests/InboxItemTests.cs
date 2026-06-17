using ABox.Domain.Inbox;

namespace ABox.Tests.Unit.Tests;

public sealed class InboxItemTests
{
    [Rule("InboxItem.MarkSeen → SeenAt stamped once and stable on repeat")]
    [Fact]
    public void MarkSeen_stamps_once_and_keeps_the_first_time()
    {
        var item = new NoteInboxItem("phase 3 needs review", ["box-7"]);
        Assert.Null(item.SeenAt);

        item.MarkSeen();
        var first = item.SeenAt;

        item.MarkSeen();

        Assert.NotNull(first);
        Assert.Equal(first, item.SeenAt);
    }

    [Rule("InboxItem.Complete → CompletedAt stamped once and stable on repeat")]
    [Fact]
    public void Complete_stamps_once_and_keeps_the_first_time()
    {
        var item = new NoteInboxItem("phase 3 needs review", ["box-7"]);
        Assert.Null(item.CompletedAt);

        item.Complete();
        var first = item.CompletedAt;

        item.Complete();

        Assert.NotNull(first);
        Assert.Equal(first, item.CompletedAt);
    }
}
