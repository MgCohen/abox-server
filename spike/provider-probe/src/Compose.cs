namespace Probe;

// ============================================================================
// THE COMPOSITION SURFACE — builders provide scope, actions don't take it.
//
//   Builder (Feature) SCAFFOLDS structure and PROVIDES a scope:
//       new Feature<TCmd>(scope => …children…)
//   The scope carries the command (scope.Command : TCmd). A Feature need not have an Endpoint.
//
//   Action (Mutate) DOES one thing. Its parameters are just (store, key, body) — NO scope.
//   The command is reached by the business-logic LEAVES through the ambient scope
//   (scope.Command.Email / scope.Command.Points), not threaded through the action.
//
// Nothing here executes. It exists to TYPE-CHECK (so the swap is enforced: `key: TArgs`
// forces the key to the store's arg type) and to be LIFTED from source by the emitter.
// ============================================================================

public sealed class Scope<TCmd>
{
    public TCmd Command => throw new InvalidOperationException("scope.Command is compile-time only; never executed.");
}

public abstract record Node;

public sealed record Feature<TCmd>(Func<Scope<TCmd>, Node> Build) : Node;

public sealed record MutateNode : Node;

public static class Compose
{
    public static Node Mutate<TArgs, TReturn>(
        IStore<TArgs, TReturn> store,
        TArgs key,
        Action<TReturn> body)
        where TReturn : notnull
        => new MutateNode();
}
