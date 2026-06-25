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
        // A credentialed box is leak-safe only with no route out (ADR 0013): verify the
        // network is actually --internal, not merely named, before the box ever holds a secret.
        if (options.RequireInternalNetwork)
            await EnsureInternalNetworkAsync(options.Network, ct);

        var network = options.Network is null ? "" : $"--network {Shell.Quote(options.Network)} ";
        // PID 1 is `sleep infinity` via an explicit entrypoint: the box image's own
        // ENTRYPOINT is /bin/sh, so a bare `sleep infinity` CMD would run `/bin/sh sleep …`
        // and exit. The agent turn runs later through `docker exec`, which ignores this.
        var runLine =
            $"docker run -d --entrypoint sleep {UserFlag}-w {WorkMount} " +
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
    // Interactive (-it): a PTY-driven CLI that needs a tty (claude). Pipe (-i): a CLI driven
    // over stdin/stdout with no tty (codex exec).
    public string InteractiveExecLine(string command, IReadOnlyDictionary<string, string>? env = null) =>
        ExecLine("-it", command, env);

    public string PipeExecLine(string command, IReadOnlyDictionary<string, string>? env = null) =>
        ExecLine("-i", command, env);

    private string ExecLine(string flags, string command, IReadOnlyDictionary<string, string>? env)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return $"docker exec {flags} {EnvFlags(env)}-w {WorkMount} {_containerId} {command}";
    }

    private static string EnvFlags(IReadOnlyDictionary<string, string>? env) =>
        env is null ? "" : string.Concat(env.Select(kv => $"-e {Shell.Quote($"{kv.Key}={kv.Value}")} "));

    private static async Task EnsureInternalNetworkAsync(string? network, CancellationToken ct)
    {
        var inspect = await RunCommand.RunAsync(
            $"docker network inspect -f {Shell.Quote("{{.Internal}}")} {Shell.Quote(network ?? "")}", ct: ct);
        if (inspect.ExitCode != 0 || inspect.Stdout.Trim() != "true")
            throw new InvalidOperationException(
                $"A credentialed box must run on an --internal docker network with no route out (ADR 0013); " +
                $"network '{network ?? "(none)"}' reports Internal='{inspect.Stdout.Trim()}'. " +
                "Attach it to the egress sidecar's abox-boxnet, or clear the credential for an unbilled turn.");
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
