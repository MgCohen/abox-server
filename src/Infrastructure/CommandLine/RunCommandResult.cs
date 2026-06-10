namespace RemoteAgents.Tools.CommandLine;

public sealed record RunCommandResult(
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    long DurationMs)
{
    public string ErrorText => string.IsNullOrEmpty(Stderr) ? Stdout : Stderr;

    public RunCommandResult EnsureOk(string op) =>
        ExitCode == 0 ? this : throw new InvalidOperationException($"{op} failed: {ErrorText}");
}
