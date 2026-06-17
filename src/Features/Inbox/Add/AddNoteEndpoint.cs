using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Contracts;
using ABox.Features.Inbox.Get;

namespace ABox.Features.Inbox.Add;

internal sealed class AddNoteEndpoint(IInbox inbox) : Endpoint<AddNoteRequest, InboxItemView>
{
    public override void Configure()
    {
        Post("/inbox");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AddNoteRequest req, CancellationToken ct)
    {
        var title = req.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            AddError(r => r.Title, "An inbox item needs a title.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var item = new NoteInboxItem { Title = title, Tags = req.Tags ?? [] };
        await inbox.Add(item, ct);
        await Send.CreatedAtAsync<GetInboxItemEndpoint>(
            new { id = item.Id },
            new InboxItemView(item.Id, item.Title, item.Tags, item.CreatedAt, item.SeenAt, item.CompletedAt),
            cancellation: ct);
    }
}
