using Probe.Domain;

namespace Probe;

// ============================================================================
// THE SCAFFOLD — Mutate requires the uniform IStore, so the `key` selector is forced
// to produce THAT provider's TArgs. This is where the type-safe swap lives.
//
//   Feature.For<TCmd>()               fixes the command type (the binding surface —
//                                     in the real system the wrapper supplies it).
//     .Mutate(via, key, body)         via  : IStore<TArgs, TReturn>  (the provider)
//                                     key  : Func<TCmd, TArgs>        (MUST match TArgs)
//                                     body : Action<TReturn, TCmd>    (the divergence)
//
// TArgs + TReturn are inferred from `via`; TCmd from `For`. The author writes only the
// provider, the key, and the body. Swap `via` and the compiler rejects a stale key.
//
// key/body are never executed — they exist to be type-checked; the emitter lifts their
// SYNTAX from the recipe source (Lift.cs). `via` is read for its Lowering (which
// idiomatic load/save methods to emit).
// ============================================================================

public static class Feature
{
    public static FeatureBuilder<TCmd> For<TCmd>() => new();
}

public sealed class FeatureBuilder<TCmd>
{
    public Mutation Mutate<TArgs, TReturn>(
        IStore<TArgs, TReturn> via,
        Func<TCmd, TArgs> key,
        Action<TReturn, TCmd> body)
        where TReturn : notnull
        => new(via.Lowering, typeof(TCmd).Name, typeof(TReturn).Name);
}

public sealed record Mutation(StoreLowering Lowering, string Command, string Return)
{
    public string FeatureName => Command.EndsWith("Command") ? Command[..^"Command".Length] : Command;
}
