using FastEndpoints;
using ABox.Domain.Inbox;
using ABox.Features.Inbox.Contracts;

namespace ABox.Features.Inbox.List;

internal sealed class ListInboxEndpoint(IInbox inbox) : EndpointWithoutRequest<IReadOnlyList<InboxItemView>>
{
    public override void Configure()
    {
        Get("/inbox");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tags = Query<List<string>>("tag", isRequired: false) ?? [];
        var items = inbox.Query(tags);
        await Send.OkAsync(
            [.. items.Select(i => new InboxItemView(i.Id, i.Title, i.Tags, i.CreatedAt, i.SeenAt, i.CompletedAt))],
            ct);
    }
}
