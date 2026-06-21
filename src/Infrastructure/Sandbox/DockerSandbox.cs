using ABox.Infrastructure.CommandLine;

namespace ABox.Infrastructure.Sandbox;

public sealed class DockerSandbox : ISandbox
{
    private const string Workdir = "/work";
    private readonly string _containerId;
    private bool _disposed;

    private DockerSandbox(string containerId) => _containerId = containerId;

    public static async Task<DockerSandbox> OpenAsync(SandboxOptions options, CancellationToken ct)
    {
        var network = options.Network is null ? "" : $"--network {Shell.QuoteArg(options.Network)} ";
        var runLine =
            $"docker run -d -w {Workdir} " +
            $"-v {Shell.QuoteArg(options.Worktree.FullName)}:{Workdir} " +
            $"-v {Shell.QuoteArg(options.SessionDir.FullName)}:/session " +
            network +
            $"{Shell.QuoteArg(options.Image)} sleep infinity";

        var result = (await RunCommand.RunAsync(runLine, ct: ct)).EnsureOk("docker run");
        var id = result.Stdout.Trim();
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"docker run returned no container id: {result.ErrorText}");
        return new DockerSandbox(id);
    }

    public async Task<ExecResult> ExecAsync(string command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var line = $"docker exec -w {Workdir} {_containerId} sh -lc {Shell.QuoteArg(command)}";
        var r = await RunCommand.RunAsync(line, ct: ct);
        return new ExecResult(r.ExitCode, r.Stdout, r.Stderr);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Guaranteed anti-zombie teardown: rm -f kills + removes even a hung box.
        // Best-effort because dispose must not throw; the box is ephemeral regardless.
        await RunCommand.RunAsync($"docker rm -f {_containerId}", new RunCommandOptions(TimeoutMs: 30_000), CancellationToken.None);
    }
}
