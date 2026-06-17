using ABox.Domain.Git;
using ABox.Features.Git.Module;

namespace ABox.Tests.Unit.Tests;

public sealed class InMemoryStackHostTests
{
    [Rule("InMemoryStackHost opening a PR onto a non-main base → an open PR targeting that base")]
    [Fact]
    public async Task OpenPullRequest_targets_a_non_main_base()
    {
        var host = new InMemoryStackHost();
        await host.CreateBranch("box/x", "main", default);
        await host.CreateBranch("phase-1", "box/x", default);

        var pull = await host.OpenPullRequest("phase-1", "box/x", "Phase 1", default);

        Assert.Equal("box/x", pull.BaseRef);
        var view = await host.GetPullRequest(pull.Number, default);
        Assert.Equal("box/x", view.BaseRef);
        Assert.Equal("open", view.State);
        Assert.True(view.Mergeable);
    }

    [Rule("InMemoryStackHost merging a parent then retargeting its child onto the merged base → parent recorded merged and child rebased clean")]
    [Fact]
    public async Task Merge_parent_then_retarget_child_onto_the_merged_base()
    {
        var host = new InMemoryStackHost();
        await host.CreateBranch("box/x", "main", default);
        await host.CreateBranch("phase-1", "box/x", default);
        await host.CreateBranch("phase-2", "phase-1", default);
        var parent = await host.OpenPullRequest("phase-1", "box/x", "Phase 1", default);
        var child = await host.OpenPullRequest("phase-2", "phase-1", "Phase 2", default);

        var outcome = await host.Merge(parent.Number, MergeMethod.Merge, default);
        await host.RetargetPullRequest(child.Number, "box/x", default);

        Assert.True(outcome.Merged);
        Assert.NotEmpty(outcome.Sha);
        var parentView = await host.GetPullRequest(parent.Number, default);
        Assert.Equal("merged", parentView.State);
        var childView = await host.GetPullRequest(child.Number, default);
        Assert.Equal("box/x", childView.BaseRef);
        Assert.True(childView.Mergeable);
    }

    [Rule("InMemoryStackHost deleting a branch → the branch is removed and can be re-created fresh")]
    [Fact]
    public async Task DeleteBranch_removes_the_branch()
    {
        var host = new InMemoryStackHost();
        await host.CreateBranch("box/x", "main", default);
        var created = await host.CreateBranch("phase-1", "box/x", default);

        await host.DeleteBranch("phase-1", default);
        var recreated = await host.CreateBranch("phase-1", "box/x", default);

        Assert.NotEqual(created.Sha, recreated.Sha);
    }
}
