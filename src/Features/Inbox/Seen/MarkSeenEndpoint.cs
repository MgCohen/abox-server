using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Contracts;

namespace ABox.Features.Inbox.Seen;

internal sealed class MarkSeenEndpoint(IInbox inbox) : EndpointWithoutRequest<InboxItemView>
{
    public override void Configure()
    {
        Post("/inbox/{id}/seen");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (await inbox.MarkSeen(Route<Guid>("id"), ct) is not { } item)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new InboxItemView(item.Id, item.Title, item.Tags, item.CreatedAt, item.SeenAt, item.CompletedAt), ct);
    }
}
