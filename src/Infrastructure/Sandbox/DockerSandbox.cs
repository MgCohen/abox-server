using System.Runtime.InteropServices;
using ABox.Infrastructure.CommandLine;

namespace ABox.Infrastructure.Sandbox;

public sealed class DockerSandbox : ISandbox
{
    public const string WorkMount = "/work";
    public const string SessionMount = "/session";
    public const string HomeMount = "/home/box";

    [DllImport("libc")] private static extern uint getuid();
    [DllImport("libc")] private static extern uint getgid();

    // Run the box as the orchestrator's own uid:gid so the bind mounts are writable
    // and files the box creates (the JSONL, hook handshake) come back host-owned. A
    // non-root orchestrator also keeps the box non-root, which bypassPermissions needs.
    private static string UserFlag => $"--user {getuid()}:{getgid()} ";

    private readonly string _containerId;
    private bool _disposed;

    private DockerSandbox(string containerId) => _containerId = containerId;

    public static async Task<DockerSandbox> OpenAsync(SandboxOptions options, CancellationToken ct)
    {
        var network = options.Network is null ? "" : $"--network {Shell.QuoteArg(options.Network)} ";
        var runLine =
            $"docker run -d {UserFlag}-w {WorkMount} " +
            $"-v {Shell.QuoteArg(options.Worktree.FullName)}:{WorkMount} " +
            $"-v {Shell.QuoteArg(options.SessionDir.FullName)}:{SessionMount} " +
            $"-v {Shell.QuoteArg(options.Home.FullName)}:{HomeMount} " +
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
        var line = $"docker exec -w {WorkMount} {_containerId} sh -lc {Shell.QuoteArg(command)}";
        var r = await RunCommand.RunAsync(line, ct: ct);
        return new ExecResult(r.ExitCode, r.Stdout, r.Stderr);
    }

    public string InteractiveExecLine(string command, IReadOnlyDictionary<string, string>? env = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var envFlags = env is null
            ? ""
            : string.Concat(env.Select(kv => $"-e {Shell.QuoteArg($"{kv.Key}={kv.Value}")} "));
        return $"docker exec -it {UserFlag}{envFlags}-w {WorkMount} {_containerId} {command}";
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
