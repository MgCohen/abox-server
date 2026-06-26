using ABox.Domain.Flow;
using ABox.Domain.Git;

namespace ABox.Tests.Support;

// Mode 3 (driven capability): a flow binds the Domain.Git plumbing capability and runs its operations
// through the engine — commit the working changes, rebase onto the remote, push. Real flow bodies are
// composition-supplied (stripped from production), so this demonstrator lives with the test that drives it.
internal sealed class GitChoreFlow : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var git = new Git(ctx.ProjectDir);
        var status = await Run(ctx, git.Status, new StatusArgs(), ct);
        await Run(ctx, git.Commit, new CommitArgs(ctx.Request, status.Paths, CoAuthor: "Bot"), ct);
        await Run(ctx, git.Pull, new PullArgs(Rebase: true), ct);
        await Run(ctx, git.Push, new PushArgs(), ct);
    }
}
