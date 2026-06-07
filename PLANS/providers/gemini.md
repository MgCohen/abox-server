# Implementation plan — Gemini CLI

The best **new native** provider: `gemini -p` is genuinely headless, bills against an
**OAuth-cached credential** (no isatty gate), and emits `stream-json`. So it's a clean
`SubprocessSession` provider, structurally a sibling of `CodexProvider`. See [`README.md`](README.md)
for shared seam mechanics.

## How the provider would look

### Config

```csharp
public sealed record GeminiConfig(
    string Name, string Description, string Model, string SystemPrompt,
    string ApprovalMode = "yolo",          // non-interactive: auto-approve tool calls
    int TimeoutMs = 120_000)
    : AgentConfig(Name, Description, Model, SystemPrompt);
```

### Provider (mirrors `CodexProvider`)

```csharp
public sealed class GeminiProvider(GeminiConfig config) : IProvider
{
    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var args = GeminiProtocol.BuildArgs(request.SessionId, config.Model, config.ApprovalMode);
        await using var session = SubprocessSession.Start(BuildStartInfo(request, args), ct);

        await session.StandardInput.WriteAsync(Compose(config.SystemPrompt, request.Prompt));
        session.CompleteStdin();

        var exit = await session.WaitForExitAsync(config.TimeoutMs, ct);
        return GeminiProtocol.Normalize(session.RawStdout, request.SessionId, exit);
    }

    private ProcessStartInfo BuildStartInfo(AgentRunRequest request, List<string> args)
    {
        var commandLine = "gemini " + string.Join(' ', args.Select(Shell.QuoteArg));
        return new ProcessStartInfo
        {
            FileName = Shell.CmdExePath,                 // gemini is an npm .cmd shim on PATH
            Arguments = $"/c {commandLine}",
            WorkingDirectory = request.ProjectDir,       // gemini uses CWD as project root
            RedirectStandardInput = true,
        };
    }
}
```

### Args + parser

```csharp
public static List<string> BuildArgs(string? sessionId, string model, string approvalMode)
{
    var args = new List<string> { "--output-format", "stream-json" };
    if (!string.IsNullOrEmpty(approvalMode)) { args.Add("--approval-mode"); args.Add(approvalMode); }
    if (!string.IsNullOrEmpty(model))        { args.Add("-m"); args.Add(model); }
    if (sessionId is not null)               { args.Add("--resume"); args.Add(sessionId); }   // see trap
    return args;                                                                              // prompt via stdin
}
```

`GeminiProtocol.Normalize(rawStdout, …)` parses the JSONL event stream — structurally identical to
`CodexProtocol.ExtractTranscript`, just different event names:

| Gemini event | → maps to |
|---|---|
| `init` | capture `session_id` |
| `message` (role `assistant`, `delta:true` chunks) | accumulate → `AgentTurn(Text)` |
| `tool_use` | `AgentTurn(ToolUse, {name,input})` |
| `tool_result` | `AgentTurn(ToolResult, output)` |
| `result` | final `response` text + token/latency stats |

Factory arm: `GeminiConfig g => new Agent(g, new GeminiProvider(g))`. Optional catalog entry, e.g. a
`Summarizer`/`Classifier` `GeminiConfig` using `gemini-3-flash`.

## Traps / concerns from the docs

- **OAuth-in-subprocess is the real risk.** The free/sub tier reads cached creds from
  `~/.gemini/oauth_creds.json`; open issues show a launched subprocess sometimes fails to find them
  if `CWD`/`HOME` is wrong (#5474) or drops into ACP mode and re-prompts for login (#12042).
  **De-risk first:** seed creds via one interactive `gemini` login, then prove a spawned
  `gemini -p` picks them up with the right `USERPROFILE`/working dir. This is the gate on the whole
  plan.
- **JSON mode has exited on *non-fatal* tool errors** (#9281) — parse defensively; treat a missing
  `result` event as a soft failure and fall back to concatenated `message` text.
- **`--resume <id>` in pure `-p` mode is undocumented** — verify it actually resumes headlessly
  before relying on it; sessions persist at `~/.gemini/tmp/<project_hash>/chats/` as a fallback.
- **Quota is mid-migration** to a "compute-used" model; the CLI still publishes per-day request
  numbers but they may shift.
- **Exit codes are meaningful:** `0` ok, `1` API error, `42` input error, `53` turn-limit — surface
  them in `DriveResult.ExitCode`.

## Extra steps

- Install Node 20+ then `npm install -g @google/gemini-cli`; verify `gemini --version` (add to guard).
- **One-time interactive login:** run `gemini`, pick "Login with Google", complete the browser OAuth.
  This writes `~/.gemini/oauth_creds.json`, which all later headless runs reuse.
- Optional: to *force* the free/OAuth tier and avoid accidental per-token billing, have the guard
  forbid `GEMINI_API_KEY`/`GOOGLE_API_KEY` for this provider.

## Runs on your PC?

**Yes, trivially.** The model runs on Google's servers; your PC only runs the Node CLI (tens of MB
RAM, no GPU). Windows is fully supported. Your 32 GB / 8 GB VRAM is irrelevant — needs network only.

## Associated costs

- **Free OAuth tier: $0** — ~**1,000 requests/day** on a personal Google account. This is the
  cheapest real frontier-class option on the board and the recommended default.
- **Subscription:** Google AI Pro **$19.99/mo** (~1,500/day) or Ultra **$100/mo** (~2,000/day), or
  Gemini Code Assist (Standard/Enterprise) — the *same* cached credential transparently inherits the
  higher ceiling, **no code change**.
- **API key (fallback, per-token):** Gemini 3 Pro **$2/M in, $12/M out** (≤200K context; $4/$18
  above); **Gemini 3 Flash $0.50/$3** with a **1M-token** context window.

## What it can / can't do

- **Can:** frontier-class coding + reasoning; **1M-token context** (Flash) — far beyond Claude/GPT,
  great for whole-repo context; tool use; structured `stream-json`; free at meaningful volume.
- **Can't:** run offline / on your hardware; sends prompts + code to Google (governance, but a
  mainstream US vendor). Headless OAuth has rough edges (the de-risk item above). Daily request caps
  on the free/sub tiers (not unlimited).
