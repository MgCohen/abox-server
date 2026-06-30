namespace ABox.Governance.Hooks;

public sealed record GitInstallResult(bool Installed, string Message)
{
    public override string ToString() => Message;
}
