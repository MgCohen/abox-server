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
        var network = options.Network is null ? "" : $"--network {Shell.Quote(options.Network)} ";
        // The credential (and any container-scoped env) is set here, at `docker run`, so a
        // later `docker exec` inherits it without the value ever reaching the exec line the
        // driving PTY echoes into its buffer (ADR 0013: token stays off the agent transcript).
        var containerEnv = EnvFlags(options.ContainerEnv);
        // PID 1 is `sleep infinity` via an explicit entrypoint: the box image's own
        // ENTRYPOINT is /bin/sh, so a bare `sleep infinity` CMD would run `/bin/sh sleep …`
        // and exit. The agent turn runs later through `docker exec`, which ignores this.
        var runLine =
            $"docker run -d --entrypoint sleep {UserFlag}{containerEnv}-w {WorkMount} " +
            $"-v {Shell.Quote(options.Worktree.FullName)}:{WorkMount} " +
            $"-v {Shell.Quote(options.SessionDir.FullName)}:{SessionMount} " +
            $"-v {Shell.Quote(options.Home.FullName)}:{HomeMount} " +
            network +
            $"{Shell.Quote(options.Image)} infinity";

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
        return $"docker exec -it {EnvFlags(env)}-w {WorkMount} {_containerId} {command}";
    }

    private static string EnvFlags(IReadOnlyDictionary<string, string>? env) =>
        env is null ? "" : string.Concat(env.Select(kv => $"-e {Shell.Quote($"{kv.Key}={kv.Value}")} "));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        // Guaranteed anti-zombie teardown (oracle A10): rm -f kills + removes even a hung
        // box. Best-effort because dispose must not throw; the box is ephemeral regardless.
        await RunCommand.RunAsync($"docker rm -f {_containerId}", new RunCommandOptions(TimeoutMs: 30_000), CancellationToken.None);
    }
}
