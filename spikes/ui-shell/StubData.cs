namespace UiShell;

public record KpiTile(string Label, string Value, string? Accent = null, bool Dot = false);

public record AttentionItem(string Actor, string Tag, string Body, string? Mono, AttentionAction Action);

public enum AttentionAction { ApproveDeny, Answer, Review }

public record ActivityItem(string Glyph, string GlyphKind, string Text, string Age);

public record ActiveRun(string Name, string Project, int Percent, string Phase, string Elapsed, string Cost);

public record QuickAction(string Glyph, string Label);

public record Step(string Glyph, string State, string Name);

public static class StubData
{
    public const string Provider = "Claude · 62%";

    public static readonly KpiTile[] Kpis =
    [
        new("Runs today", "14"),
        new("Success", "86%", Accent: "green", Dot: true),
        new("Awaiting me", "3", Accent: "orange", Dot: true),
        new("Usage", "62%"),
    ];

    public static readonly AttentionItem[] Attention =
    [
        new("Implementer", "Permission", "run", "git push origin feature/deck-shuffle", AttentionAction.ApproveDeny),
        new("Reviewer (Codex)", "Question", "Empty-state — skeleton or spinner?", null, AttentionAction.Answer),
        new("SDLC Flow", "Signoff", "approve feature brief", "Inventory slots", AttentionAction.Review),
    ];

    public static readonly ActivityItem[] Activity =
    [
        new("✓", "ok", "Feature \"Card hover\" completed — eval 92/100", "28m"),
        new("↺", "muted", "Quality loop reverted change to Deck.cs (no improvement)", "1h"),
        new("⎇", "muted", "PR #127 merged", "2h"),
    ];

    public static readonly ActiveRun[] Runs =
    [
        new("Build Deck Shuffler", "Card Framework", 60, "phase 2/4 · Implementation", "4m", "$0.42"),
        new("Nightly Quality Loop", "Scaffold", 40, "scanning repository", "12m", "$0.18"),
    ];

    public static readonly QuickAction[] QuickActions =
    [
        new("💬", "New Quick Chat"),
        new("◆", "Run a Flow"),
        new("📁", "Add Project"),
    ];

    public static readonly Step[] Steps =
    [
        new("✓", "ok", "Guard subscription"),
        new("✓", "ok", "Plan changes"),
        new("●", "active", "Implement DeckShuffler"),
        new("○", "idle", "Validate"),
        new("○", "idle", "Review & commit"),
    ];

    public static readonly string[] Log =
    [
        "› reading Assets/Scripts/Deck.cs",
        "› editing DeckShuffler.cs (+42 -3)",
        "› running unity tests…",
        "✓ 18 passed",
        "› awaiting review",
    ];
}
