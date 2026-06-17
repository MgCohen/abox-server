# Stacked Review Orchestrator — superseded

> **Superseded by [`the-box.md`](the-box.md).** This doc sketched a swipe-style review
> over a self-healing PR stack. That stack is now a *component inside* the **Box**
> model, and its mechanics (node state machine, two-level merge, restack engine,
> conflict-tier ladder, ports & adapters, walking-skeleton build order) are folded
> into `the-box.md`, alongside several decisions that **changed**: swipe direction
> (left = approve), approval invalidation (ground-up review makes it a rare backstop,
> not the workhorse), the integration-branch target (a Box branch, not `main`), and
> the Inbox unifying the old review-surface + notifier ports.
>
> The original content lives in git history. **`the-box.md` governs.**
