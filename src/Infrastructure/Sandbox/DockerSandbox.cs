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

    public static bool RunsAsRoot => OperatingSystem.IsLinux() && getuid() == 0;

    // Run the box as the orchestrator's own uid:gid so the bind mounts are writable and
    // files the box creates (the JSONL, hook handshake) come back host-owned. Linux only:
    // Docker Desktop on Win/Mac maps uids itself and rejects a raw host uid.
    private static string UserFlag =>
        OperatingSystem.IsLinux() ? $"--user {getuid()}:{getgid()} " : "";

    private readonly string _containerId;
    private bool _disposed;

    private DockerSandbox(string containerId) => _containerId = containerId;

    public static async Task<DockerSandbox> OpenAsync(SandboxOptions options, CancellationToken ct)
    {
        var network = options.Network is null ? "" : $"--network {Shell.QuotePosix(options.Network)} ";
        var runLine =
            $"docker run -d {UserFlag}-w {WorkMount} " +
            $"-v {Shell.QuotePosix(options.Worktree.FullName)}:{WorkMount} " +
            $"-v {Shell.QuotePosix(options.SessionDir.FullName)}:{SessionMount} " +
            $"-v {Shell.QuotePosix(options.Home.FullName)}:{HomeMount} " +
            network +
            $"{Shell.QuotePosix(options.Image)} sleep infinity";

        var result = (await RunCommand.RunAsync(runLine, ct: ct)).EnsureOk("docker run");
        var id = result.Stdout.Trim();
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"docker run returned no container id: {result.ErrorText}");
        return new DockerSandbox(id);
    }

    // The container already runs as UserFlag's uid (set on `docker run`); exec inherits it.
    public string InteractiveExecLine(string command, IReadOnlyDictionary<string, string>? env = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var envFlags = env is null
            ? ""
            : string.Concat(env.Select(kv => $"-e {Shell.QuotePosix($"{kv.Key}={kv.Value}")} "));
        return $"docker exec -it {envFlags}-w {WorkMount} {_containerId} {command}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Guaranteed anti-zombie teardown (oracle A10): rm -f kills + removes even a hung
        // box. Best-effort because dispose must not throw; the box is ephemeral regardless.
        await RunCommand.RunAsync($"docker rm -f {_containerId}", new RunCommandOptions(TimeoutMs: 30_000), CancellationToken.None);
    }
}
