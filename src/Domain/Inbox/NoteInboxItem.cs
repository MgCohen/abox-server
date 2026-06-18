namespace ABox.Domain.Inbox;

// Provisional: a stand-in item so the Inbox runs standalone before Decision (the first real
// InboxItem) lands — the dependency points Decision -> Inbox, never the reverse.
public sealed record NoteInboxItem : InboxItem;
