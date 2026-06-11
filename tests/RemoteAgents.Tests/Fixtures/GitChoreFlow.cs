using RemoteAgents.Domain.Flow;
using RemoteAgents.Domain.Git;

namespace RemoteAgents.Tests;

// Mode 3 (driven capability): a flow binds the Domain.Git plumbing capability and runs its operations
// through the engine — commit the working changes, rebase onto the remote, push. Real flow bodies are
// composition-supplied (stripped from production), so this demonstrator lives with the test that drives it.
internal sealed class GitChoreFlow : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var git = new Git(ctx.ProjectDir);
        var changed = await Run(ctx, git.ChangedFiles, new ChangedFilesArgs(), ct);
        await Run(ctx, git.Commit, new CommitArgs(ctx.Request, changed.Files, CoAuthor: "Bot"), ct);
        await Run(ctx, git.Pull, new PullArgs(Rebase: true), ct);
        await Run(ctx, git.Push, new PushArgs(), ct);
    }
}
