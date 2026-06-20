using ABox.Domain.Decisions;
using ABox.Domain.Inbox;
using ABox.Infrastructure.Storage;

namespace ABox.Tests.Unit.Tests;

public sealed class DecisionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("decisions-").FullName;

    private Inbox NewInbox() => new(new JsonRepository<InboxItem>(new StorageRoot(_dir)));

    private Decisions NewDecisions(Inbox inbox) =>
        new(new JsonRepository<Decision>(new StorageRoot(_dir)), inbox);

    [Rule("Decisions.Raise → stores the question and pushes a matching inbox item sharing its id")]
    [Fact]
    public async Task Raise_stores_the_decision_and_pushes_an_inbox_item_under_the_same_id()
    {
        var inbox = NewInbox();
        var decisions = NewDecisions(inbox);

        var decision = await decisions.Raise("Merge phase 3?", ["box-7"]);

        var stored = await decisions.Get(decision.Id);
        Assert.Equal("Merge phase 3?", stored!.Question);
        Assert.Null(stored.AnsweredAt);

        var item = await inbox.Get(decision.Id);
        Assert.NotNull(item);
        Assert.Equal(decision.Id, item!.Id);
        Assert.Equal("Merge phase 3?", item.Title);
        Assert.Equal(["box-7"], item.Tags);
    }

    [Rule("Decisions.Get → the decision by id, or null when absent")]
    [Fact]
    public async Task Get_returns_the_decision_without_changing_it()
    {
        var decisions = NewDecisions(NewInbox());
        var decision = await decisions.Raise("ship it?", []);

        var got = await decisions.Get(decision.Id);

        Assert.Equal(decision.Id, got!.Id);
        Assert.Null(got.AnsweredAt);
    }

    [Rule("Decisions.Get → the decision by id, or null when absent")]
    [Fact]
    public async Task Get_returns_null_for_an_unknown_id() =>
        Assert.Null(await NewDecisions(NewInbox()).Get(Guid.NewGuid()));

    [Rule("Decisions.List → every decision in arrival order")]
    [Fact]
    public async Task List_returns_all_decisions_in_arrival_order()
    {
        var decisions = NewDecisions(NewInbox());
        var first = await decisions.Raise("first?", []);
        var second = await decisions.Raise("second?", []);

        var all = await decisions.List();

        Assert.Equal([first.Id, second.Id], all.Select(d => d.Id));
    }

    [Rule("Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent")]
    [Fact]
    public async Task Answer_records_the_yes_no_answer_and_an_optional_note()
    {
        var decisions = NewDecisions(NewInbox());
        var decision = await decisions.Raise("approve?", []);

        var answered = await decisions.Answer(decision.Id, answer: true, note: "looks good");

        Assert.True(answered!.Answer);
        Assert.Equal("looks good", answered.Note);
        Assert.NotNull(answered.AnsweredAt);
    }

    [Rule("Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent")]
    [Fact]
    public async Task Answer_records_a_no()
    {
        var decisions = NewDecisions(NewInbox());
        var decision = await decisions.Raise("approve?", []);

        var answered = await decisions.Answer(decision.Id, answer: false, note: "not yet");

        Assert.False(answered!.Answer);
        Assert.Equal("not yet", answered.Note);
        Assert.NotNull(answered.AnsweredAt);
    }

    [Rule("Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent")]
    [Fact]
    public async Task Answer_keeps_the_first_answer_on_repeat()
    {
        var decisions = NewDecisions(NewInbox());
        var decision = await decisions.Raise("approve?", []);

        var first = await decisions.Answer(decision.Id, answer: true, note: null);
        var again = await decisions.Answer(decision.Id, answer: false, note: "changed my mind");

        Assert.True(again!.Answer);
        Assert.Null(again.Note);
        Assert.Equal(first!.AnsweredAt, again.AnsweredAt);
    }

    [Rule("Decisions.Answer → the decision recorded with its yes/no answer once and stable on repeat, null when absent")]
    [Fact]
    public async Task Answer_returns_null_for_an_unknown_id() =>
        Assert.Null(await NewDecisions(NewInbox()).Answer(Guid.NewGuid(), answer: true, note: null));

    [Rule("Decisions.Answer → completes the inbox item it raised")]
    [Fact]
    public async Task Answer_completes_the_inbox_item_raised_under_the_same_id()
    {
        var inbox = NewInbox();
        var decisions = NewDecisions(inbox);
        var decision = await decisions.Raise("merge?", []);
        Assert.Null((await inbox.Get(decision.Id))!.CompletedAt);

        await decisions.Answer(decision.Id, answer: true, note: null);

        Assert.NotNull((await inbox.Get(decision.Id))!.CompletedAt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
