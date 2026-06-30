namespace ProbeB;

// The wrapper family from authoring-dialects.md (D2 scope form). These are REAL
// methods: registering a handler stores the lambda so it can be invoked at runtime
// with a freshly-resolved scope. The generator scans these same call sites for the
// `scope.Get<T>` / `scope.Ask<T>` markers — both at once, additively.

public delegate object HandlerBody(Scope scope, object command);

public static class App
{
    static readonly Dictionary<Type, HandlerBody> _handlers = new();
    public static Container Container { get; } = new();

    // `Handler<TCommand>(scope => { ... })` — in-process send. Real registration.
    public static void Handler<TCommand>(Func<Scope, object> body)
        => _handlers[typeof(TCommand)] = (scope, _) => body(scope);

    // `Endpoint<TCommand>(scope => { ... })` — HTTP-ish. Same seam, different wrapper.
    public static void Endpoint<TCommand>(Func<Scope, object> body)
        => _handlers[typeof(TCommand)] = (scope, _) => body(scope);

    public static object Dispatch<TCommand>(TCommand command) where TCommand : notnull
    {
        if (!_handlers.TryGetValue(typeof(TCommand), out var body))
            throw new InvalidOperationException($"No handler registered for {typeof(TCommand)}.");
        return body(Container.NewScope(), command);
    }
}
