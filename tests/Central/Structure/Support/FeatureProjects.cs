namespace ABox.Tests.Central.Structure.Support;

// The csproj tally of one feature folder, split by role. The canonical slice (ADR 0011 D2, as amended by the
// contract-publishing split) is exactly one implementation project (verbs as folders, Module folded in) plus its
// leaves: at most one external Api leaf and at most one internal Contract leaf, and at least one of the two.
internal sealed record FeatureProjects(int Implementation, int Api, int Contract)
{
    public int Leaves => Api + Contract;

    public bool IsCanonical => Implementation == 1 && Api <= 1 && Contract <= 1 && Leaves >= 1;

    public override string ToString() => $"{Implementation} impl + {Api} api + {Contract} contract";
}
