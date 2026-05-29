#:package Microsoft.AspNetCore.SignalR.Client@10.0.7
#:project ../../src/RemoteAgents/RemoteAgents.csproj
#:project ../RemoteAgents.UI.Components/RemoteAgents.UI.Components.csproj

// signalr-stream-smoke.cs — fire a run via Host REST, then subscribe to
// /hub/runs Stream(runId) and print each AgentEvent as it arrives until
// the stream completes. Smoke for Layer D (SignalR delivery) without
// needing a browser.
//
// Usage:
//   dotnet run ui/scripts/signalr-stream-smoke.cs                       (kicks claude-only against remote-unity-agents)
//   dotnet run ui/scripts/signalr-stream-smoke.cs <host>                (override host base)
//   dotnet run ui/scripts/signalr-stream-smoke.cs <host> <existing-id>  (subscribe to an existing runId without POSTing)

using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using RemoteAgents.Events;
using RemoteAgents.UI.Components.Models;

// File-based programs disable reflection-based STJ by default; the smoke
// is plumbing-only and not worth wiring source-gen for. Flip it back on.
AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);

var hostBase = args.Length > 0 ? args[0] : "http://localhost:5062";
var existingRunId = args.Length > 1 ? args[1] : null;

Guid runId;
if (existingRunId is not null)
{
    runId = Guid.Parse(existingRunId);
    Console.WriteLine($"Subscribing to existing run {runId}");
}
else
{
    using var http = new HttpClient { BaseAddress = new Uri(hostBase) };
    var resp = await http.PostAsJsonAsync("runs", new
    {
        project = "remote-unity-agents",
        flow = "claude-only",
        prompt = "Just say the word hello and stop. Do not read or write any files.",
        args = (string[]?)null,
    });
    resp.EnsureSuccessStatusCode();
    var run = await resp.Content.ReadFromJsonAsync<RunSummary>()
        ?? throw new InvalidOperationException("null run summary");
    runId = run.Id;
    Console.WriteLine($"POST /runs → {runId} (status {run.Status})");
}

await using var conn = new HubConnectionBuilder()
    .WithUrl($"{hostBase}/hub/runs")
    .Build();

await conn.StartAsync();
Console.WriteLine($"hub connected ({conn.ConnectionId})");
Console.WriteLine($"streaming events for {runId} …");
Console.WriteLine();

// Run both streams concurrently. The agent-event stream is the existing
// raw PTY pipeline; the chat-event stream is the C7 JSONL-tail pipeline.
var nEvent = 0;
var nChat = 0;

var agentTask = Task.Run(async () =>
{
    await foreach (var evt in conn.StreamAsync<AgentEvent>("Stream", runId))
    {
        nEvent++;
        var tag = evt.GetType().Name;
        var payload = evt switch
        {
            AgentEvent.Started s         => $"prompt='{Truncate(s.Prompt, 60)}'",
            AgentEvent.StreamChunk c     => $"chars={c.Chunk.Length}",
            AgentEvent.DialogDismissed d => $"match='{d.Match}'",
            AgentEvent.Phase p           => $"{p.Status}: {Truncate(p.Detail, 60)}",
            AgentEvent.Completed done    => $"exit={done.ExitCode} chars={done.OutputChars}",
            AgentEvent.Failed f          => $"{f.ExceptionType ?? "?"}: {Truncate(f.Reason, 80)}",
            _                            => "(unknown)",
        };
        Console.WriteLine($"[event {nEvent:D3}] {tag,-18} {payload}");
    }
});

var chatTask = Task.Run(async () =>
{
    await foreach (var evt in conn.StreamAsync<ChatEvent>("StreamChat", runId))
    {
        nChat++;
        var tag = evt.GetType().Name;
        var payload = evt switch
        {
            ChatEvent.UserText u      => $"text='{Truncate(u.Text, 80)}'",
            ChatEvent.AssistantText a => $"text='{Truncate(a.Text, 80)}'",
            ChatEvent.Thinking t      => $"chars={t.Text.Length}",
            ChatEvent.ToolUse tu      => $"{tu.Name}({Truncate(tu.InputJson, 50)})",
            ChatEvent.ToolResult tr   => $"useId={tr.ToolUseId} err={tr.IsError} chars={tr.Content.Length}",
            ChatEvent.Meta m          => $"{m.Tag}: {Truncate(m.Detail, 50)}",
            _                         => "(unknown)",
        };
        Console.WriteLine($"[chat  {nChat:D3}] {tag,-18} {payload}");
    }
});

await Task.WhenAll(agentTask, chatTask);

Console.WriteLine();
Console.WriteLine($"streams ended — {nEvent} agent events, {nChat} chat events.");

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

public sealed record RunSummary(Guid Id, string Project, string Flow, string Prompt, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? SessionId, string? SessionDir,
    int? ExitCode, string? FailureReason);
