using System.Diagnostics;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Codex;

public sealed class CodexProvider(CodexConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var tmpDir = Directory.CreateTempSubdirectory("agents-codex-").FullName;
        try
        {
            var lastMessageFile = Path.Combine(tmpDir, "last.txt");
            await using var session = SubprocessSession.Start(BuildStartInfo(request, lastMessageFile), ct);

            var sniff = SniffSessionId(session, request.SessionId);
            await session.StandardInput.WriteAsync(ComposePrompt(request));
            session.CompleteStdin();

            var exitCode = await session.WaitForExitAsync(config.JsonStreamTimeoutMs, ct);
            var sessionId = await sniff;
            var text = File.Exists(lastMessageFile) ? await File.ReadAllTextAsync(lastMessageFile, ct) : "";

            return new DriveResult(text, sessionId ?? "", exitCode, session.RawStdout,
                CodexProtocol.ExtractTranscript(session.RawStdout));
        }
        finally { TryDelete(tmpDir); }
    }

    private ProcessStartInfo BuildStartInfo(AgentRunRequest request, string lastMessageFile)
    {
        var args = CodexProtocol.BuildArgs(request.SessionId, request.ProjectDir, lastMessageFile, config.Model, config.Sandbox);
        var commandLine = "codex " + string.Join(' ', args.Select(Shell.QuoteArg));

        // codex resolves from PATH as a shim, so it is spawned through cmd.exe.
        return new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c {commandLine}",
            WorkingDirectory = request.ProjectDir,
            RedirectStandardInput = true,
        };
    }

    private static Task<string?> SniffSessionId(SubprocessSession session, string? initial) =>
        Task.Run(async () =>
        {
            var sessionId = initial;
            await foreach (var line in session.StdoutLines())
                sessionId ??= CodexProtocol.ScanSessionId(line);
            return sessionId;
        });

    private string ComposePrompt(AgentRunRequest request) =>
        string.IsNullOrEmpty(config.SystemPrompt)
            ? request.Prompt
            : config.SystemPrompt + "\n\n" + request.Prompt;

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best-effort: temp cleanup races are non-fatal */ }
    }
}
