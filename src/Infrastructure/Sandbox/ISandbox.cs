namespace ABox.Infrastructure.Sandbox;

public interface ISandbox : IAsyncDisposable
{
    // The host-side command line that runs `command` interactively inside the box
    // (a TTY allocated in the box, env applied). The caller drives it through its own
    // PTY — the transport for a real claude turn under ADR 0013's host-PTY model.
    string InteractiveExecLine(string command, IReadOnlyDictionary<string, string>? env = null);
}
