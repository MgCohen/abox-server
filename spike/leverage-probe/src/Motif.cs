namespace Probe;

// ============================================================================
// THE MOTIF COMPONENT — written ONCE, reused by every aggregate-mutation feature.
// This is the "component that carries the standards"; the author's recipe slots
// only the divergence into it. It is NOT counted as author surface (it is the
// reusable component definition, the whole point of leverage).
//
// `Mutates<TAggregate>` carries the entire aggregate-mutation scaffold:
//   - load the aggregate by key from Repo<TAggregate>      (implied)
//   - the handler shape: (Scope scope, TCommand command)   (implied)
//   - persist via Repo<TAggregate>.Save()                  (implied)
//   - return the mutated aggregate                         (implied)
//   - RegisterDiscovered for Repo<TAggregate>              (implied — falls out
//                                                            of the motif's own
//                                                            scope.Get marker)
//
// The author supplies only:
//   - terse model declarations (command + any minted records), and
//   - the divergence: the irreducible business logic over `agg` and `command`.
//
// Naming: TAggregate's name + the command derive the feature name and namespace
// by convention, so the author need not restate them.
// ============================================================================

static class Feature
{
    // The one motif call the author writes. Everything load/save/return/wire is
    // implied by `Mutates<TAggregate>`; `with` is ONLY the divergence.
    //
    //   key      : how to derive the aggregate's load key from the command, as the
    //              C# expression the author would write (e.g. "command.Email").
    //              A string for now — apples-to-apples with the slice's string glue;
    //              typing it is the separate §8 #10 lambda-leaf decision (out of scope).
    //   command  : terse "Name(field:Type, ...)" — the trigger model, minted.
    //   models   : terse "Name(field:Type, ...)" lines — extra minted records the
    //              divergence builds (one per line).
    //   with     : the divergence body — ONLY the business logic. It may use `scope`
    //              (Ask cross-module), `command`, the minted models, and `agg`
    //              (the loaded aggregate the motif binds). No load/save/return here.
    public static MutationRecipe Mutates<TAggregate>(
        string key,
        string command,
        string models,
        string with)
        where TAggregate : notnull
        => new(
            Aggregate: typeof(TAggregate).Name,
            LoadKey: key,
            Command: TerseModel.Parse(command),
            Models: TerseModel.ParseAll(models),
            Divergence: with);
}

// What the author authored, captured. The emitter expands it into the full feature
// by re-introducing everything the motif implies.
sealed record MutationRecipe(
    string Aggregate,
    string LoadKey,
    TerseModel Command,
    IReadOnlyList<TerseModel> Models,
    string Divergence)
{
    // Conventions the motif derives so the author never restates them: the feature
    // name is the command name minus its "Command" suffix; the namespace groups
    // features by the aggregate they mutate.
    public string FeatureName => Command.Name.EndsWith("Command")
        ? Command.Name[..^"Command".Length]
        : Command.Name;
    public string Namespace => $"Acme.{Aggregate}s";
    public string Repo => $"Repo<{Aggregate}>";
    public string AggregateVar => "agg";

    // Every minted record this feature emits: the command plus the author's models.
    public IEnumerable<TerseModel> AllModels => new[] { Command }.Concat(Models);
}
