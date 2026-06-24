using System.Diagnostics;
using ABox.Infrastructure.CommandLine;

namespace ABox.Domain.Agents.Codex;

public sealed class CodexProvider(CodexConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        if (config.Policy != PermissionPolicy.Bypass)
            throw new NotSupportedException(
                $"Codex does not yet honor PermissionPolicy.{config.Policy} (ADR 0007 §5); it runs with the baked sandbox default. Leave Policy at the default Bypass.");

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
        var args = CodexProtocol.BuildArgs(request.SessionId, request.ProjectDir, lastMessageFile, config.Model);
        var commandLine = "codex " + string.Join(' ', args.Select(Shell.Quote));

        var psi = Shell.Command(commandLine);
        psi.WorkingDirectory = request.ProjectDir;
        psi.RedirectStandardInput = true;
        return psi;
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
        AgentDirective.ComposeSystemPrompt(config.SystemPrompt, config.Resolution) + "\n\n" + request.Prompt;

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best-effort: temp cleanup races are non-fatal */ }
    }
}
