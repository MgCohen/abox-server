namespace ABox.Infrastructure.Sandbox;

public sealed record ExecResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;

    public override string ToString()
    {
        var stream = string.IsNullOrEmpty(Stderr) ? Stdout : Stderr;
        var trimmed = stream.Length > 500 ? stream[..500] + "…" : stream;
        return $"exit {ExitCode}: {trimmed.Trim()}";
    }
}
