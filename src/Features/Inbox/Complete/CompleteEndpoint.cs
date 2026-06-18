using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Contracts;

namespace ABox.Features.Inbox.Complete;

internal sealed class CompleteEndpoint(IInbox inbox) : EndpointWithoutRequest<InboxItemView>
{
    public override void Configure()
    {
        Post("/inbox/{id}/complete");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (await inbox.Complete(Route<Guid>("id"), ct) is not { } item)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(
            new InboxItemView(item.Id, item.Title, item.Tags, item.CreatedAt, item.SeenAt, item.CompletedAt), ct);
    }
}
