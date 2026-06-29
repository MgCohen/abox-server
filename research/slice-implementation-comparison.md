---
type: research
status: comparison
tags: [#architecture, #vsa, #mediator, #wolverine, #fastendpoints, #saga]
related: [[contract-axis-reference-comparison]], [[riverbooks-module-sharing]]
---

# Current vs MediatR vs Wolverine, per vertical slice

> **Why this exists.** We are weighing whether to add an in-process mediator (and
> with it, native sagas) behind our FastEndpoints edge. This doc shows the *same*
> slice authored three ways — today's direct style, MediatR, Wolverine — across the
> cases that actually differ, so the trade is concrete rather than vibes.

The constant in all three: **the HTTP edge stays FastEndpoints, the push edge stays
SignalR.** A mediator never touches the wire — it sits behind `HandleAsync`. The
variable is *what the endpoint delegates to*, and *how slices talk to each other*.

---

## 1. Simple vertical slice — create a project

### Current (no mediator — logic lives in the endpoint)

```csharp
internal sealed class AddProjectEndpoint(IProjectRepository projects)
    : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure()
    {
        Post("/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length == 0) { AddError(r => r.Name, "Required."); await Send.ErrorsAsync(400, ct); return; }
        if (await projects.GetByName(name, ct) is not null) { AddError(r => r.Name, "Exists."); await Send.ErrorsAsync(409, ct); return; }

        var project = Project.Create(name, req.Path!.Trim());
        await projects.Add(project, ct);
        await Send.OkAsync(new ProjectDto(project.Id, project.Name, project.Path), ct);
    }
}
```

### MediatR (typed request/handler behind a thin endpoint)

```csharp
public sealed record CreateProject(string Name, string Path) : IRequest<ProjectDto>;

public sealed class CreateProjectHandler(IProjectRepository projects)
    : IRequestHandler<CreateProject, ProjectDto>
{
    public async Task<ProjectDto> Handle(CreateProject cmd, CancellationToken ct)
    {
        var project = Project.Create(cmd.Name.Trim(), cmd.Path.Trim());
        await projects.Add(project, ct);
        return new ProjectDto(project.Id, project.Name, project.Path);
    }
}
```

### Wolverine (static handler, convention-discovered)

```csharp
public sealed record CreateProject(string Name, string Path);

public static class CreateProjectHandler
{
    public static async Task<ProjectDto> Handle(
        CreateProject cmd, IProjectRepository projects, CancellationToken ct)
    {
        var project = Project.Create(cmd.Name.Trim(), cmd.Path.Trim());
        await projects.Add(project, ct);
        return new ProjectDto(project.Id, project.Name, project.Path);
    }
}
```

| | Logic location | Handler contract | Enforced by |
|---|---|---|---|
| Current | inside the endpoint | n/a | nothing — it's just a method body |
| MediatR | `IRequestHandler<,>` | **typed interface** | the compiler |
| Wolverine | `static Handle` | **convention** (method name + first param) | a build-time scan, not the compiler |

---

## 2. Cross-feature interaction — Inbox reacts to "project created"

This is the case that separates the three. The producer (Projects) must not know the
consumer (Inbox) exists.

### Current (no bus — you inject the collaborator = coupling)

```csharp
// Projects must take a dependency on Inbox to "notify" it. The slices are now coupled,
// and adding a second reaction means editing this handler again.
internal sealed class AddProjectEndpoint(IProjectRepository projects, IInbox inbox)
    : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var project = Project.Create(req.Name!.Trim(), req.Path!.Trim());
        await projects.Add(project, ct);
        await inbox.Add(new NoteInboxItem { Title = $"Project {project.Id} created" }, ct);  // ← coupling
        await Send.OkAsync(new ProjectDto(project.Id, project.Name, project.Path), ct);
    }
}
```

### MediatR (publish a notification; Inbox subscribes)

```csharp
public sealed record ProjectCreated(Guid Id) : INotification;

public sealed class CreateProjectHandler(IProjectRepository projects, IMediator mediator)
    : IRequestHandler<CreateProject, ProjectDto>
{
    public async Task<ProjectDto> Handle(CreateProject cmd, CancellationToken ct)
    {
        var project = Project.Create(cmd.Name.Trim(), cmd.Path.Trim());
        await projects.Add(project, ct);
        await mediator.Publish(new ProjectCreated(project.Id), ct);   // ← explicit publish
        return new ProjectDto(project.Id, project.Name, project.Path);
    }
}

// in the Inbox slice — references only Projects.Contracts
public sealed class AnnounceProject(IInbox inbox) : INotificationHandler<ProjectCreated>
{
    public Task Handle(ProjectCreated evt, CancellationToken ct)
        => inbox.Add(new NoteInboxItem { Title = $"Project {evt.Id} created" }, ct);
}
```

### Wolverine (return the event; Inbox subscribes)

```csharp
public sealed record ProjectCreated(Guid Id);

public static class CreateProjectHandler
{
    public static async Task<(ProjectDto, ProjectCreated)> Handle(
        CreateProject cmd, IProjectRepository projects, CancellationToken ct)
    {
        var project = Project.Create(cmd.Name.Trim(), cmd.Path.Trim());
        await projects.Add(project, ct);
        return (new ProjectDto(project.Id, project.Name, project.Path),
                new ProjectCreated(project.Id));   // ← event is a return value (or publish via IMessageBus to stay explicit)
    }
}

// in the Inbox slice — references only Projects.Contracts
public static class AnnounceProject
{
    public static Task Handle(ProjectCreated evt, IInbox inbox, CancellationToken ct)
        => inbox.Add(new NoteInboxItem { Title = $"Project {evt.Id} created" }, ct);
}
```

| | Producer's deps | Add a 2nd reaction | Decoupling |
|---|---|---|---|
| Current | grows per consumer (`IInbox`, `IHubContext`, …) | edit the producer | none — direct coupling |
| MediatR | `IMediator` only | add a handler | via `INotification` |
| Wolverine | none (or `IMessageBus`) | add a handler | via the bus |

> Only do this when the event has ≥2 real reactions or must exist anyway (a saga
> waiting on it). A 1:1 "raise event → single push" is ceremony — push inline.

---

## 3. The endpoint — in theory, shouldn't change (and doesn't)

The endpoint stays FastEndpoints in all three. Current bakes the logic in; MediatR and
Wolverine make it a thin delegator.

### Current

```csharp
public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
{
    // full business logic inline (see §1)
}
```

### MediatR

```csharp
internal sealed class AddProjectEndpoint(IMediator mediator) : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure() { Post("/projects"); AllowAnonymous(); }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var dto = await mediator.Send(new CreateProject(req.Name, req.Path), ct);
        await Send.OkAsync(dto, ct);
    }
}
```

### Wolverine

```csharp
internal sealed class AddProjectEndpoint(IMessageBus bus) : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure() { Post("/projects"); AllowAnonymous(); }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var dto = await bus.InvokeAsync<ProjectDto>(new CreateProject(req.Name, req.Path), ct);
        await Send.OkAsync(dto, ct);
    }
}
```

The MediatR and Wolverine endpoints are byte-for-byte the same except `IMediator.Send`
vs `IMessageBus.InvokeAsync`. The wire layer is genuinely unaffected by the choice.

---

## 4. Configure / subscribe handlers — wiring

### Current (FastEndpoints assembly scan; no handler concept)

```csharp
builder.Services.AddFastEndpoints();          // discovers Endpoint<,> by assembly scan
// each feature's Module exposes EndpointsAssembly for discovery
app.UseFastEndpoints();
```
Handlers don't exist as a concept — there's nothing to subscribe. Cross-feature reactions
are direct method calls (§2), so there's no registration step and no fan-out.

### MediatR (interface-based registration)

```csharp
builder.Services.AddFastEndpoints();
builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblies(/* feature assemblies */));
// every IRequestHandler<,> and INotificationHandler<> is registered by its interface
app.UseFastEndpoints();
```
Subscription = *implement the interface*. The compiler guarantees the handler's shape;
DI discovers it by the closed interface.

### Wolverine (convention discovery)

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ProjectsModule).Assembly);   // scan feature assemblies
    opts.Policies.AutoApplyTransactions();                             // outbox: publish only on commit
    opts.Durability.Mode = DurabilityMode.Solo;                        // in-process, no broker
});
builder.Services.AddFastEndpoints();
app.UseFastEndpoints();
```
Subscription = *a method named `Handle` whose first param is the message*. No interface;
discovery is by convention at scan time. A typo silently un-registers the handler — so
enforcement, if you want it, is an arch-test, not the compiler.

---

## Verdict per case

| Case | Current | MediatR | Wolverine |
|---|---|---|---|
| Simple slice | logic-in-endpoint, no contract | typed handler, compiler-enforced | static handler, convention-enforced |
| Cross-feature | **coupled** (direct injection) | decoupled, typed `INotification` | decoupled, loose return/publish |
| Endpoint | logic inline | thin delegator | thin delegator (identical) |
| Wiring | assembly scan, no handlers | register by interface | register by convention |
| **Sagas** | hand-rolled (the Flow engine) | **none — also hand-rolled** | **native `Saga`** |
| License | — | **commercial** | free |
| Fits "enforceable for agents" | partial (no decoupling) | **strong** (compiler) | weak at author time (needs arch-tests) |

## The honest summary

- **Current → MediatR** is the *enforceability* upgrade: it adds typed, decoupled
  cross-feature messaging the compiler checks. It does **not** solve sagas, and it's now
  a paid dependency.
- **Current → Wolverine** is the *capability* upgrade: native sagas + outbox +
  scheduling, free — but its handler model is convention-over-interface, which trades
  away exactly the author-time enforcement we want and must be recovered with arch-tests.
- **The fork:** if the only thing pulling us toward Wolverine is sagas, weigh
  "Wolverine quarantined to the orchestration slice" (or a hardened typed Flow engine)
  against swallowing its loose mediator idiom across every slice. Dispatch is everywhere;
  the saga is one slice. Don't loosen the whole surface to fix one concern.
