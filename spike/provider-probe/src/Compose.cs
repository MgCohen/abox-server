namespace Probe;

// ============================================================================
// THE COMPOSITION SURFACE — builders vs actions.
//
//   Builders (Feature, Endpoint) SCAFFOLD structure (folder, namespace, DI, route) and
//   PROVIDE a scope: `new Feature<TCmd>(scope => …children…)`. TCmd flows DOWN through the
//   builder lambda — that is the binding surface. A Feature need not have an Endpoint.
//
//   Actions (Mutate, Ask, Emit) DO one thing at a point. They CONSUME the ambient scope
//   (to recover TCmd) but open none — leaves, no lambda of their own.
//
// The canonical action form is the free function `Mutate(scope, via, key, body)` (sibling
// of `Ask(scope, q)`); `scope.Mutate(...)` is generated sugar over it. `new Mutate<>()`
// is NOT used: a C# constructor cannot infer the class type params (TCmd from scope, TArgs
// from via), so the free function — which infers all three — is the working leaf.
//
// None of this is executed. It exists to TYPE-CHECK (so the swap is enforced) and to be
// LIFTED from source by the emitter (Lift.cs).
// ============================================================================

public sealed class Scope<TCmd>;

public abstract record Node;

public sealed record Feature<TCmd>(Func<Scope<TCmd>, Node> Build) : Node;

public sealed record MutateNode : Node;

public static class Compose
{
    public static Node Mutate<TCmd, TArgs, TAgg>(
        Scope<TCmd> scope,
        IStore<TArgs, TAgg> via,
        Func<TCmd, TArgs> key,
        Action<TAgg, TCmd> body)
        where TAgg : notnull
        => new MutateNode();
}

public static class ScopeSugar
{
    public static Node Mutate<TCmd, TArgs, TAgg>(
        this Scope<TCmd> scope,
        IStore<TArgs, TAgg> via,
        Func<TCmd, TArgs> key,
        Action<TAgg, TCmd> body)
        where TAgg : notnull
        => Compose.Mutate(scope, via, key, body);
}
