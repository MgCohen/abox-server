using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Contracts;

namespace ABox.Features.Inbox.Get;

internal sealed class GetInboxItemEndpoint(IInbox inbox) : Endpoint<InboxItemByIdRequest, InboxItemView>
{
    public override void Configure()
    {
        Get("/inbox/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(InboxItemByIdRequest req, CancellationToken ct)
    {
        if (await inbox.Get(req.Id, ct) is not { } item)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new InboxItemView(item.Id, item.Title, item.Tags, item.CreatedAt, item.SeenAt, item.CompletedAt), ct);
    }
}
