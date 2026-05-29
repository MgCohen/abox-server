using RemoteAgents.Events;
using RemoteAgents.Host.Runs;

namespace RemoteAgents.Host.Sinks;

// Background subscriber over the run's broadcaster that mirrors
// AgentEvent.ProviderSessionAttached onto Run.ProviderSession. Decoupled
// from the producer side — both InProcessFlowExecutor and the subprocess
// path emit events through the same ChannelSink, so this one observer
// covers every transport.
//
// Replaces the legacy ClaudeJsonlTailer's stdout-sniffing path. Provider-
// agnostic: ClaudeAgent emits ProviderSessionAttached("claude", uuid) the
// moment its session id is decided; CodexAgent does the same with
// ("codex", threadId).
public static class RunStateSink
{
    public static Task StartAsync(Run run, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in run.Sink.Broadcaster.Subscribe(ct))
                {
                    if (evt is AgentEvent.ProviderSessionAttached attached)
                        run.ProviderSession = attached.Session;
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
        }, ct);
}
