namespace ABox.Domain.Inbox;

// The generic inbox item: a title + tags with no producer-specific payload. Notes and Decision (by
// projection, sharing the item id) both push through it, so the dependency points producer -> Inbox,
// never the reverse. A typed item earns its place only when a producer needs payload this can't carry.
public sealed record NoteInboxItem : InboxItem;
