using Probe.Domain;

namespace Probe;

// ============================================================================
// THE EXTRACTION — the reusable typed component, written ONCE, reused by every
// aggregate-mutation feature. NOT counted as author surface (it is the component
// the standards live in, the whole point of the leverage).
//
// This is the answer to the rejected string motif. There, the scaffold was a string
// template and the glue was free text. Here:
//
//   - the SCAFFOLD (load -> mutate -> save -> return + repo wiring) is carried as
//     TYPED STRUCTURE: the generic TAgg fixes the aggregate, its Repo<TAgg>, and the
//     return type; TCmd fixes the trigger command. The emitter DERIVES the scaffold
//     text from these types — the author never writes load/save/return.
//
//   - the GLUE is a pair of TYPED, COMPILER-CHECKED delegates, not strings:
//       LoadBy : Func<TCmd, string>            -- how to find the aggregate
//       With   : Action<TAgg, TCmd, Scope>     -- the irreducible business divergence
//     The author writes real C# in these lambdas. The compiler checks every member
//     access against the real domain types. The emitter LIFTS the lambda bodies into
//     the owned handler via Roslyn (see Lift.cs) — it never executes them.
//
// So nothing in the author's hands is free text: the variant types are generic
// arguments, and the business logic is real code the compiler enforces.
// ============================================================================

sealed class Mutation<TAgg, TCmd>
    where TAgg : notnull
    where TCmd : notnull
{
    public Func<TCmd, string> LoadKey { get; }
    public Action<TAgg, TCmd, Scope> Divergence { get; }

    public Mutation(Func<TCmd, string> loadBy, Action<TAgg, TCmd, Scope> with)
    {
        LoadKey = loadBy;
        Divergence = with;
    }

    // The emitter reads the typed identity off the composed object (the structured
    // half); it reads the glue bodies off the recipe source (the real-code half).
    public Type AggregateType => typeof(TAgg);
    public Type CommandType => typeof(TCmd);
}

static class Feature
{
    public static MutationBuilder<TAgg, TCmd> Mutates<TAgg, TCmd>()
        where TAgg : notnull
        where TCmd : notnull
        => new();
}

sealed class MutationBuilder<TAgg, TCmd>
    where TAgg : notnull
    where TCmd : notnull
{
    Func<TCmd, string>? _loadBy;

    public MutationBuilder<TAgg, TCmd> LoadBy(Func<TCmd, string> selector)
    {
        _loadBy = selector;
        return this;
    }

    public Mutation<TAgg, TCmd> With(Action<TAgg, TCmd, Scope> divergence)
        => new(
            _loadBy ?? throw new InvalidOperationException("LoadBy(...) must be set before With(...)."),
            divergence);
}
