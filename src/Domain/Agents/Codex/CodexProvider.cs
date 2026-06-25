using ABox.Infrastructure.CommandLine;
using ABox.Infrastructure.Sandbox;

namespace ABox.Domain.Agents.Codex;

public sealed class CodexProvider(CodexConfig config, CodexSandbox sandbox) : IProvider, IAsyncDisposable
{
    // One .codex HOME for the provider's whole life (codex writes sessions here, and a resume
    // turn must re-mount the same HOME to find them); copied from the auth template so each
    // provider gets its own writable credential dir. Disposed with the provider (Agent owns it).
    private DirectoryInfo? _home;

    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        await SubscriptionGuard.CheckAsync(EnvScrub.CodexKeys, "codex", ct);

        if (config.Policy != PermissionPolicy.Bypass)
            throw new NotSupportedException(
                $"Codex does not yet honor PermissionPolicy.{config.Policy} (ADR 0007 §5); it runs with the box as its wall. Leave Policy at the default Bypass.");

        var sessionDir = Directory.CreateTempSubdirectory("agents-codex-session-").FullName;
        try
        {
            var lastInBox = $"{DockerSandbox.SessionMount}/last.txt";
            var args = CodexProtocol.BuildArgs(request.SessionId, DockerSandbox.WorkMount, lastInBox, config.Model);
            var codexLine = "codex " + string.Join(' ', args.Select(Shell.Quote));

            var options = new SandboxOptions(
                Worktree: new DirectoryInfo(request.ProjectDir),
                SessionDir: new DirectoryInfo(sessionDir),
                Home: _home ??= PrepareHome(),
                Image: sandbox.Image,
                Network: sandbox.Network,
                RequireInternalNetwork: true);
            await using var box = await DockerSandbox.OpenAsync(options, ct);

            var psi = Shell.Command(box.PipeExecLine(codexLine, BoxEnv()));
            psi.RedirectStandardInput = true;
            await using var session = SubprocessSession.Start(psi, ct);

            var sniff = SniffSessionId(session, request.SessionId);
            await session.StandardInput.WriteAsync(ComposePrompt(request));
            session.CompleteStdin();

            var exitCode = await session.WaitForExitAsync(config.JsonStreamTimeoutMs, ct);
            var sessionId = await sniff;
            var lastHost = Path.Combine(sessionDir, "last.txt");
            var text = File.Exists(lastHost) ? await File.ReadAllTextAsync(lastHost, ct) : "";

            return new DriveResult(text, sessionId ?? "", exitCode, session.RawStdout,
                CodexProtocol.ExtractTranscript(session.RawStdout));
        }
        finally { TryDelete(sessionDir); }
    }

    public ValueTask DisposeAsync()
    {
        if (_home is { } home) TryDelete(home.FullName);
        _home = null;
        return ValueTask.CompletedTask;
    }

    private DirectoryInfo PrepareHome()
    {
        var home = Directory.CreateTempSubdirectory("agents-codex-home-");
        CopyDir(sandbox.AuthTemplate, home);
        return home;
    }

    // HOME and CODEX_HOME point at the mounted .codex (auth + sessions); the egress proxy is
    // the box's only route out. No credential is on the exec line — it lives in the mounted file.
    private Dictionary<string, string> BoxEnv() => new()
    {
        ["HOME"] = DockerSandbox.HomeMount,
        ["CODEX_HOME"] = $"{DockerSandbox.HomeMount}/.codex",
        ["HTTPS_PROXY"] = sandbox.ProxyUrl,
        ["HTTP_PROXY"] = sandbox.ProxyUrl,
    };

    private string ComposePrompt(AgentRunRequest request) =>
        AgentDirective.ComposeSystemPrompt(config.SystemPrompt, config.Resolution) + "\n\n" + request.Prompt;

    private static Task<string?> SniffSessionId(SubprocessSession session, string? initial) =>
        Task.Run(async () =>
        {
            var sessionId = initial;
            await foreach (var line in session.StdoutLines())
                sessionId ??= CodexProtocol.ScanSessionId(line);
            return sessionId;
        });

    private static void CopyDir(DirectoryInfo src, DirectoryInfo dst)
    {
        dst.Create();
        foreach (var dir in src.GetDirectories())
            CopyDir(dir, dst.CreateSubdirectory(dir.Name));
        foreach (var file in src.GetFiles())
            file.CopyTo(Path.Combine(dst.FullName, file.Name), overwrite: true);
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best-effort: temp cleanup races are non-fatal */ }
    }
}
