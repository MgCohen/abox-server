using RemoteAgents.Agents;
using RemoteAgents.Primitives;

namespace RemoteAgents.Flows;

// Baseline flow. Spin up Claude inside a PTY against the requested
// project, capture whatever changed, return the result. No validation,
// no review, no git.
//
// Phase 4 pilot — first flow migrated to the IFlow contract. The
// `cli/flows/claude-only.cs` script becomes a thin shim that resolves
// this flow from the registry and calls RunAsync.
public sealed class ClaudeOnlyFlow : IFlow
{
    public string Name => "claude-only";
    public string? Summary => "Hand it a project + prompt. Claude runs; the diff is captured. No validation, no review, no git.";

    public async Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct)
    {
        var before = FsDiff.Snapshot(ctx.ProjectDir);

        var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };
        var result = await claude.RunAsync(new AgentRunRequest(
            Prompt: ctx.UserPrompt,
            SessionId: null,
            ProjectDir: ctx.ProjectDir), ct);

        // Forensic dumps — useful while PTY timings are still being tuned.
        await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "claude-raw.txt"), result.RawOutput, ct);
        await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "claude-text.txt"), result.Text, ct);

        var after = FsDiff.Snapshot(ctx.ProjectDir);
        var diff = FsDiff.Diff(before, after);

        Console.WriteLine();
        Console.WriteLine($"Claude session: {result.SessionId}");
        Console.WriteLine($"Files changed:  {diff.Changed.Count}");
        Console.WriteLine($"Files added:    {diff.Added.Count}");
        Console.WriteLine($"Files removed:  {diff.Removed.Count}");
        foreach (var f in diff.All) Console.WriteLine($"  - {f}");
        Console.WriteLine();
        Console.WriteLine($"Transcript: {ctx.Session.Dir}");

        return new FlowResult(FlowExitReason.Shipped);
    }
}
