Template: [template.md](./template.md)
Harness: [Rulebook convention](../../../../tests/Harness/README.md)

### a ping flow with a trivial prompt → the agent replies and the run completes
- **Why:** the simplest real-CLI proof — a live `claude`/`codex` session drives a ping flow to a Completed
  terminal carrying the agent's own reply, which no scripted provider can establish.

### an autonomous agent given an under-specified prompt → self-resolves and completes without Needs input
- **Why:** an Autonomous role must drive its own blocking question through the auto-resolver and reach
  Completed, never stalling on "Needs input:" — the live proof of the autonomy resume loop.

### an agent given a file request → writes the file into the project on disk
- **Why:** only a real CLI session actually edits the project; this proves the agent's tool use lands the
  exact requested bytes in the working directory, end to end.

### Ask policy with a gated tool and Allow → the hook fires and the write goes through
- **Why:** proves PreToolUse fires over ConPTY, carries the tool payload to the resolver, and an Allow lets
  the gated write complete — the live permission-request path.

### Ask policy with a gated tool and a denial → the write is blocked and the run does not hang
- **Why:** a null/deny resolution must block the gated tool and return cleanly, replacing acceptEdits' silent
  mid-turn hang.

### Auto policy with a gated tool → the tool runs without consulting the resolver
- **Why:** Auto auto-approves through the same gate without a human — the write runs even though the resolver
  would have denied, and the resolver is never consulted.

### a Human agent given a registry answer → resumes and completes
- **Why:** InteractiveResolver parks the question and a registry answer resumes the live run to Completed —
  the inbox/endpoint resume loop against a real CLI.

### a Human agent with no answer available → escalates as Needs input
- **Why:** with no resolution a Human agent must surface NeedsInput rather than hang or guess — the
  deny-on-null escalation.
