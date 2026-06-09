using Flows;
using Flows.Contracts;
using Infra.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;
using Notifications;
using Notifications.Contracts;

var services = new ServiceCollection();
services.AddSingleton<Dispatcher>();
services.AddSingleton<IEventBus, InProcessEventBus>();
services.AddSingleton<IPipelineBehavior, AuditBehavior>();
services.AddFlows();
services.AddNotifications();

var provider = services.BuildServiceProvider();
var dispatcher = provider.GetRequiredService<Dispatcher>();

var run = await dispatcher.Send<RunFlowRequest, RunFlowResponse>(new RunFlowRequest("card-framework"));
Console.WriteLine($"ran flow {run.FlowId} -> {run.Status}");

var snapshot = await dispatcher.Send<GetFlowSnapshotRequest, FlowSnapshotDto?>(new GetFlowSnapshotRequest(run.FlowId));
Console.WriteLine($"snapshot: {snapshot?.Project} / {snapshot?.Status} / phases={snapshot?.Phases.Count}");

var notes = await dispatcher.Send<ListNotificationsRequest, IReadOnlyList<NotificationDto>>(new ListNotificationsRequest());
foreach (var note in notes)
    Console.WriteLine($"note: {note.Message}");
