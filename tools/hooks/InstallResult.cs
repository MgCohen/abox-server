namespace ABox.Governance.Hooks;

public sealed record InstallResult(bool Installed, string Message)
{
    public override string ToString() => Message;
}
