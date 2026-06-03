namespace RemoteAgents.Actors.Agents.Codex;

public static class CodexArgs
{
    public static List<string> Build(string? sessionId, string projectDir, string lastMessageFile, string model, string sandbox)
    {
        var args = new List<string> { "exec" };
        if (sessionId is not null) { args.Add("resume"); args.Add(sessionId); }

        args.Add("--cd"); args.Add(projectDir);
        args.Add("-o"); args.Add(lastMessageFile);
        args.Add("--sandbox"); args.Add(sandbox);
        // codex refuses to run unattended in a non-git or first-seen dir without this.
        args.Add("--skip-git-repo-check");
        args.Add("--json");
        if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
        args.Add("-");
        return args;
    }
}
