namespace RemoteAgents.Agents;

// Claude's first-run TUI dialogs that block the prompt. DetectStartupDialog
// classifies the splash buffer into one of these so MaybeDismissDialogAsync
// can map to keystrokes via a typed switch (was a "trust"/"bypass-warning"
// magic-string contract between the two methods).
public enum StartupDialog
{
    // "Do you trust this folder?" / "Is this a project you ..."
    Trust,
    // "Bypass Permissions mode" / "Yes, I accept"
    BypassWarning,
}
