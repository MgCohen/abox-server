using Probe.Domain;
using static Probe.Compose;

namespace Probe;

// THE SAME FEATURE, provider = Repo. `new Feature<AddPointsCommand>(scope => …)` is the
// builder — it fixes TCmd and hands `scope` down. `Mutate(scope, …)` is the action (the
// canonical catalog form). Because via is a Repo (IStore<string,User>), the key MUST return
// a string — `c.Email` type-checks. The body is provider-agnostic. Everything between the
// AUTHORED markers is the measured surface.
public static class RepoRecipe
{
    // === AUTHORED (begin) ===
    public static Node AddPoints() =>
        new Feature<AddPointsCommand>(scope =>
            Mutate(scope,
                via:  Stores.Repository<User>(),
                key:  c => c.Email,
                body: (user, c) => user.AddPoints(c.Points)));
    // === AUTHORED (end) ===
}
