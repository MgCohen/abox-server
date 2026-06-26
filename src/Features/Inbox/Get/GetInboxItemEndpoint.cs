using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Api;

namespace ABox.Features.Inbox.Get;

internal sealed class GetInboxItemEndpoint(IInbox inbox) : EndpointWithoutRequest<InboxItemView>
{
    public override void Configure()
    {
        Get("/inbox/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (await inbox.Get(Route<Guid>("id"), ct) is not { } item)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new InboxItemView(item.Id, item.Title, item.Tags, item.CreatedAt, item.SeenAt, item.CompletedAt), ct);
    }
}
