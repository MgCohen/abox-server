namespace RemoteAgents.Sessions;

// Typed session artifacts. Replaces the scattered string literals
// "claude-text.txt" / "claude-raw.txt" / "codex-review.txt" /
// "codex-review.jsonl" / "transcript.jsonl" that used to live in every
// flow file. Session is the only thing that knows the on-disk basename
// associated with each artifact.
public enum SessionArtifact
{
    Transcript    = 0,  // transcript.jsonl
    ClaudeText    = 1,  // claude-text.txt (distilled assistant text)
    ClaudeRaw     = 2,  // claude-raw.txt (full PTY buffer)
    CodexReview   = 3,  // codex-review.txt (review text)
    CodexReviewJl = 4,  // codex-review.jsonl (structured artifact)
}
