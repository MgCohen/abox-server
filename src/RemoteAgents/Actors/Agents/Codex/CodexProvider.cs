using System.Diagnostics;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Codex;

public sealed class CodexProvider(CodexConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "agents-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var lastMessageFile = Path.Combine(tmpDir, "last.txt");

        var args = CodexArgs.Build(request.SessionId, request.ProjectDir, lastMessageFile, config.Model, config.Sandbox);
        var commandLine = "codex " + string.Join(' ', args.Select(Shell.QuoteArg));

        // codex resolves from PATH as a shim, so it is spawned through cmd.exe.
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c {commandLine}",
            WorkingDirectory = request.ProjectDir,
            RedirectStandardInput = true,
        };

        await using var session = SubprocessSession.Start(psi, ct);

        var sessionId = request.SessionId;
        var sniff = Task.Run(async () =>
        {
            await foreach (var line in session.StdoutLines())
            {
                if (sessionId is not null) continue;
                var found = CodexSessionId.Scan(line);
                if (found is not null) sessionId = found;
            }
        }, ct);

        var prompt = string.IsNullOrEmpty(config.SystemPrompt)
            ? request.Prompt
            : config.SystemPrompt + "\n\n" + request.Prompt;
        await session.StandardInput.WriteAsync(prompt);
        session.CompleteStdin();

        var exitCode = await session.WaitForExitAsync(config.JsonStreamTimeoutMs, ct);
        await sniff;

        var text = File.Exists(lastMessageFile) ? await File.ReadAllTextAsync(lastMessageFile, ct) : "";
        try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort: temp cleanup races are non-fatal */ }

        return new DriveResult(text, sessionId ?? "", exitCode, session.RawStdout, CodexJsonl.ExtractTranscript(session.RawStdout));
    }
}
